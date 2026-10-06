using System.Diagnostics;
using System.Text.Json;
using Maf.Lab.Api.Plugins;
using Maf.Lab.Plugins.Abstractions;
using Maf.Lab.Tests.Plugins;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Maf.Lab.Tests;

/// <summary>
/// The plugin contract suite (introduce-plugins task 3.7), run against every folder under plugins/: its manifest is
/// valid with progress and stopping; the api boots with it and without it; its routes exist only while it is installed;
/// and its folder is deletable — nothing outside it names its path or its assembly, so its tests go with it. The suite
/// proves itself on fixtures: a folder that passes, one that must be refused, and the in-assembly fixture plugin.
/// </summary>
[Collection(PluginFixtureCollection.Name)]
public sealed class PluginContractTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static string Repo => CorpusLoaderTests.RepoRoot();

    public static IEnumerable<string> PluginFolders() =>
        Directory.Exists(Path.Combine(Repo, "plugins"))
            ? Directory.EnumerateDirectories(Path.Combine(Repo, "plugins"))
                .Where(d => File.Exists(Path.Combine(d, "plugin.toml"))).Select(Path.GetFileName).Order(StringComparer.Ordinal)!
            : [];

    [Fact]
    public void Every_plugin_folder_keeps_the_contract()
    {
        var (code, output) = Plugins("validate");
        Assert.True(code == 0, "invalid manifests:\n" + output);
        foreach (var name in PluginFolders())
        {
            var manifest = Manifest(name);
            Assert.False(string.IsNullOrWhiteSpace(manifest.Progress), $"{name}: no progress");
            Assert.False(string.IsNullOrWhiteSpace(manifest.Stopping), $"{name}: no stopping");
            AssertBootsWithAndWithout(manifest);
            Assert.Empty(MentionsOutsideItsFolder(name));
        }
    }

    [Fact]
    public void The_suite_refuses_a_broken_manifest_naming_it()
    {
        var (code, output) = Plugins("validate", Path.Combine(Repo, "tests", "fixtures", "plugins"));

        Assert.NotEqual(0, code);
        Assert.Contains("plugins/broken/plugin.toml", output);
        Assert.Contains("missing required `stopping`", output);
        Assert.DoesNotContain("plugins/good/", output);
    }

    [Fact]
    public void The_suite_sees_a_plugins_routes_only_while_it_is_installed() =>
        AssertBootsWithAndWithout(FixturePlugin.Manifest());

    private static void AssertBootsWithAndWithout(PluginManifest manifest)
    {
        using (var without = new ApiFactory(ApiFactory.ProceduralModel()))
        {
            _ = without.CreateClient();
            Assert.Empty(RoutesOf(without, manifest.Name));
        }
        using var with = new ApiFactory(ApiFactory.ProceduralModel()) { InstalledPlugins = [manifest] };
        _ = with.CreateClient();
        var hasCode = PluginHost.Discover([typeof(ApiFactory).Assembly]).ContainsKey(manifest.Name);
        if (hasCode && with.Services.GetRequiredService<LoadedPlugins>().Plugins.Single(p => p.Name == manifest.Name) is IContributesEndpoints)
        {
            Assert.NotEmpty(RoutesOf(with, manifest.Name));
        }
    }

    private static List<string> RoutesOf(ApiFactory factory, string plugin) =>
        [.. factory.Services.GetServices<EndpointDataSource>().SelectMany(s => s.Endpoints)
            .Where(e => e.Metadata.GetMetadata<PluginRouteMetadata>()?.Plugin == plugin)
            .Select(e => e.DisplayName ?? "")];

    /// <summary>Tracked files outside the plugin's folder that name its path or its server assembly.</summary>
    private static List<string> MentionsOutsideItsFolder(string name)
    {
        var needles = new List<string> { $"plugins/{name}/" };
        needles.AddRange(Directory.EnumerateFiles(Path.Combine(Repo, "plugins", name), "*.csproj", SearchOption.AllDirectories)
            .Select(Path.GetFileNameWithoutExtension)!);
        var tracked = Run("git", ["ls-files"], Repo).Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        return [.. tracked
            .Where(f => !f.StartsWith($"plugins/{name}/", StringComparison.Ordinal) && !f.StartsWith("openspec/", StringComparison.Ordinal))
            .Where(f => File.Exists(Path.Combine(Repo, f)) && new FileInfo(Path.Combine(Repo, f)).Length < 2_000_000)
            .Where(f => { var text = File.ReadAllText(Path.Combine(Repo, f)); return needles.Any(n => text.Contains(n, StringComparison.Ordinal)); })];
    }

    private static PluginManifest Manifest(string name)
    {
        var (code, output) = Plugins("manifest-json", null, name);
        Assert.True(code == 0, output);
        return JsonDocument.Parse(output).RootElement.GetProperty("manifest").Deserialize<PluginManifest>(Json)!;
    }

    private static (int Code, string Output) Plugins(string command, string? root = null, string? name = null)
    {
        var args = new List<string> { Path.Combine(Repo, "scripts", "plugins.py"), command };
        if (name is not null)
        {
            args.Add(name);
        }
        return Run("python3", args, Repo, root);
    }

    private static (int Code, string Output) Run(string file, IEnumerable<string> args, string cwd, string? pluginsRoot = null)
    {
        var start = new ProcessStartInfo(file) { WorkingDirectory = cwd, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in args)
        {
            start.ArgumentList.Add(a);
        }
        if (pluginsRoot is not null)
        {
            start.Environment["MAF_PLUGINS_ROOT"] = pluginsRoot;
        }
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output);
    }
}
