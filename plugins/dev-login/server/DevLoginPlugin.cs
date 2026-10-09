namespace Maf.Lab.Plugins.DevLogin;

public sealed class DevLoginPlugin : IMafPlugin, IContributesServices, IContributesEndpoints
{
    public const string PluginName = "dev-login";
    public string Name => PluginName;
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        if (!string.IsNullOrWhiteSpace(configuration["Auth:Authority"]))
            throw new InvalidOperationException("dev-login cannot be installed with company authentication.");
        services.AddSingleton<IPluginTokens, DevPluginTokens>();
    }
    public void MapEndpoints(IMafEndpoints endpoints) => endpoints.Routes.MapDevIssuer();
}
