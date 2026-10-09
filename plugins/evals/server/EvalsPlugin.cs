namespace Maf.Lab.Plugins.Evals;

public sealed class EvalsPlugin : IMafPlugin, IContributesServices, IContributesEndpoints
{
    public const string PluginName = "evals";
    public string Name => PluginName;
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<DatasetWriter>();
        services.AddSingleton<IAppendEvalDataset>(sp => sp.GetRequiredService<DatasetWriter>());
    }
    public void MapEndpoints(IMafEndpoints endpoints) => endpoints.Routes.MapEvalReports();
}
