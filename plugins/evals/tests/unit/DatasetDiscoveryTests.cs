using System.Text.Json;
using Maf.Lab.Eval.Datasets;

namespace Maf.Lab.Tests;

public sealed class DatasetDiscoveryTests
{
    [Fact]
    public void Common_feedback_and_only_installed_owners_are_loaded_together()
    {
        var root = Directory.CreateTempSubdirectory("maf-dataset-discovery-").FullName;
        var data = Directory.CreateDirectory(Path.Combine(root, "evals")).FullName;
        var plugins = Directory.CreateDirectory(Path.Combine(root, "plugins")).FullName;
        Directory.CreateDirectory(Path.Combine(plugins, "weather", "evals"));
        Directory.CreateDirectory(Path.Combine(plugins, "traffic", "evals"));
        File.WriteAllText(Path.Combine(data, "injection.jsonl"), Row("feedback"));
        File.WriteAllText(Path.Combine(plugins, "weather", "evals", "injection.jsonl"), Row("weather"));
        File.WriteAllText(Path.Combine(plugins, "traffic", "evals", "injection.jsonl"), Row("traffic"));
        var path = Path.Combine(plugins, ".installed");
        File.WriteAllText(path, """{"plugins":[{"manifest":{"name":"weather"}}]}""");
        Assert.Equal(["feedback", "weather"], DatasetLoader.Injection(data).Select(r => r.Id));
        File.WriteAllText(path, """{"plugins":[]}""");
        Assert.Equal(["feedback"], DatasetLoader.Injection(data).Select(r => r.Id));
    }

    private static string Row(string id) => JsonSerializer.Serialize(new
    {
        id, question = "a fixture", tenantId = "firm-a", forbiddenStrings = new[] { "secret" },
        forbiddenTenantIds = new[] { "firm-b" },
    }) + "\n";
}
