using System.Net.Http.Json;
using System.Threading.Channels;
using Maf.Lab.Api.Agent;
using Maf.Lab.Api.Agent.Decisions;
using Maf.Lab.Api.Storage;
using Maf.Lab.Domain.Chat;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Indexing;
using Maf.Lab.Plugins.Abstractions;
using Maf.Lab.Retrieval.Auth;
using Maf.Lab.Domain.Configuration;
using Maf.Lab.Retrieval.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Maf.Lab.Eval.Hosting;

/// <summary>
/// Runs chat turns through the production agent path: ChatTurnRunner → MCP client → retrieval MCP server (hosted
/// in-process on loopback unless Evals:McpEndpoint points at a running one) → tenant-scoped search.
/// </summary>
public sealed class EvalAgentHost : IAsyncDisposable
{
    private readonly WebApplication? _retrieval;
    private readonly WebApplication? _portfolio;
    private readonly WebApplication? _code;
    private readonly string _workDir;

    private readonly bool _keepWorkDir;

    private EvalAgentHost(ServiceProvider services, WebApplication? retrieval, WebApplication? portfolio, WebApplication? code, string workDir,
        bool keepWorkDir = false)
    {
        Services = services;
        _retrieval = retrieval;
        _portfolio = portfolio;
        _code = code;
        _workDir = workDir;
        _keepWorkDir = keepWorkDir;
    }

    public ServiceProvider Services { get; }

    /// <summary>
    /// Puts this host's domains in scope for a suite that asks the classifier directly (outside a turn, which scopes its
    /// own): the same frozen view an api request reads.
    /// </summary>
    public async Task<DomainCatalogue> DomainsForAsync(string organization, CancellationToken ct)
    {
        var principal = EvalPrincipal(organization);
        var scope = await Services.GetRequiredService<IPluginAccess>().For(principal, ct);
        return Services.GetRequiredService<DomainCatalogue>().For(scope);
    }

    /// <summary>
    /// Refuses a suite that measures a domain the stack has not installed: it fails loudly, naming the plugin, instead of
    /// scoring every case zero.
    /// </summary>
    public void RequireDomain(string domain, string plugin)
    {
        if (Services.GetRequiredService<DomainCatalogue>().Get(domain) is null)
        {
            throw new InvalidOperationException(
                $"the {plugin} plugin is not installed (no {domain} domain in plugins/.installed): install it (make plugin-on NAME={plugin}) or run make up first");
        }
    }

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

