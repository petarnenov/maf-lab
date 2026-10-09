using System.Diagnostics;
using System.Text.Json;
using Maf.Lab.Plugins.A2A;
using Maf.Lab.Plugins.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Maf.Lab.Tests;

/// <summary>The a2a plugin as its tests see it: its manifest as make reads it, installed beside the stand-in domains.</summary>
public static class A2APluginSupport
{
    private static readonly Lazy<PluginManifest> Loaded = new(Load);

    public static PluginManifest Manifest => Loaded.Value;

    /// <summary>The a2a plugin and the stand-in domains the shared fakes speak.</summary>
    public static IReadOnlyList<PluginManifest> Installed => [Manifest, .. StandInDomains.Installed];

    public static void BootstrapPartners(ApiFactory api)
    {
        if (!api.BootstrapTenantPlugins) return;
        foreach (var tenant in api.Services.GetRequiredService<IOptions<Maf.Lab.A2A.A2AOptions>>().Value.Partners.Values
            .SelectMany(partner => partner.Firms).Distinct(StringComparer.Ordinal))
            api.ClientFor("fixture", tenant, Maf.Lab.Domain.Tenancy.Role.USER).Dispose();
    }

    private static PluginManifest Load()
    {
        var start = new ProcessStartInfo("python3")
        {
            WorkingDirectory = CorpusLoaderTests.RepoRoot(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in new[] { Path.Combine("scripts", "plugins.py"), "manifest-json", A2APlugin.PluginName })
        {
            start.ArgumentList.Add(a);
        }
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return JsonDocument.Parse(output).RootElement.GetProperty("manifest").Deserialize<PluginManifest>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }
}
