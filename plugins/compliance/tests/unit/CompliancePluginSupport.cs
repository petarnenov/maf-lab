using System.Diagnostics;
using System.Text.Json;
using Maf.Lab.Plugins.Abstractions;
using Maf.Lab.Plugins.Compliance;

namespace Maf.Lab.Tests;

/// <summary>
/// The compliance plugin as its tests see it: its manifest as make reads it (`scripts/plugins.py manifest-json
/// compliance`), and the installed set an api under test runs with — the stand-in domains plus this plugin, whose server
/// part joins by its name.
/// </summary>
public static class CompliancePluginSupport
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly Lazy<PluginManifest> Loaded = new(Load);

    public static PluginManifest Manifest => Loaded.Value;

    /// <summary>What an api under test installs to have the reviewer's client and the audit screen.</summary>
    public static IReadOnlyList<PluginManifest> Installed => [.. StandInDomains.Installed, Manifest];

    private static PluginManifest Load()
    {
        var start = new ProcessStartInfo("python3")
        {
            WorkingDirectory = CorpusLoaderTests.RepoRoot(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in new[] { Path.Combine("scripts", "plugins.py"), "manifest-json", CompliancePlugin.PluginName })
        {
            start.ArgumentList.Add(a);
        }
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return JsonDocument.Parse(output).RootElement.GetProperty("manifest").Deserialize<PluginManifest>(Json)!;
    }
}
