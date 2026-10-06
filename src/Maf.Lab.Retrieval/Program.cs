using Maf.Lab.Retrieval.Auth;
using Maf.Lab.Retrieval.Graph;
using Maf.Lab.Retrieval.Store;
using Maf.Lab.Retrieval.Tools;

using Maf.Lab.Hosting;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Maf.Lab.Retrieval;

// names a domain until the extract-evals-plugin follow-up moves it (introduce-plugins 8.1)
// The billing host, which stays here until the eval stops hosting it in-process.
/// <summary>MCP server (protocol 2026-07-28, Streamable HTTP, stateless) exposing search_documents and the billing stub tools.</summary>
public partial class Program
{
    public static void Main(string[] args) => BuildApp(args).Run();

    public static WebApplication BuildApp(string[] args, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Configuration.AddJsonFile("retrieval.json", optional: true).AddEnvironmentVariables();
        configure?.Invoke(builder);

        // What this service emits about itself. Nothing is exported unless an OTLP endpoint is configured.
        builder.AddLabTelemetry("maf-lab-mcp-retrieval");

        // The one place every replica reads. A replica that cannot see it answers some requests correctly and
        // loses others, so it does not start at all.
        builder.AddSharedState();
        builder.RequireSharedState<Maf.Lab.Domain.SharedState.IIdempotencyStore>();

        builder.Services.AddMafRetrievalCore(builder.Configuration);
        builder.Services.AddDevJwtAuthentication(builder.Configuration);
        // The graph store: billing relationships. The driver connects on first use, so the server starts without it and
        // the graph tool answers "temporarily unavailable" until it is back.
        builder.Services.AddGraphStore(builder.Configuration);
        // The writable store belongs to this server alone, so it is registered here and not in the shared core.
        builder.Services.TryAddSingleton<Billing.BillingSeedStore>();
        builder.Services.TryAddSingleton<Billing.BillingAccountStore>();
        builder.Services.AddSingleton<Billing.FeeAdjustmentLedger>();
        builder.Services.AddSingleton<Billing.AccountFees>();
        builder.Services.AddSingleton<Billing.ProposalSigner>();
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.AddHostedService<BootstrapService>();
        builder.Services
            .AddMcpServer(o => o.ServerInfo = new() { Name = "maf-lab-retrieval", Version = "1.0.0" })
            .WithHttpTransport(o => o.Stateless = true)
            .WithTools<SearchDocumentsTool>()
            .WithTools<BillingTools>()
            .WithTools<FeeAdjustmentTools>()
            .WithTools<BillingGraphTools>();

        var app = builder.Build();
        // Resolved now so a missing JEV_MAF_LAB is reported once at startup, not on the first gated search.
        app.Services.GetRequiredService<Jev.JevCredential>();
        app.UseInstanceHeader();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapInstanceHealth();
        app.MapMcp("/mcp").RequireAuthorization();
        return app;
    }
}

/// <summary>Creates the collections when Qdrant becomes reachable; never blocks startup.</summary>
internal sealed class BootstrapService(CollectionBootstrapper bootstrapper, ILogger<BootstrapService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        for (var attempt = 1; !stoppingToken.IsCancellationRequested; attempt++)
        {
            try
            {
                await bootstrapper.EnsureAsync(stoppingToken);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning("Qdrant bootstrap attempt {Attempt} failed: {ErrorType}", attempt, ex.GetType().Name);
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(30, attempt * 2)), stoppingToken);
            }
        }
    }
}
