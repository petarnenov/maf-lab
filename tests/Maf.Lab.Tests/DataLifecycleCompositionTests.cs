using System.Runtime.CompilerServices;
using Maf.Lab.Api.DataLifecycle;
using Maf.Lab.Api.Plugins;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Plugins.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Maf.Lab.Tests;

public sealed class DataLifecycleCompositionTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Core_lifecycle_is_available_without_optional_plugins()
    {
        using var factory = new ApiFactory(ApiFactory.ProceduralModel()) { InstalledPlugins = [] };
        var core = Assert.Single(factory.Services.GetServices<NamedDataLifecycle>());
        Assert.Null(core.Plugin);
        Assert.Same(factory.Services.GetRequiredService<CoreDataLifecycle>(), core.Lifecycle);
        Assert.Null(factory.Services.GetService<FixtureLifecycleStore>());
    }

    [Fact]
    public async Task Installed_but_disabled_plugin_keeps_its_lifecycle_and_receives_the_explicit_job_scope()
    {
        using var factory = new ApiFactory(ApiFactory.ProceduralModel())
        {
            InstalledPlugins = [FixtureLifecyclePlugin.Manifest], BootstrapTenantPlugins = false,
        };
        var principal = new Principal("person", TenantId.Firm("firm-a"), Role.USER);
        var enabled = await factory.Services.GetRequiredService<IPluginAccess>().For(principal, Ct);
        Assert.False(enabled.IsInUse(FixtureLifecyclePlugin.Manifest.Name));
        var entries = factory.Services.GetServices<NamedDataLifecycle>().ToArray();
        Assert.Equal(2, entries.Length);
        Assert.Single(entries, entry => entry.Plugin is null);
        var plugin = Assert.Single(entries, entry => entry.Plugin == FixtureLifecyclePlugin.Manifest.Name);
        var scope = new DataLifecycleScope(principal.TenantId, principal.UserId);
        await plugin.Lifecycle.DeleteAsync(scope, Ct);
        var store = factory.Services.GetRequiredService<FixtureLifecycleStore>();
        Assert.Same(store, plugin.Lifecycle);
        Assert.Equal(scope, Assert.Single(store.Deleted));
        Assert.Equal(Ct, store.LastToken);
    }
}

public sealed class FixtureLifecyclePlugin : IMafPlugin, IContributesServices, IContributesDataLifecycle
{
    public static PluginManifest Manifest => new()
    {
        Name = "lifecycle-fixture", Kind = PluginKinds.App, Scope = PluginScopes.Tenant,
        Environments = ["dev", "qa"], Description = "Lifecycle fixture", Progress = "None", Stopping = "None",
    };
    public string Name => Manifest.Name;
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration) => services.AddSingleton<FixtureLifecycleStore>();
    public IDataLifecycle CreateDataLifecycle(IServiceProvider services) => services.GetRequiredService<FixtureLifecycleStore>();
}

public sealed class FixtureLifecycleStore : IDataLifecycle
{
    public List<DataLifecycleScope> Deleted { get; } = [];
    public CancellationToken LastToken { get; private set; }
    public Task DeleteAsync(DataLifecycleScope scope, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        LastToken = ct;
        Deleted.Add(scope);
        return Task.CompletedTask;
    }
    public async IAsyncEnumerable<DataExportRecord> ExportAsync(DataLifecycleScope scope, [EnumeratorCancellation] CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await Task.CompletedTask;
        yield break;
    }
    public Task ApplyRetentionAsync(DataRetentionPolicy policy, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
