using Maf.Lab.Retrieval.Auth;
using Maf.Lab.Retrieval.Store;
using Maf.Lab.Retrieval.Tools;

using Maf.Lab.Hosting;

namespace Maf.Lab.Retrieval;

/// <summary>MCP server (protocol 2026-07-28, Streamable HTTP, stateless) exposing search_documents and the billing stub tools.</summary>
public partial class Program
{
    public static void Main(string[] args) => BuildApp(args).Run();

    public static WebApplication BuildApp(string[] args, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Configuration.AddJsonFile("retrieval.json", optional: true).AddEnvironmentVariables();
        configure?.Invoke(builder);

        builder.Services.AddMafRetrievalCore(builder.Configuration);
        builder.Services.AddDevJwtAuthentication(builder.Configuration);
        builder.Services.AddHostedService<BootstrapService>();
        builder.Services
            .AddMcpServer(o => o.ServerInfo = new() { Name = "maf-lab-retrieval", Version = "1.0.0" })
            .WithHttpTransport(o => o.Stateless = true)
            .WithTools<SearchDocumentsTool>()
            .WithTools<BillingTools>();

        var app = builder.Build();
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
