using System.Diagnostics;
using System.Text.Json;
using Maf.Lab.Api.Agent;
using Maf.Lab.Plugins.Abstractions;
using Maf.Lab.Plugins.Code;

namespace Maf.Lab.Tests;

/// <summary>
/// The code plugin as its tests see it: its manifest as make reads it (`scripts/plugins.py manifest-json code`), its
/// descriptor with the prompt fragment, and the three-domain view the lab had before the plugin left the core —
/// billing and portfolio (the core tests' stand-ins for them), codebase from this folder (introduce-plugins 5.2).
/// </summary>
public static class CodePluginSupport
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly Lazy<PluginManifest> Loaded = new(Load);

    private static string Folder => Path.Combine(CorpusLoaderTests.RepoRoot(), "plugins", CodePlugin.PluginName);

    /// <summary>The plugin's manifest, read the way make reads it.</summary>
    public static PluginManifest Manifest => Loaded.Value;

    /// <summary>The codebase domain's descriptor, with its prompt fragment.</summary>
    public static DomainDescriptor Descriptor =>
        Manifest.Domain!.ToDescriptor(File.ReadAllText(Path.Combine(Folder, Manifest.Domain.Prompt!)));

    /// <summary>Billing (the stand-in) and portfolio, then codebase: every domain this repository ships.</summary>
    public static DomainCatalogue ThreeDomains() =>
        DomainCatalogue.Of([.. StandInDomains.WithBilling.All, Descriptor], [.. StandInDomains.WithBilling.Behaviours, new CodebaseBehaviour()]);

    /// <summary>Puts the three-domain view in scope for the static readers, until disposed.</summary>
    public static IDisposable Use() => DomainCatalogue.Use(ThreeDomains());

    /// <summary>An api with the code plugin installed: its manifest in the installed set and its prompt beside it.</summary>
    public static ApiFactory WithCode(this ApiFactory api)
    {
        var folder = Path.Combine(api.PluginsRoot, CodePlugin.PluginName);
        Directory.CreateDirectory(folder);
        File.Copy(Path.Combine(Folder, Manifest.Domain!.Prompt!), Path.Combine(folder, Manifest.Domain.Prompt!), overwrite: true);
        return api;
    }

    private static PluginManifest Load()
    {
        var start = new ProcessStartInfo("python3")
        {
            WorkingDirectory = CorpusLoaderTests.RepoRoot(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in new[] { Path.Combine("scripts", "plugins.py"), "manifest-json", CodePlugin.PluginName })
        {
            start.ArgumentList.Add(a);
        }
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return JsonDocument.Parse(output).RootElement.GetProperty("manifest").Deserialize<PluginManifest>(Json)!;
    }
}
