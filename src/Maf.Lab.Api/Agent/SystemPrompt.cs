namespace Maf.Lab.Api.Agent;

/// <summary>Versioned system prompt loaded from Prompts/. Changing it requires an eval run (see README).</summary>
public sealed class SystemPrompt
{
    public SystemPrompt(IConfiguration configuration)
    {
        Version = configuration["Agent:SystemPrompt"] ?? "system.v1";
        var path = Path.Combine(AppContext.BaseDirectory, "Prompts", $"{Version}.md");
        Text = File.ReadAllText(path);
    }

    public string Version { get; }
    public string Text { get; }
}
