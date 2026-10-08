using Maf.Lab.Api.Agent;
using Microsoft.Extensions.Configuration;

namespace Maf.Lab.Tests;

/// <summary>Which system prompt the agent runs with (add-system-prompt-v3, add-codebase-domain, add-neo4j-graph, add-bulgarian-history-domain), and that the previous one is a setting away.</summary>
public class SystemPromptTests
{
    private static SystemPrompt Load(string? version) => new(new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Agent:SystemPrompt"] = version }).Build());

    [Fact]
    public void The_default_is_v6_with_the_history_of_Bulgaria_on_top_of_v5()
    {
        var prompt = Load(null);

        Assert.Equal("system.v6", prompt.Version);
        // add-bulgarian-history-domain: the fourth domain, its search, and answers only from what that search returns.
        Assert.Contains("search_bulgarian_history", prompt.Text);
        Assert.Contains("history of Bulgaria", prompt.Text);
        Assert.Contains("four domains", prompt.Text);
        Assert.Contains("get_aum_history, not search_bulgarian_history", prompt.Text);
        Assert.Contains("rather than answering from general knowledge", prompt.Text);
        // add-neo4j-graph: the graph tools are named, and coverage and callers come from them, not from a snippet.
        Assert.Contains("trace_billing_relationships", prompt.Text);
        Assert.Contains("trace_code_symbol", prompt.Text);
        Assert.Contains("change_impact", prompt.Text);
        Assert.Contains("not from search_codebase", prompt.Text);
        Assert.Contains("## Data cards", prompt.Text);
        Assert.Contains("never calculate trades", prompt.Text);
        // add-codebase-domain: the codebase is a domain with its search, code is cited by place, general programming stays out.
        Assert.Contains("search_codebase", prompt.Text);
        Assert.Contains("path:start-end", prompt.Text);
        Assert.Contains("general programming that is not about this system", prompt.Text);
        Assert.DoesNotContain("travel, coding,", prompt.Text);
    }

    [Fact]
    public void Configuration_rolls_back_to_v5()
    {
        var prompt = Load("system.v5");

        Assert.Equal("system.v5", prompt.Version);
        Assert.Contains("change_impact", prompt.Text);
        Assert.DoesNotContain("search_bulgarian_history", prompt.Text);
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
