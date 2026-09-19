namespace Maf.Lab.Eval;

public sealed class EvalOptions
{
    public const string Section = "Evals";

    /// <summary>Folder with the JSONL datasets and reports/. Empty = repo evals/.</summary>
    public string Root { get; set; } = "";
    /// <summary>Use an already running MCP server; empty = host the retrieval server in-process on a loopback port.</summary>
    public string McpEndpoint { get; set; } = "";
    /// <summary>Collection indexed with contextual retrieval on, for the contextual variant.</summary>
    public string ContextualCollection { get; set; } = "maf_chunks_ctx";
    /// <summary>suite → metric → minimum value. Thresholds are configuration, not code.</summary>
    public Dictionary<string, Dictionary<string, double>> Thresholds { get; set; } = new();
}
