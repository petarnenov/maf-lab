using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Plugins.Abstractions;
using Maf.Lab.Plugins.$Name;

namespace Maf.Lab.Tests;

/// <summary>The $name plugin's route, with the plugin installed as make installs it.</summary>
public class ${Name}PluginTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_summary_counts_the_callers_own_conversations()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel()) { InstalledPlugins = [Manifest()] };
        var adam = api.ClientFor("adam", "firm-a", Role.USER);
        await ApiFactory.ChatAsync(adam, "what is the procedure when a fee schedule is missing");
        await ApiFactory.ChatAsync(api.ClientFor("rita", "firm-a", Role.USER), "what is the procedure when a fee schedule is missing");

        var summary = await adam.GetFromJsonAsync<${Name}Summary>("/api/$name/summary", Ct);

        Assert.Equal(new ${Name}Summary(1, false), summary);
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.CreateClient().GetAsync("/api/$name/summary", Ct)).StatusCode);
    }

    /// <summary>The manifest as make reads it.</summary>
    private static PluginManifest Manifest()
    {
        var start = new ProcessStartInfo("python3") { WorkingDirectory = CorpusLoaderTests.RepoRoot(), RedirectStandardOutput = true };
        foreach (var a in new[] { Path.Combine("scripts", "plugins.py"), "manifest-json", ${Name}Plugin.PluginName })
        {
            start.ArgumentList.Add(a);
        }
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return JsonDocument.Parse(output).RootElement.GetProperty("manifest").Deserialize<PluginManifest>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }
}
