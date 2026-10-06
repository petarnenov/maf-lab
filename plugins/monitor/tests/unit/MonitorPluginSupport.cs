using System.Diagnostics;
using System.Text.Json;
using Maf.Lab.Plugins.Abstractions;
using Maf.Lab.Plugins.Monitor;
using Maf.Lab.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maf.Lab.Tests;

/// <summary>The monitor as its tests see it: its manifest as make reads it, and an api with it installed.</summary>
public static class MonitorPluginSupport
{
    private static readonly Lazy<PluginManifest> Loaded = new(Load);

    public static PluginManifest Manifest => Loaded.Value;

    /// <summary>An api with the monitor installed.</summary>
    public static ApiFactory Api(ScriptedChatClient chat, FakeToolSource? tools = null, IReadOnlyDictionary<string, string?>? settings = null) =>
        new(chat, tools)
        {
            InstalledPlugins = [Manifest],
            ExtraSettings = settings ?? new Dictionary<string, string?>(),
        };

    /// <summary>The one store, as the monitor reaches its table.</summary>
    public static DbContext Db(ApiFactory api) =>
        api.Services.GetRequiredService<IDbContextFactory<DbContext>>().CreateDbContext();

    private static PluginManifest Load()
    {
        var start = new ProcessStartInfo("python3")
        {
            WorkingDirectory = CorpusLoaderTests.RepoRoot(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in new[] { Path.Combine("scripts", "plugins.py"), "manifest-json", MonitorPlugin.PluginName })
        {
            start.ArgumentList.Add(a);
        }
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return JsonDocument.Parse(output).RootElement.GetProperty("manifest").Deserialize<PluginManifest>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }
}
