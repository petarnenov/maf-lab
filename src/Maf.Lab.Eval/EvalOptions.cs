namespace Maf.Lab.Eval;

public sealed class EvalOptions
{
    public const string Section = "Evals";

    /// <summary>Folder with the JSONL datasets and reports/. Empty = repo evals/.</summary>
    public string Root { get; set; } = "";
    /// <summary>Use an already running MCP server; empty = host the retrieval server in-process on a loopback port.</summary>
    public string McpEndpoint { get; set; } = "";
    /// <summary>
    /// The portfolio domain's MCP server; empty = host it in-process on a loopback port, as the retrieval server is.
    /// </summary>
    public string PortfolioMcpEndpoint { get; set; } = "";
    /// <summary>The codebase MCP server (add-codebase-domain); empty = host it in-process on a loopback port.</summary>
    public string CodeMcpEndpoint { get; set; } = "";
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
    /// Keeps the run's work directory instead of deleting it, and prints where it is. Its `eval.db` holds the
    /// turn traces the run produced, which is the only way to read what a run cost per stage after the fact.
    /// Off for an ordinary run: a kept directory is a temp directory nobody cleans up.
    /// </summary>
    public bool KeepWorkDir { get; set; }

    /// <summary>
    /// Tolerances that differ from the default, one entry each. A list rather than a dictionary because a metric
    /// name carries the configuration path separator — `recall@5:bg` — so it cannot be a configuration key; as a
    /// value it is just a string. An entry without a metric applies to its whole suite.
    /// </summary>
    public List<MetricTolerance> RegressionTolerances { get; set; } = [];

    /// <summary>
    /// suite → how many times a run executes it, its metrics read as the mean (adopt-meai-evaluation). For a suite that
    /// drives the live agent, whose answers vary from run to run; <c>--repeat N</c> overrides it for one run.
    /// </summary>
    public Dictionary<string, int> Repeat { get; set; } = new();

    /// <summary>The Jev grade of generated answers (adopt-meai-evaluation).</summary>
    public JudgeOptions Judge { get; set; } = new();

    /// <summary>The metric's own tolerance, else its suite's, else the default.</summary>
    public double ToleranceFor(string suite, string metric) =>
        RegressionTolerances.FirstOrDefault(t => t.Suite == suite && t.Metric == metric)?.Tolerance
        ?? ToleranceFor(suite);

    public double ToleranceFor(string suite) =>
        RegressionTolerances.FirstOrDefault(t => t.Suite == suite && t.Metric is null)?.Tolerance ?? RegressionTolerance;
}

/// <summary>
/// How far one metric — or a whole suite, when <paramref name="Metric"/> is null — may fall below its baseline
/// before it counts as a regression.
/// </summary>
/// <param name="Measured">The observed spread this number came from. A tolerance nobody can trace back to a
/// measurement gets widened by the next person who sees a red gate, and then the gate means nothing.</param>
/// <remarks>
/// Optional parameters come last on purpose: configuration binding can only construct an entry whose every
/// parameter without a default is present, so a suite-wide entry that omits <paramref name="Metric"/> would
/// otherwise be skipped silently rather than reported.
/// </remarks>
public sealed record MetricTolerance(string Suite, double Tolerance, string? Metric = null, string? Measured = null);

/// <summary>How the generation grade asks Jev (adopt-meai-evaluation, design D3).</summary>
public sealed class JudgeOptions
{
    /// <summary>Per request. An eval, not a turn: nobody waits on it, so the budget is wider than the answer check's 3 s.</summary>
    public double TimeoutSeconds { get; set; } = 10;

    /// <summary>Sentences graded per answer; the rest are left out and the case says it was truncated.</summary>
    public int MaxSentences { get; set; } = 60;

    /// <summary>
    /// Characters of sources sent, whole items in the order the answer check sends them (cited first). Twice the
    /// check's cap: the check sends nothing over its cap, the grade has to grade every answer.
    /// </summary>
    public int MaxSourceChars { get; set; } = 24000;
}

