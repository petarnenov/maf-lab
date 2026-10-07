using System.Diagnostics;
using System.Text.Json;
using Maf.Lab.Plugins.Abstractions;
using Maf.Lab.Plugins.Observability;
using Maf.Lab.TestSupport;

namespace Maf.Lab.Tests;

/// <summary>The observability plugin as its tests see it: its manifest as make reads it, and an api with or without it.</summary>
public static class ObservabilityPluginSupport
{
    private static readonly Lazy<PluginManifest> Loaded = new(Load);

    public static PluginManifest Manifest => Loaded.Value;

    /// <summary>An api with the plugin installed (or not), its settings as compose/env/observability.env gives them.</summary>
    public static ApiFactory Api(bool installed = true) =>
        new(ApiFactory.ProceduralModel())
        {
            InstalledPlugins = installed ? [Manifest, .. StandInDomains.Installed] : StandInDomains.Installed,
            ExtraSettings = new Dictionary<string, string?>
            {
                ["Telemetry:TraceUrlTemplate"] = "http://localhost:7171/jaeger/trace/{traceId}",
                ["Telemetry:JaegerUrl"] = "http://localhost:7171/jaeger",
            },
        };

    private static PluginManifest Load()
    {
        var start = new ProcessStartInfo("python3")
        {
            WorkingDirectory = CorpusLoaderTests.RepoRoot(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in new[] { Path.Combine("scripts", "plugins.py"), "manifest-json", ObservabilityPlugin.PluginName })
        {
            start.ArgumentList.Add(a);
        }
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return JsonDocument.Parse(output).RootElement.GetProperty("manifest").Deserialize<PluginManifest>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }
}
