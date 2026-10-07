using Maf.Lab.Plugins.Abstractions;

namespace Maf.Lab.Plugins.Insights;

/// <summary>
/// The Jev and intent statistics (extract-insights-plugin; dev and qa only): a developer's view of how every Jev call
/// site behaved on the firm's turns. It reads the turns' core records through <see cref="ITurnRecords"/> and the guard's
/// effective thresholds through <see cref="IGuardSettings"/>; it keeps nothing of its own.
/// </summary>
public sealed class InsightsPlugin : IMafPlugin, IContributesEndpoints
{
    public const string PluginName = "insights";

    public string Name => PluginName;

    public void MapEndpoints(IMafEndpoints endpoints) => StatsEndpoints.Map(endpoints.Routes);
}
