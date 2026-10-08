using System.Threading.Channels;
using Maf.Lab.Api.Agent;
using Maf.Lab.Api.Agent.Jev;
using Maf.Lab.Api.Storage;
using Maf.Lab.Domain.Chat;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Indexing;
using Maf.Lab.Retrieval.Auth;
using Maf.Lab.Domain.Configuration;
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
    private readonly WebApplication? _portfolio;
    private readonly WebApplication? _bulgarianHistory;
    private readonly WebApplication? _code;
    private readonly string _workDir;

    private readonly bool _keepWorkDir;

    private EvalAgentHost(ServiceProvider services, WebApplication? retrieval, WebApplication? portfolio, WebApplication? bulgarianHistory, WebApplication? code, string workDir,
        bool keepWorkDir = false)
    {
        Services = services;
        _retrieval = retrieval;
        _portfolio = portfolio;
        _bulgarianHistory = bulgarianHistory;
        _code = code;
        _workDir = workDir;
        _keepWorkDir = keepWorkDir;
    }

    public ServiceProvider Services { get; }

    /// <param name="codeSettings">
    /// Settings for an in-process codebase server, which is then always started here, whatever Evals:CodeMcpEndpoint
    /// says: the graph-depth suite's pin (add-graph-depth-eval) only exists on a server it starts itself.
    /// </param>
    public static async Task<EvalAgentHost> StartAsync(IConfiguration configuration, CancellationToken ct, Action<IServiceCollection>? overrides = null,
        IReadOnlyDictionary<string, string?>? codeSettings = null)
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

        // The portfolio domain's server, in-process the same way unless Evals:PortfolioMcpEndpoint names a running one.
        WebApplication? portfolio = null;
        var portfolioEndpoint = options.PortfolioMcpEndpoint;
        if (string.IsNullOrWhiteSpace(portfolioEndpoint))
        {
            portfolio = Maf.Lab.Portfolio.Program.BuildApp([], b =>
            {
                b.Configuration.AddConfiguration(configuration);
                b.Configuration["Urls"] = "http://127.0.0.1:0";
                b.Logging.SetMinimumLevel(LogLevel.Warning);
            });
            await portfolio.StartAsync(ct);
            portfolioEndpoint = portfolio.Urls.First().TrimEnd('/') + "/mcp";
        }

        // The Bulgarian history domain's server (add-bulgarian-history-domain), in-process the same way.
        WebApplication? bulgarianHistory = null;
        var bulgarianHistoryEndpoint = options.BulgarianHistoryMcpEndpoint;
        if (string.IsNullOrWhiteSpace(bulgarianHistoryEndpoint))
        {
            bulgarianHistory = Maf.Lab.BulgarianHistory.Program.BuildApp([], b =>
            {
                b.Configuration.AddConfiguration(configuration);
                b.Configuration["Urls"] = "http://127.0.0.1:0";
                b.Logging.SetMinimumLevel(LogLevel.Warning);
            });
            await bulgarianHistory.StartAsync(ct);
            bulgarianHistoryEndpoint = bulgarianHistory.Urls.First().TrimEnd('/') + "/mcp";
        }

        // The codebase's server, the third domain (add-codebase-domain), the same way.
        WebApplication? code = null;
        var codeEndpoint = codeSettings is null ? options.CodeMcpEndpoint : "";
        if (string.IsNullOrWhiteSpace(codeEndpoint))
        {
            (code, codeEndpoint) = await StartCodeServerAsync(configuration, codeSettings, ct);
        }

        var workDir = Directory.CreateTempSubdirectory("maf-eval-").FullName;
        if (options.KeepWorkDir)
        {
            Console.WriteLine($"   work dir kept → {workDir}");
        }
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddConfiguration(configuration.GetSection("Logging")).AddSimpleConsole(o => o.SingleLine = true));
        services.AddSingleton(configuration);
        overrides?.Invoke(services);
        services.AddMafIndexing(configuration);
        services.Configure<AuthOptions>(configuration.GetSection(AuthOptions.Section));
        services.Configure<AgentOptions>(configuration.GetSection(AgentOptions.Section));
        services.PostConfigure<AgentOptions>(o =>
        {
            o.McpEndpoint = endpoint;
            o.Servers =
            [
                new McpServerOptions { Domain = Domains.Portfolio, Endpoint = portfolioEndpoint },
                new McpServerOptions { Domain = Domains.Codebase, Endpoint = codeEndpoint, Tools = [Maf.Lab.Domain.Code.CodeTools.Search, Maf.Lab.Domain.Graph.GraphTools.TraceCodeSymbol, Maf.Lab.Domain.Graph.GraphTools.ChangeImpact] },
                new McpServerOptions { Domain = Domains.BulgarianHistory, Endpoint = bulgarianHistoryEndpoint },
            ];
        });
        services.AddDbContextFactory<MafDbContext>(o => o.UseSqlite($"Data Source={Path.Combine(workDir, "eval.db")}"));
        services.AddSingleton<SystemPrompt>();
        services.AddSingleton<TokenCounter>();
        services.AddSingleton<ToolAudit>();
        services.AddHttpClient("mcp");
        services.AddSingleton<IToolSource, McpToolSource>();
        services.AddSingleton<ConversationService>();
        services.AddJevIntentClassifier(configuration);
        // The write flow: an eval turn can propose an adjustment, so the turn runner needs it. No compliance
        // agent is configured here, so a proposal over the threshold ends as "unreachable" — which is an
        // honest outcome for a suite that measures which tool the model picks, not what a reviewer says.
        services.Configure<FeeAdjustmentOptions>(configuration.GetSection("FeeAdjustments"));
        services.Configure<Maf.Lab.Api.A2A.ComplianceOptions>(configuration.GetSection("Compliance"));
        services.AddHttpClient("a2a-consult");
        services.AddSingleton<Maf.Lab.Api.A2A.ComplianceConsultant>();
        services.AddTransient<FeeAdjustmentFlow>();
        services.AddTransient<ChatTurnRunner>();

        var provider = services.BuildServiceProvider();
        await using (var db = await provider.GetRequiredService<IDbContextFactory<MafDbContext>>().CreateDbContextAsync(ct))
        {
            await DatabaseInitializer.InitializeAsync(db, ct);
        }
        return new EvalAgentHost(provider, retrieval, portfolio, bulgarianHistory, code, workDir, options.KeepWorkDir);
    }

    /// <summary>The codebase MCP server in-process on a loopback port, with <paramref name="settings"/> over the configuration.</summary>
    public static async Task<(WebApplication App, string Endpoint)> StartCodeServerAsync(IConfiguration configuration,
        IReadOnlyDictionary<string, string?>? settings, CancellationToken ct)
    {
        var code = Maf.Lab.CodeSearch.Program.BuildApp([], b =>
        {
            b.Configuration.AddConfiguration(configuration);
            if (settings is not null)
            {
                b.Configuration.AddInMemoryCollection(settings);
            }
            b.Configuration["Urls"] = "http://127.0.0.1:0";
            b.Logging.SetMinimumLevel(LogLevel.Warning);
        });
        await code.StartAsync(ct);
        return (code, code.Urls.First().TrimEnd('/') + "/mcp");
    }

    /// <summary>A bearer token for the eval principal of <paramref name="firmId"/>: the servers derive the tenant from it.</summary>
    public static string EvalToken(IConfiguration configuration, string firmId)
    {
        var principal = EvalPrincipal(firmId);
        var auth = configuration.GetSection(AuthOptions.Section).Get<AuthOptions>() ?? new AuthOptions();
        return DevJwt.Issue(auth, principal.UserId, principal.FirmId, principal.Role, []).Token;
    }

    public static Principal EvalPrincipal(string firmId) => new($"eval-{firmId}", TenantId.Firm(firmId), Role.ADVISOR, []);

    public async Task<TurnResult> AskAsync(string firmId, string question, CancellationToken ct)
    {
        var principal = EvalPrincipal(firmId);
        var auth = Services.GetRequiredService<IOptions<AuthOptions>>().Value;
        var (token, _) = DevJwt.Issue(auth, principal.UserId, principal.FirmId, principal.Role, []);
        var conversationId = await Services.GetRequiredService<ConversationService>().CreateAsync(principal, ct);
        // The eval reads the turn's result, not its stream, so what the turn says goes to a channel nobody drains.
        var channel = Channel.CreateUnbounded<Microsoft.Extensions.AI.ChatResponseUpdate>();
        var runner = Services.GetRequiredService<ChatTurnRunner>();
        var result = await runner.RunAsync(principal, token, conversationId, question, $"r_{Guid.NewGuid():N}", channel.Writer, ct);
        channel.Writer.TryComplete();
        return result;
    }

    public async ValueTask DisposeAsync()
    {
        await Services.DisposeAsync();
        foreach (var server in new[] { _retrieval, _portfolio, _bulgarianHistory, _code }.OfType<WebApplication>())
        {
            await server.StopAsync();
            await server.DisposeAsync();
        }
        if (_keepWorkDir)
        {
            return;
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
