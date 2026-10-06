using Maf.Lab.Api.Agent;
using Microsoft.Extensions.Configuration;

namespace Maf.Lab.Tests;

/// <summary>Which system prompt the agent runs with (add-system-prompt-v3, add-codebase-domain, add-neo4j-graph, introduce-plugins), and that the previous one is a setting away.</summary>
public class SystemPromptTests
{
    private static SystemPrompt Load(string? version) => new(new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Agent:SystemPrompt"] = version }).Build());

    [Fact]
    public void The_default_is_v6_assembled_from_the_built_in_domains()
    {
        // Portfolio built in and the stand-in billing domain (no fragment of its own: billing's is the plugin's).
        using var domains = DomainCatalogue.Use(StandInDomains.WithBilling);
        var prompt = Load(null);

        Assert.Equal("core.v6", prompt.Version);
        Assert.DoesNotContain("{{", prompt.Text);
        Assert.DoesNotContain("<!--", prompt.Text);
        Assert.Contains("## Data cards", prompt.Text);
        Assert.Contains("never calculate trades", prompt.Text);
        // (Crossing into billing needs billing's own fragment: BillingSystemPromptTests, in the billing plugin.)
        Assert.Contains("general programming that is not about these domains", prompt.Text);
        Assert.DoesNotContain("travel, coding,", prompt.Text);
        // A domain that is not in use is not in the prompt (introduce-plugins 5g): without the code plugin, no code.
        Assert.DoesNotContain("search_codebase", prompt.Text);
    }

    [Fact]
    public void Only_the_domains_in_use_are_in_the_prompt()
    {
        var portfolio = DomainCatalogue.Current.Get(Maf.Lab.Api.BuiltIn.BuiltInDomains.Portfolio)!;

        var text = SystemPrompt.Assemble(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Prompts", "core.v6.md")), [portfolio]);

        Assert.Contains("get_household_portfolio", text);
        Assert.DoesNotContain("get_billing_run_status", text);
        Assert.DoesNotContain("search_codebase", text);
        // One domain: nothing crosses into another.
        Assert.DoesNotContain("cross from one domain", text);
        Assert.Contains("You answer only about its portfolios, as covered", text);
    }

    [Fact]
    public void Configuration_rolls_back_to_v5()
    {
        var prompt = Load("system.v5");

        Assert.Equal("system.v5", prompt.Version);
        Assert.Contains("general programming that is not about this system", prompt.Text);
    }

    [Fact]
    public void Configuration_rolls_back_to_v4()
    {
        var prompt = Load("system.v4");

        Assert.Equal("system.v4", prompt.Version);
        Assert.Contains("search_codebase", prompt.Text);
        Assert.DoesNotContain("change_impact", prompt.Text);
    }

    [Fact]
    public void Configuration_rolls_back_to_v3()
    {
        var prompt = Load("system.v3");

        Assert.Equal("system.v3", prompt.Version);
        Assert.DoesNotContain("search_codebase", prompt.Text);
    }

    [Fact]
    public void The_api_settings_do_not_pin_another_prompt()
    {
        // The stack ran v2 after the default moved to v3 because appsettings.json still named v2: the default lives in
        // one place, SystemPrompt.DefaultVersion, and the shipped settings leave it alone.
        var settings = new ConfigurationBuilder().AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json")).Build();

        Assert.Null(settings["Agent:SystemPrompt"]);
    }

    [Fact]
    public void Configuration_rolls_back_to_v2()
    {
        var prompt = Load("system.v2");

        Assert.Equal("system.v2", prompt.Version);
        Assert.DoesNotContain("## Data cards", prompt.Text);
    }
}
