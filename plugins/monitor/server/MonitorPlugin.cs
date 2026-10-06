using Maf.Lab.Plugins.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Maf.Lab.Plugins.Monitor;

/// <summary>
/// The monitor (introduce-plugins 5.3; dev and qa only, 5d): what a developer sees behind the scenes of a turn. It
/// observes every turn — the full trace, the model capture, the prompt, retrieval diagnostics, the run's frames — keeps
/// it in its own table with its own retention, and serves it to the chat's "Behind the scenes" pane. Without it, a turn
/// keeps only its core record.
/// </summary>
public sealed class MonitorPlugin : IMafPlugin, IContributesTurnObserver, IContributesServices, IContributesEndpoints, IContributesModel
{
    public const string PluginName = "monitor";

    public string Name => PluginName;

    public ITurnObserver CreateObserver(IServiceProvider services) => services.GetRequiredService<MonitorObserver>();

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<TracingOptions>(configuration.GetSection(TracingOptions.Section));
        services.AddSingleton<MonitorObserver>();
        services.AddSingleton<TraceRetentionService>();
        services.AddHostedService(sp => sp.GetRequiredService<TraceRetentionService>());
    }

    public void MapEndpoints(IMafEndpoints endpoints) => TraceEndpoints.Map(endpoints.Routes);

    public void ConfigureModel(ModelBuilder model) => TurnDiagnosticsRow.Configure(model);
}
