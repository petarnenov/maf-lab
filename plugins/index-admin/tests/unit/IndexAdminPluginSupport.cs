using System.Diagnostics;
using System.Text.Json;
using Maf.Lab.Plugins.Abstractions;
using Maf.Lab.Plugins.IndexAdmin;

namespace Maf.Lab.Tests;

/// <summary>
/// The index admin as its tests see it: the manifest as make reads it, stand-in plugins that declare corpora, and an api
/// with them installed whose corpora are folders under the host's plugins root.
/// </summary>
public static class IndexAdminPluginSupport
{
    private static readonly Lazy<PluginManifest> Loaded = new(Load);

    public static PluginManifest Manifest => Loaded.Value;

    /// <summary>A stand-in plugin that owns a corpus in <c>files/corpus</c> of its folder, indexed into its own collection.</summary>
    public static PluginManifest CorpusPlugin(string name, string layout = CorpusLayoutNames.Tenants, string? graph = null) => new()
    {
        Name = name,
        Kind = PluginKinds.App,
        Scope = PluginScopes.Installation,
        Environments = ["dev"],
        Description = $"A stand-in owning the {name} corpus.",
        Progress = "None — a stand-in",
        Stopping = "None — a stand-in",
        Corpus = new CorpusTable
        {
            Path = "files/corpus",
            Collection = $"maf_{name}_chunks",
            MetaCollection = $"maf_{name}_meta",
            Layout = layout,
            Graph = graph,
        },
    };

    /// <summary>An api with the index admin and these corpus owners installed, beside the core tests' stand-in domains.</summary>
    public static ApiFactory Api(IReadOnlyList<PluginManifest> corpora, Dictionary<string, string?>? settings = null) =>
        new(ApiFactory.ProceduralModel())
        {
            InstalledPlugins = [Manifest, .. corpora, .. StandInDomains.Installed],
            ExtraSettings = settings ?? new Dictionary<string, string?>(),
        };

    /// <summary>Writes one document of a tenant into a plugin's corpus folder under the host's plugins root.</summary>
    public static void WriteDocument(ApiFactory api, string plugin, string tenant = "firm-a")
    {
        var docs = Path.Combine(api.PluginsRoot, plugin, "files", "corpus", tenant, "docs");
        Directory.CreateDirectory(docs);
        File.WriteAllText(Path.Combine(docs, "fees.md"), "# Fees\n\nA fee schedule is assigned per account.\n");
    }

    private static PluginManifest Load()
    {
        var start = new ProcessStartInfo("python3")
        {
            WorkingDirectory = CorpusLoaderTests.RepoRoot(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in new[] { Path.Combine("scripts", "plugins.py"), "manifest-json", IndexAdminPlugin.PluginName })
        {
            start.ArgumentList.Add(a);
        }
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return JsonDocument.Parse(output).RootElement.GetProperty("manifest").Deserialize<PluginManifest>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }
}
