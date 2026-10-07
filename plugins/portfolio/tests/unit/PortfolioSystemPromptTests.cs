using Maf.Lab.Api.Agent;
using Microsoft.Extensions.Configuration;

namespace Maf.Lab.Tests;

/// <summary>
/// The portfolio fragment of the system prompt (add-portfolio-domain, add-activity-cards, introduce-plugins 4.7), moved
/// with the domain into its plugin (extract-portfolio) and run in the views the assertions were written for.
/// </summary>
public class PortfolioSystemPromptTests
{
    private static SystemPrompt Load(string? version) => new(new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Agent:SystemPrompt"] = version }).Build());

    /// <summary>
    /// The core tests' stand-in for billing, with a summary of its own: the crossing between domains is said only when more
    /// than one domain in use has a fragment.
    /// </summary>
    private static DomainCatalogue WithAnotherDomain() => DomainCatalogue.Of(
        [StandInDomains.BillingManifest.Domain!.ToDescriptor("<!-- summary -->\nits firm's fee billing"), PortfolioPluginSupport.Descriptor],
        [new Plugins.StandInBillingBehaviour(), new Maf.Lab.Plugins.Portfolio.PortfolioBehaviour()]);

    [Fact]
    public void The_default_carries_the_data_cards_section_and_crosses_into_another_domain()
    {
        using var domains = DomainCatalogue.Use(WithAnotherDomain());
        var prompt = Load(null);

        Assert.Equal("core.v6", prompt.Version);
        Assert.DoesNotContain("{{", prompt.Text);
        Assert.Contains("## Data cards", prompt.Text);
        Assert.Contains("never calculate trades", prompt.Text);
        Assert.Contains("cross from one domain", prompt.Text);
    }

    [Fact]
    public void Alone_it_is_the_only_domain_in_the_prompt()
    {
        var text = SystemPrompt.Assemble(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Prompts", "core.v6.md")),
            [PortfolioPluginSupport.Descriptor]);

        Assert.Contains("get_household_portfolio", text);
        Assert.DoesNotContain("get_billing_run_status", text);
        Assert.DoesNotContain("search_codebase", text);
        // One domain: nothing crosses into another.
        Assert.DoesNotContain("cross from one domain", text);
        Assert.Contains("You answer only about its portfolios, as covered", text);
    }
}
