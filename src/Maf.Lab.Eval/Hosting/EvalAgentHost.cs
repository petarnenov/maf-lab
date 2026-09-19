using System.Threading.Channels;
using Maf.Lab.Api.Agent;
using Maf.Lab.Api.Storage;
using Maf.Lab.Domain.Chat;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Indexing;
using Maf.Lab.Retrieval.Auth;
using Maf.Lab.Retrieval.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Maf.Lab.Eval.Hosting;

/// <summary>
/// Runs chat turns through the production agent path: ChatTurnRunner → MCP client → retrieval MCP server (hosted
/// in-process on loopback unless Evals:McpEndpoint points at a running one) → tenant-scoped search.
/// </summary>
public sealed class EvalAgentHost : IAsyncDisposable
{
    private readonly WebApplication? _retrieval;
    private readonly string _workDir;

    private EvalAgentHost(ServiceProvider services, WebApplication? retrieval, string workDir)
    {
        Services = services;
        _retrieval = retrieval;
        _workDir = workDir;
    }

    public ServiceProvider Services { get; }

    public static async Task<EvalAgentHost> StartAsync(IConfiguration configuration, CancellationToken ct, Action<IServiceCollection>? overrides = null)
    {
        var options = configuration.GetSection(EvalOptions.Section).Get<EvalOptions>() ?? new EvalOptions();
        WebApplication? retrieval = null;
        var endpoint = options.McpEndpoint;
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            retrieval = Maf.Lab.Retrieval.Program.BuildApp([], b =>
            {
                b.Configuration.AddConfiguration(configuration);
                b.Configuration["Urls"] = "http://127.0.0.1:0";
                b.Logging.SetMinimumLevel(LogLevel.Warning);
                overrides?.Invoke(b.Services);
            });
            await retrieval.StartAsync(ct);
            endpoint = retrieval.Urls.First().TrimEnd('/') + "/mcp";
        }

        var workDir = Directory.CreateTempSubdirectory("maf-eval-").FullName;
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddConfiguration(configuration.GetSection("Logging")).AddSimpleConsole(o => o.SingleLine = true));
        services.AddSingleton(configuration);
        overrides?.Invoke(services);
        services.AddMafIndexing(configuration);
        services.Configure<AuthOptions>(configuration.GetSection(AuthOptions.Section));
        services.Configure<AgentOptions>(configuration.GetSection(AgentOptions.Section));
        services.PostConfigure<AgentOptions>(o => o.McpEndpoint = endpoint);
        services.AddDbContextFactory<MafDbContext>(o => o.UseSqlite($"Data Source={Path.Combine(workDir, "eval.db")}"));
        services.AddSingleton<SystemPrompt>();
        services.AddSingleton<TokenCounter>();
        services.AddSingleton<ToolAudit>();
        services.AddHttpClient("mcp");
        services.AddSingleton<IToolSource, McpToolSource>();
        services.AddSingleton<ConversationService>();
        services.AddTransient<ChatTurnRunner>();

        var provider = services.BuildServiceProvider();
        await using (var db = await provider.GetRequiredService<IDbContextFactory<MafDbContext>>().CreateDbContextAsync(ct))
        {
            await db.Database.EnsureCreatedAsync(ct);
        }
        return new EvalAgentHost(provider, retrieval, workDir);
    }

    public static Principal EvalPrincipal(string firmId) => new($"eval-{firmId}", TenantId.Firm(firmId), Role.ADVISOR, []);

    public async Task<TurnResult> AskAsync(string firmId, string question, CancellationToken ct)
    {
        var principal = EvalPrincipal(firmId);
        var auth = Services.GetRequiredService<IOptions<AuthOptions>>().Value;
        var (token, _) = DevJwt.Issue(auth, principal.UserId, principal.FirmId, principal.Role, []);
        var conversationId = await Services.GetRequiredService<ConversationService>().CreateAsync(principal, ct);
        var channel = Channel.CreateUnbounded<ChatEvent>();
        var runner = Services.GetRequiredService<ChatTurnRunner>();
        var result = await runner.RunAsync(principal, token, conversationId, question, channel.Writer, ct);
        channel.Writer.TryComplete();
        return result;
    }

    public async ValueTask DisposeAsync()
    {
        await Services.DisposeAsync();
        if (_retrieval is not null)
        {
            await _retrieval.StopAsync();
            await _retrieval.DisposeAsync();
        }
        try
        {
            Directory.Delete(_workDir, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
