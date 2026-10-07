using Microsoft.AspNetCore.Hosting;

namespace Maf.Lab.TestSupport;

/// <summary>
/// The core tests' providers for a hosted program other than the api (an MCP server): an installed set holding only the
/// fixture decision engine, so the program starts with exactly one engine (ProviderHost) and asks a fake one.
/// </summary>
public static class TestProviders
{
    private static readonly Lazy<string> Root = new(() =>
    {
        var root = Path.Combine(Path.GetTempPath(), "maf-lab-test-providers", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var document = new
        {
            schema = 1,
            env = "dev",
            plugins = new[] { new { manifest = FixtureEnginePlugin.Manifest, serverJson = (object?)null, hasServer = false } },
        };
        File.WriteAllText(Path.Combine(root, ".installed"),
            System.Text.Json.JsonSerializer.Serialize(document, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)));
        return root;
    });

    /// <summary>Installs the fixture engine in the program <paramref name="builder"/> hosts.</summary>
    public static IWebHostBuilder UseFixtureEngine(this IWebHostBuilder builder) => builder
        .UseSetting("Plugins:Root", Root.Value)
        .UseSetting("Plugins:ExtraAssemblies:0", typeof(TestProviders).Assembly.GetName().Name);
}
