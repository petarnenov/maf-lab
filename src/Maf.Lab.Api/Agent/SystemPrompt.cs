namespace Maf.Lab.Api.Agent;

/// <summary>Versioned system prompt loaded from Prompts/. Changing it requires an eval run (see README).</summary>
public sealed class SystemPrompt
{
    /// <summary>
    /// The prompt in use unless <c>Agent:SystemPrompt</c> names another. v3 (add-system-prompt-v3) builds on the data
    /// cards instead of restating them; <c>Agent:SystemPrompt=system.v2</c> rolls back.
    /// </summary>
    public const string DefaultVersion = "system.v3";

    public SystemPrompt(IConfiguration configuration)
    {
        Version = configuration["Agent:SystemPrompt"] ?? DefaultVersion;
        var path = Path.Combine(AppContext.BaseDirectory, "Prompts", $"{Version}.md");
        Text = File.ReadAllText(path);
    }

    public string Version { get; }
    public string Text { get; }
}
