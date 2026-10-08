namespace Maf.Lab.Api.Agent;

/// <summary>Versioned system prompt loaded from Prompts/. Changing it requires an eval run (see README).</summary>
public sealed class SystemPrompt
{
    /// <summary>
    /// The prompt in use unless <c>Agent:SystemPrompt</c> names another. v6 (add-bulgarian-history-domain) adds the
    /// fourth domain, the history of Bulgaria, with its search and its scope rule to v5's graph tools;
    /// <c>Agent:SystemPrompt=system.v5</c> rolls back.
    /// </summary>
    public const string DefaultVersion = "system.v6";

    public SystemPrompt(IConfiguration configuration)
    {
        Version = configuration["Agent:SystemPrompt"] ?? DefaultVersion;
        var path = Path.Combine(AppContext.BaseDirectory, "Prompts", $"{Version}.md");
        Text = File.ReadAllText(path);
    }

    public string Version { get; }
    public string Text { get; }
}
