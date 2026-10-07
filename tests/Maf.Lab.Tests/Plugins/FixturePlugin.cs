using Maf.Lab.Plugins.Abstractions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Maf.Lab.Tests.Plugins;

/// <summary>
/// A plugin compiled into the test assembly (introduce-plugins tasks 3.4 and 3.7): it contributes a service, one route
/// and open work, so the composition root, the gate and the open-work routes are proved without a real plugin folder.
/// </summary>
public sealed class FixturePlugin : IMafPlugin, IContributesServices, IContributesEndpoints, IContributesOpenWork, IOpenWork
{
    public const string PluginName = "fixture";

    public string Name => PluginName;

    /// <summary>The fixture's open work, shared by every host in the test run: cleared by a cancel, as a store would.</summary>
    public static readonly List<OpenWorkItem> Open = [];

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration) =>
        services.AddSingleton<FixtureMarker>();

    public void MapEndpoints(IMafEndpoints endpoints) =>
        endpoints.Routes.MapGet("/api/fixture/ping", (FixtureMarker marker) => Results.Ok(new { pong = marker.Value }));

    public IOpenWork CreateOpenWork(IServiceProvider services) => this;

    public Task<IReadOnlyList<OpenWorkItem>> ListOpenAsync(CancellationToken ct)
    {
        lock (Open)
        {
            return Task.FromResult<IReadOnlyList<OpenWorkItem>>([.. Open]);
        }
    }

    public Task CancelAllAsync(CancellationToken ct)
    {
        lock (Open)
        {
            Open.Clear();
        }
        return Task.CompletedTask;
    }

    public static PluginManifest Manifest(params string[] environments) => new()
    {
        Name = PluginName,
        Kind = PluginKinds.App,
        Scope = PluginScopes.Installation,
        Environments = environments.Length > 0 ? environments : ["dev", "qa"],
        Description = "A test fixture",
        Progress = "None — a fixture",
        Stopping = "None — a fixture",
    };
}

/// <summary>A service only the fixture registers: present exactly when the fixture is installed.</summary>
public sealed class FixtureMarker
{
    public string Value => "pong";
}