        // The code domain is a remote plugin. Configuration overrides its operator-reviewed installed endpoint.
        WebApplication? code = null;
        var codeEndpoint = options.CodeMcpEndpoint;
        if (string.IsNullOrWhiteSpace(codeEndpoint))
        {
            using var catalogue = new Maf.Lab.Api.Plugins.PluginCatalogue(
                Options.Create(new Maf.Lab.Api.Plugins.PluginOptions { Root = configuration["Plugins:Root"] ?? "plugins" }),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<Maf.Lab.Api.Plugins.PluginCatalogue>.Instance);
            codeEndpoint = catalogue.McpServers().GetValueOrDefault("code")?.Endpoint ?? "";
        }
        codeEndpoint = GatewayEndpoint(configuration, codeEndpoint);

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
            o.Servers = new(StringComparer.Ordinal)
            {
                ["billing"] = new McpServerOptions { Domain = "billing", Endpoint = endpoint },
                ["portfolio"] = new McpServerOptions { Domain = "portfolio", Endpoint = portfolioEndpoint },
                // Keyed by the code plugin's name, so it shadows the server its server.json names (reachable only inside
                // the stack's network): the eval reaches the code server it started, or the stack's through the balancer.
                ["code"] = new McpServerOptions { Domain = "codebase", Endpoint = codeEndpoint, Tools = [Maf.Lab.Domain.Code.CodeTools.Search, Maf.Lab.Domain.Graph.GraphTools.TraceCodeSymbol, Maf.Lab.Domain.Graph.GraphTools.ChangeImpact] },
            };
        });
        // Unpooled, as the api's store is (DECISIONS §84).
        services.AddDbContextFactory<MafDbContext>(o => o.UseSqlite(
            new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = Path.Combine(workDir, "eval.db"), Pooling = false }.ToString()));
        // The domains the eval turns read, as the api builds them: every plugin the stack has installed (plugins/.installed,
        // read once), with the behaviours of those whose code is here.
        // Each turn takes a frozen view of it, as an api turn does.
        services.Configure<Maf.Lab.Api.Plugins.PluginOptions>(configuration.GetSection(Maf.Lab.Api.Plugins.PluginOptions.Section));
        services.AddSingleton<Maf.Lab.Api.Plugins.PluginCatalogue>();
        services.AddSingleton(sp => new DomainCatalogue(
            Maf.Lab.Api.Plugins.PluginHost.InstalledBehaviours(sp.GetRequiredService<Maf.Lab.Api.Plugins.PluginCatalogue>().Current),
            sp.GetRequiredService<Maf.Lab.Api.Plugins.PluginCatalogue>(), sp.GetRequiredService<IOptions<Maf.Lab.Api.Plugins.PluginOptions>>()));
        // Every eval turn traced in full, as before the monitor became a plugin: the eval's own observer wants it all.
        services.AddSingleton<EvalTraceCapture>();
        services.AddSingleton<Maf.Lab.Plugins.Abstractions.ITurnObserver>(sp => sp.GetRequiredService<EvalTraceCapture>());
        services.AddSingleton<Maf.Lab.Api.Agent.Tracing.TurnObservers>();
        services.AddSingleton<SystemPrompt>();
        services.AddSingleton<TokenCounter>();
        services.AddSingleton<ToolAudit>();
        services.AddHttpClient("mcp");
        services.AddHttpClient("dev-audience-tokens");
        services.AddSingleton<DevAudienceTokens>();
        services.AddSingleton<IPluginTokens>(sp => sp.GetRequiredService<DevAudienceTokens>());
        services.AddSingleton<IPluginAccess>(sp => sp.GetRequiredService<DevAudienceTokens>());
        // The decision engine the installed provider registers, metered for the suites that ask it through the
        // production classes (its input tokens per suite).
        services.AddInstalledProviders(configuration);
        services.AddSingleton<DecisionUsageMeter>();
        MeteredDecisionEngine.DecorateEngine(services);
        services.AddSingleton<IToolSource>(sp =>
        {
            var source = ActivatorUtilities.CreateInstance<McpToolSource>(sp);
            return codeSettings?.GetValueOrDefault("CodeSearch:GraphDepthPin") is { } pin
                ? new PinnedGraphTools(source, int.Parse(pin, System.Globalization.CultureInfo.InvariantCulture)) : source;
        });
        services.AddSingleton<ConversationService>();
        services.AddDecisionCallers(configuration);
        // The installed plugins' own services, as the api composes them: with compliance installed, its reviewer (make eval
        // passes the stack's Compliance:*), so a case over the review threshold consults the stack's reviewer, and a stop
        // reaches it too (the eval exits 130; the consultant sends A2A tasks/cancel within 5 s). Without it, no reviewer.
        Maf.Lab.Api.Plugins.PluginHost.InstalledServices(Maf.Lab.Api.Plugins.PluginHost.ReadInstalled(configuration), services, configuration);
        services.TryAddSingleton<Maf.Lab.Plugins.Abstractions.IReviewerConsultation, Maf.Lab.Api.Agent.Writes.NoReviewer>();
        // The installed plugins' write flows over the core's ports, as the api composes them (generalize-write-confirmation).
        services.AddScoped<Maf.Lab.Api.Agent.Writes.WriteTurnContext>();
        services.AddScoped<Maf.Lab.Plugins.Abstractions.IWriteAudit, Maf.Lab.Api.Agent.Writes.CoreWriteAudit>();
        services.AddScoped<Maf.Lab.Plugins.Abstractions.IConsultationScreening, Maf.Lab.Api.Agent.Writes.CoreConsultationScreening>();
        services.AddScoped<Maf.Lab.Plugins.Abstractions.IWriteTraceStep, Maf.Lab.Api.Agent.Writes.CoreWriteTraceStep>();
        services.AddScoped(sp => new Maf.Lab.Api.Agent.Writes.WriteFlows(Maf.Lab.Api.Plugins.PluginHost.InstalledWriteFlows(
            sp.GetRequiredService<Maf.Lab.Api.Plugins.PluginCatalogue>().Current, sp)));
        services.AddScoped<Maf.Lab.Api.Agent.Writes.WriteConfirmations>();
        services.AddTransient<ChatTurnRunner>();

        services.AddSingleton<IPluginTokens>(sp => sp.GetRequiredService<DevAudienceTokens>());
        var provider = services.BuildServiceProvider();
        _ = provider.GetRequiredService<IOptions<InstalledProviders>>().Value;
        await using (var db = await provider.GetRequiredService<IDbContextFactory<MafDbContext>>().CreateDbContextAsync(ct))
        {
            await DatabaseInitializer.InitializeAsync(db, ct);
        }
        return new EvalAgentHost(provider, retrieval, portfolio, code, workDir, options.KeepWorkDir);
    }

    public static string CodeEndpoint(IConfiguration configuration)
    {
        if (configuration["Evals:CodeMcpEndpoint"] is { Length: > 0 } configured) return configured;
        using var catalogue = new Maf.Lab.Api.Plugins.PluginCatalogue(
            Options.Create(new Maf.Lab.Api.Plugins.PluginOptions { Root = configuration["Plugins:Root"] ?? "plugins" }),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Maf.Lab.Api.Plugins.PluginCatalogue>.Instance);
        var endpoint = configuration["Agent:Servers:code:Endpoint"] ?? catalogue.McpServers().GetValueOrDefault("code")?.Endpoint
            ?? throw new InvalidOperationException("The installed code plugin has no MCP endpoint.");
        return GatewayEndpoint(configuration, endpoint);
    }

    private static string GatewayEndpoint(IConfiguration configuration, string endpoint)
    {
        if (Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) && uri.Host == "lb"
            && configuration["Evals:GatewayBaseUrl"] is { Length: > 0 } gateway)
            return gateway.TrimEnd('/') + uri.PathAndQuery;
        return endpoint;
    }

    /// <summary>A bearer token for the eval principal of <paramref name="tenantId"/>: the servers derive the tenant from it.</summary>
    public static async Task<string> EvalTokenAsync(IConfiguration configuration, string organization, string audience, CancellationToken ct)
    {
        using var client = new HttpClient();
        using var response = await client.PostAsJsonAsync(configuration["Evals:DevTokenEndpoint"]
            ?? (configuration["Evals:GatewayBaseUrl"] ?? "http://localhost:7171").TrimEnd('/') + "/dev/token",
            new { userId = EvalPrincipal(organization).UserId, tenantId = organization, role = "USER", audience }, ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(ct)).GetProperty("token").GetString()!;
    }

    public static Principal EvalPrincipal(string tenantId) => new($"eval-{tenantId}", TenantId.Firm(tenantId), Role.USER);

    /// <summary>What a write tool's question must state, as its installed flow says (none when no flow says).</summary>
    public Maf.Lab.Plugins.Abstractions.IStatesConfirmationFacts? FactsFor(string toolName) =>
        Services.GetRequiredService<Maf.Lab.Api.Agent.Writes.WriteFlows>().For(toolName) as Maf.Lab.Plugins.Abstractions.IStatesConfirmationFacts;

    public async Task<TurnResult> AskAsync(string tenantId, string question, CancellationToken ct)
    {
        var principal = EvalPrincipal(tenantId);
        var token = await Services.GetRequiredService<DevAudienceTokens>().MintAsync(principal, "api", ct);
        var conversationId = await Services.GetRequiredService<ConversationService>().CreateAsync(principal, ct);
        // The eval reads the turn's result, not its stream, so what the turn says goes to a channel nobody drains.
        var channel = Channel.CreateUnbounded<Microsoft.Extensions.AI.ChatResponseUpdate>();
        var runner = Services.GetRequiredService<ChatTurnRunner>();
        var runId = $"r_{Guid.NewGuid():N}";
        var observation = Services.GetRequiredService<Maf.Lab.Api.Agent.Tracing.TurnObservers>().Begin(runId);
        try
        {
            return await runner.RunAsync(principal, token, conversationId, question, runId, channel.Writer, ct, observation: observation);
        }
        finally
        {
            channel.Writer.TryComplete();
            await observation.CompleteAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await Services.DisposeAsync();
        foreach (var server in new[] { _retrieval, _portfolio, _code }.OfType<WebApplication>())
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
