using System.Diagnostics;
using System.Text.Json;
using Maf.Lab.Api.Agent;
using Maf.Lab.Plugins.Abstractions;
using Maf.Lab.Plugins.Portfolio;

namespace Maf.Lab.Tests;

/// <summary>
/// The portfolio plugin as its tests see it: its manifest as make reads it (`scripts/plugins.py manifest-json
/// portfolio`), its descriptor with the prompt fragment, and the view the lab had before the plugin left the core —
/// portfolio from this folder, and billing (the core tests' stand-in for it) beside it (extract-portfolio).
/// </summary>
public static class PortfolioPluginSupport
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly Lazy<PluginManifest> Loaded = new(Load);

    public static string Folder => Path.Combine(CorpusLoaderTests.RepoRoot(), "plugins", PortfolioPlugin.PluginName);

    /// <summary>The plugin's manifest, read the way make reads it.</summary>
    public static PluginManifest Manifest => Loaded.Value;

    /// <summary>The portfolio domain's descriptor, with its prompt fragment.</summary>
    public static DomainDescriptor Descriptor =>
        Manifest.Domain!.ToDescriptor(File.ReadAllText(Path.Combine(Folder, Manifest.Domain.Prompt!)));

    /// <summary>The core tests' stand-in for billing, and portfolio from this folder.</summary>
    public static DomainCatalogue WithPortfolio() => DomainCatalogue.Of(
        [StandInDomains.BillingDomain, Descriptor], [new Plugins.StandInBillingBehaviour(), new PortfolioBehaviour()]);

    /// <summary>Puts that view in scope for the static readers, until disposed.</summary>
    public static IDisposable Use() => DomainCatalogue.Use(WithPortfolio());

    private static PluginManifest Load()
    {
        var start = new ProcessStartInfo("python3")
        {
            WorkingDirectory = CorpusLoaderTests.RepoRoot(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in new[] { Path.Combine("scripts", "plugins.py"), "manifest-json", PortfolioPlugin.PluginName })
        {
            start.ArgumentList.Add(a);
        }
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return JsonDocument.Parse(output).RootElement.GetProperty("manifest").Deserialize<PluginManifest>(Json)!;
    }
}

/// <summary>The core tests' stand-in portfolio domain is this plugin's table: a change here fails naming the stale copy.</summary>
public class PortfolioPluginDriftTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public void The_core_fixture_is_this_plugins_domain()
    {
        // The prompt is set aside: it is a file of this folder, which the core cannot read once the folder is gone.
        var plugin = JsonSerializer.Serialize(PortfolioPluginSupport.Manifest.Domain! with { Prompt = null }, Web);
        var fixture = JsonSerializer.Serialize(StandInDomains.PortfolioManifest.Domain! with { Prompt = null }, Web);

        Assert.True(plugin == fixture,
            $"tests/Maf.Lab.Tests/Plugins/FixturePortfolioPlugin.cs is stale against plugins/portfolio/plugin.toml:\n{plugin}\n{fixture}");
    }
}
