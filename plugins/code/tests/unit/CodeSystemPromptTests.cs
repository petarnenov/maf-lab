using Maf.Lab.Api.Agent;
using Maf.Lab.Plugins.Code;
using Microsoft.Extensions.Configuration;

namespace Maf.Lab.Tests;

/// <summary>
/// The codebase fragment of the system prompt (add-codebase-domain, add-neo4j-graph, introduce-plugins 4.7), moved with
/// the domain into its plugin (5.2) and run in the three-domain view the assertions were written for.
/// </summary>
public class CodeSystemPromptTests
{
    private static SystemPrompt Load(string? version) => new(new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Agent:SystemPrompt"] = version }).Build());

    [Fact]
    public void The_default_is_v6_assembled_from_the_domains_in_use_with_everything_v5_said()
    {
        using var domains = CodePluginSupport.Use();
        var prompt = Load(null);

        Assert.Equal("core.v6", prompt.Version);
        Assert.DoesNotContain("{{", prompt.Text);
        Assert.DoesNotContain("<!--", prompt.Text);
        // add-neo4j-graph: the graph tools are named, and coverage and callers come from them, not from a snippet.
        // (Billing's graph tool is named in billing's own fragment, the billing plugin's: the stand-in here has none.)
        Assert.Contains("trace_code_symbol", prompt.Text);
        Assert.Contains("change_impact", prompt.Text);
        Assert.Contains("not from search_codebase", prompt.Text);
        Assert.Contains("## Data cards", prompt.Text);
        Assert.Contains("never calculate trades", prompt.Text);
        // add-codebase-domain: the codebase is a domain with its search, code is cited by place, general programming stays out.
        Assert.Contains("search_codebase", prompt.Text);
        Assert.Contains("path:start-end", prompt.Text);
        Assert.Contains("general programming that is not about these domains", prompt.Text);
        Assert.DoesNotContain("travel, coding,", prompt.Text);
    }

    [Fact]
    public void Only_the_domains_in_use_are_in_the_prompt()
    {
        using var domains = CodePluginSupport.Use();
        var prompt = Load(null);
        var code = DomainCatalogue.Current.Get(CodePlugin.DomainId)!;

        var text = SystemPrompt.Assemble(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Prompts", "core.v6.md")), [code]);

        Assert.Contains("search_codebase", text);
        Assert.DoesNotContain("search_documents", text);
        Assert.DoesNotContain("get_household_portfolio", text);
        Assert.DoesNotContain("## Data cards", text);
        // One domain: nothing crosses into another.
        Assert.DoesNotContain("cross from one domain", text);
        Assert.Contains("cross from one domain", prompt.Text);
        Assert.Contains("You answer only about this lab's own code, as covered", text);
    }
}
