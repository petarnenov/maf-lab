namespace Maf.Lab.Api.Agent;

public sealed class AgentOptions
{
    public const string Section = "Agent";

    /// <summary>The billing domain's MCP server — the first server, and the domain every unmapped tool belongs to.</summary>
    public string McpEndpoint { get; set; } = "http://localhost:5090/mcp";

    /// <summary>
    /// Further domains, each served by its own MCP server (add-portfolio-domain). A turn offers the union of every
    /// server's tools, each tool known by the domain that owns it.
    /// </summary>
    public List<McpServerOptions> Servers { get; set; } = [];

    /// <summary>Every server of the turn, billing first: the first server to offer a tool name keeps it.</summary>
    public IReadOnlyList<McpServerOptions> AllServers() =>
        [new McpServerOptions { Domain = Domains.Billing, Endpoint = McpEndpoint },
            .. Servers.Where(s => !string.IsNullOrWhiteSpace(s.Endpoint) && !string.IsNullOrWhiteSpace(s.Domain))];
    public int HistoryTokenBudget { get; set; } = 3000;
    public int LongAnswerChars { get; set; } = 800;
    /// <summary>Issue the forced search_documents call on the model's behalf (Ollama ignores tool_choice).</summary>
    public bool EmulateRequiredToolMode { get; set; } = true;
    /// <summary>Request retrieval diagnostics from the MCP server for the behind-the-scenes monitor.</summary>
    public bool TraceRetrieval { get; set; } = true;
}

/// <summary>One domain's MCP server.</summary>
public sealed class McpServerOptions
{
    public string Domain { get; set; } = "";
    public string Endpoint { get; set; } = "";
    /// <summary>The server's tools the agent is offered; empty offers every tool it lists (add-codebase-domain).</summary>
    public List<string> Tools { get; set; } = [];
}
