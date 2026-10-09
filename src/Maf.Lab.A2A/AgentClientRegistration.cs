namespace Maf.Lab.A2A;

/// <summary>An outbound OAuth client registration at one agent's authorization server; deployment configuration.</summary>
public sealed class AgentClientRegistration
{
    public string BaseUrl { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
}
