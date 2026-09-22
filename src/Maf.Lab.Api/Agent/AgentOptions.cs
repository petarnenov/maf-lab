namespace Maf.Lab.Api.Agent;

public sealed class AgentOptions
{
    public const string Section = "Agent";

    public string McpEndpoint { get; set; } = "http://localhost:5090/mcp";
    public int HistoryTokenBudget { get; set; } = 3000;
    public int LongAnswerChars { get; set; } = 800;
    /// <summary>Issue the forced search_documents call on the model's behalf (Ollama ignores tool_choice).</summary>
    public bool EmulateRequiredToolMode { get; set; } = true;
    /// <summary>Request retrieval diagnostics from the MCP server for the behind-the-scenes monitor.</summary>
    public bool TraceRetrieval { get; set; } = true;
    /// <summary>
    /// Model that classifies questions the English rules do not recognise. Chosen for this job rather than
    /// inherited from the answering model: a reasoning model spends its budget thinking before it emits the one
    /// word this asks for. Measured against `make eval SUITE=selection` — median 479 ms against gpt-oss:120b's
    /// 1049 ms over the same runs, with selection quality no worse. Empty falls back to the chat model.
    /// </summary>
    public string IntentModel { get; set; } = "gemma4:31b";
    /// <summary>Budget for that classification; 0 disables it and leaves unrecognised questions as Other.</summary>
    public double IntentTimeoutSeconds { get; set; } = 5;
}
