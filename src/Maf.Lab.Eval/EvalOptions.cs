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
    /// <summary>
    /// How far below the baseline a metric may fall before it counts as a regression. generation and selection call
    /// a live model and vary between runs; a gate that cries wolf gets switched off.
    /// </summary>
    public double RegressionTolerance { get; set; } = 0.02;
    /// <summary>
    /// Per-suite override, for a suite whose measured run-to-run noise exceeds the default. retrieval needs one
    /// because a non-English query is translated by a live model, and a different translation retrieves different
    /// chunks: `recall@5:bg` was measured alternating between 0.660 and 0.681 across four runs, while the English
    /// metric never moved.
    /// </summary>
    public Dictionary<string, double> RegressionTolerances { get; set; } = new();

    /// <summary>
    /// Keeps the run's work directory instead of deleting it, and prints where it is. Its `eval.db` holds the
    /// turn traces the run produced, which is the only way to read what a run cost per stage after the fact.
    /// Off for an ordinary run: a kept directory is a temp directory nobody cleans up.
    /// </summary>
    public bool KeepWorkDir { get; set; }

    public double ToleranceFor(string suite) =>
        RegressionTolerances.TryGetValue(suite, out var tolerance) ? tolerance : RegressionTolerance;
}
