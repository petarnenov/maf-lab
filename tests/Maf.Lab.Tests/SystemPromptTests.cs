using Maf.Lab.Api.Agent;
using Microsoft.Extensions.Configuration;

namespace Maf.Lab.Tests;

/// <summary>Which system prompt the agent runs with (add-system-prompt-v3), and that the previous one is a setting away.</summary>
public class SystemPromptTests
{
    private static SystemPrompt Load(string? version) => new(new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Agent:SystemPrompt"] = version }).Build());

    [Fact]
    public void The_default_is_v3_and_it_builds_on_the_data_cards()
    {
        var prompt = Load(null);

        Assert.Equal("system.v3", prompt.Version);
        Assert.Contains("## Data cards", prompt.Text);
        Assert.Contains("never calculate trades", prompt.Text);
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
