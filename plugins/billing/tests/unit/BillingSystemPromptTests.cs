using Maf.Lab.Api.Agent;
using Microsoft.Extensions.Configuration;

namespace Maf.Lab.Tests;

/// <summary>
/// The billing fragment of the system prompt (add-neo4j-graph, introduce-plugins 4.7), moved with the domain into its
/// plugin (extract-billing) and run in the view the assertions were written for: billing from this folder, portfolio beside it (the core tests' stand-in).
/// </summary>
public class BillingSystemPromptTests
{
    private static SystemPrompt Load(string? version) => new(new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Agent:SystemPrompt"] = version }).Build());

    [Fact]
    public void The_default_names_the_billing_graph_tool()
    {
        using var domains = BillingPluginSupport.Use();
        var prompt = Load(null);

        Assert.Equal("core.v6", prompt.Version);
        Assert.DoesNotContain("{{", prompt.Text);
        Assert.Contains("trace_billing_relationships", prompt.Text);
        // (The crossing into portfolio is portfolio's own fragment: PortfolioSystemPromptTests.)
    }
}
