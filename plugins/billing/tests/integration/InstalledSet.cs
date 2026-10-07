using Maf.Lab.Api.Plugins;
using Maf.Lab.Plugins.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Maf.Lab.IntegrationTests;

/// <summary>
/// A plugin catalogue over an installed set of the test's choosing (extract-compliance-plugin), for a tool source whose
/// tools depend on another plugin being in use. Each plugin is named only: what is asked of the catalogue is whether a
/// plugin is installed, and the named plugin's folder need not be present for that.
/// Each gets a kind, because the catalogue leaves out a manifest without one; `app` contributes nothing else to the turn.
/// </summary>
internal static class InstalledSet
{
    public static PluginCatalogue Of(params string[] names)
    {
        var root = Directory.CreateTempSubdirectory("maf-installed-").FullName;
        var document = new
        {
            schema = 1,
            env = "dev",
            plugins = names.Select(n => new { manifest = new PluginManifest { Name = n, Kind = PluginKinds.App }, serverJson = (object?)null, hasServer = false }),
        };
        File.WriteAllText(Path.Combine(root, ".installed"),
            System.Text.Json.JsonSerializer.Serialize(document, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)));
        return new PluginCatalogue(Options.Create(new PluginOptions { Root = root }), NullLogger<PluginCatalogue>.Instance);
    }
}
