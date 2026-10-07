using Maf.Lab.Plugins.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Maf.Lab.Tests;

/// <summary>The installed plugins' manifests, as a plugin reads them through the port (extract-topology-plugin D2).</summary>
public sealed class InstalledPluginsTests
{
    [Fact]
    public void The_port_lists_the_installed_manifests_as_the_installed_set_carries_them()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel());
        _ = api.CreateClient();

        var installed = api.Services.GetRequiredService<IInstalledPlugins>().Installed();

        Assert.Equal(StandInDomains.Installed.Select(m => m.Name), installed.Select(m => m.Name));
        Assert.Equal(StandInDomains.BillingManifest.Domain!.Id, installed.Single(m => m.Name == StandInDomains.BillingManifest.Name).Domain!.Id);
    }

    [Fact]
    public void The_core_alone_has_none()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel()) { InstalledPlugins = [] };
        _ = api.CreateClient();

        Assert.Empty(api.Services.GetRequiredService<IInstalledPlugins>().Installed());
    }
}
