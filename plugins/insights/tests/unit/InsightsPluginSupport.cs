using System.Diagnostics;
using System.Text.Json;
using Maf.Lab.Plugins.Abstractions;
using Maf.Lab.Plugins.Insights;
using Maf.Lab.TestSupport;

namespace Maf.Lab.Tests;

/// <summary>The statistics as their tests see them: the manifest as make reads it, and an api with it installed.</summary>
public static class InsightsPluginSupport
{
    private static readonly Lazy<PluginManifest> Loaded = new(Load);

    public static PluginManifest Manifest => Loaded.Value;

    /// <summary>The installed set of an api with the statistics: this plugin and the core tests' stand-in domains.</summary>
    public static IReadOnlyList<PluginManifest> Installed => [Manifest, .. StandInDomains.Installed];

    /// <summary>An api with the statistics installed.</summary>
    public static ApiFactory Api(ScriptedChatClient chat) => new(chat) { InstalledPlugins = Installed };

    private static PluginManifest Load()
    {
        var start = new ProcessStartInfo("python3")
        {
            WorkingDirectory = CorpusLoaderTests.RepoRoot(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in new[] { Path.Combine("scripts", "plugins.py"), "manifest-json", InsightsPlugin.PluginName })
        {
            start.ArgumentList.Add(a);
        }
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return JsonDocument.Parse(output).RootElement.GetProperty("manifest").Deserialize<PluginManifest>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }
}
