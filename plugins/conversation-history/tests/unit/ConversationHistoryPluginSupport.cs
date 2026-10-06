using System.Diagnostics;
using System.Text.Json;
using Maf.Lab.Plugins.Abstractions;
using Maf.Lab.Plugins.ConversationHistory;
using Maf.Lab.TestSupport;

namespace Maf.Lab.Tests;

/// <summary>The conversation list as its tests see it: its manifest as make reads it, and an api with it installed.</summary>
public static class ConversationHistoryPluginSupport
{
    private static readonly Lazy<PluginManifest> Loaded = new(Load);

    public static PluginManifest Manifest => Loaded.Value;

    /// <summary>An api with the conversation list installed.</summary>
    public static ApiFactory Api(ScriptedChatClient chat) => new(chat) { InstalledPlugins = [Manifest, .. StandInDomains.Installed] };

    private static PluginManifest Load()
    {
        var start = new ProcessStartInfo("python3")
        {
            WorkingDirectory = CorpusLoaderTests.RepoRoot(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in new[] { Path.Combine("scripts", "plugins.py"), "manifest-json", ConversationHistoryPlugin.PluginName })
        {
            start.ArgumentList.Add(a);
        }
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return JsonDocument.Parse(output).RootElement.GetProperty("manifest").Deserialize<PluginManifest>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }
}
