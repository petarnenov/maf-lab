namespace Maf.Lab.Api.Agent;

/// <summary>Versioned system prompt loaded from Prompts/. Changing it requires an eval run (see README).</summary>
public sealed class SystemPrompt
{
    /// <summary>
    /// The prompt in use unless <c>Agent:SystemPrompt</c> names another. v4 (add-codebase-domain) adds the codebase domain
    /// and its citation rule to v3's data cards; <c>Agent:SystemPrompt=system.v3</c> rolls back.
    /// </summary>
    public const string DefaultVersion = "system.v4";

    public SystemPrompt(IConfiguration configuration)
    {
        Version = configuration["Agent:SystemPrompt"] ?? DefaultVersion;
        var path = Path.Combine(AppContext.BaseDirectory, "Prompts", $"{Version}.md");
        Text = File.ReadAllText(path);
    }

    public string Version { get; }
    public string Text { get; }
}
