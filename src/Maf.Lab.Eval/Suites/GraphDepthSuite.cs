using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using Maf.Lab.Api.Agent;
using Maf.Lab.CodeSearch;
using Maf.Lab.Domain.Evals;
using Maf.Lab.Domain.Graph;
using Maf.Lab.Eval.Datasets;
using Maf.Lab.Eval.Hosting;
using Maf.Lab.Eval.Judging;
using Maf.Lab.Plugins.Abstractions;
using Maf.Lab.Hosting.Cli;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Maf.Lab.Eval.Suites;

/// <param name="Depth">The depth the variant's codebase server is pinned to (CodeSearch:GraphDepthPin).</param>
public sealed record GraphDepthVariant(string Name, int Depth);

/// <summary>What one graph tool call returned, reduced to what the comparison counts.</summary>
/// <param name="Items">What a needed item is matched against: method symbols for a trace, test files for an impact.</param>
/// <param name="Nodes">Every method the result lists, tests included: what the model has to read.</param>
/// <param name="Unresolved">Why the case's symbol or file was not found, when it was not: the dataset needs fixing.</param>
public sealed record GraphDepthProbe(IReadOnlyList<string> Items, int Nodes, bool Truncated, int Tokens, double LatencyMs, string? Unresolved = null);

/// <summary>
/// The graph-depth comparison (add-graph-depth-eval): the same labelled code-graph cases with the graph tools pinned to
/// 2, 3 and 4 calls. The structural layer calls the tools directly, with no model; the end-to-end layer asks the agent
/// and grades the answer with the Jev grade the generation suite uses (graph-depth-jev-grade, DECISIONS.md §80): its
/// claims against what the turn read, its relevance to the question. Whether it names the labelled items is
/// <c>mentionRecall</c>, a match in code. A comparison: it reports, it does not gate.
/// </summary>
public sealed class GraphDepthSuite(IConfiguration configuration, DecisionGrader? grader)
{
    public const string Name = "graph-depth";
    public static readonly IReadOnlyList<GraphDepthVariant> Variants = [new("depth-2", 2), new("depth-3", 3), new("depth-4", 4)];
    public static readonly string PinSetting = $"{CodeSearchOptions.Section}:{nameof(CodeSearchOptions.GraphDepthPin)}";

    private readonly TokenCounter _tokens = new();

    /// <param name="structuralOnly">No chat turn and no grade: needs neither the chat model's key nor Jev's.</param>
    public async Task<IReadOnlyList<EvalVariantResult>> RunAsync(SuiteContext ctx, bool structuralOnly, CancellationToken ct)
    {
        var cases = ctx.Take(DatasetLoader.GraphDepth(ctx.DatasetRoot)).ToList();
        if (!structuralOnly && grader is not { IsConfigured: true })
        {
            throw new InvalidOperationException(
                "graph-depth: the end-to-end layer is graded by the decision engine and needs it configured; pass --structural-only to run without it.");
        }
        using var console = new ConsoleProgress(Name);
        var bar = new GraphDepthProgress(console, cases.Count, Variants.Count, structuralOnly ? 1 : 2);
        try
        {
            var perVariant = new List<(GraphDepthVariant Variant, Dictionary<string, double> Metrics, List<EvalCaseFailure> Failures)>();
            var answersByVariant = new Dictionary<string, IReadOnlyList<(TurnResult? Turn, JudgeScore Score, bool JudgeFailed)>>();
            foreach (var variant in Variants)
            {
                var failures = new List<EvalCaseFailure>();
                var probes = await StructuralAsync(variant, cases, bar, ct);
                var metrics = StructuralMetrics(cases, probes, failures);
                ctx.Progress($"{Name} {variant.Name} structural: {Describe(metrics)}");
                if (!structuralOnly)
                {
                    var answers = await EndToEndAsync(variant, cases, bar, ct);
                    answersByVariant[variant.Name] = answers;
                    foreach (var (key, value) in EndToEndMetrics(cases, answers, failures))
                    {
                        metrics[key] = value;
                    }
                    ctx.Progress($"{Name} {variant.Name} end-to-end: faithfulness={metrics["faithfulness"]:0.###} relevance={metrics["relevance"]:0.###} " +
                        $"mentionRecall={metrics["mentionRecall"]:0.###} graphToolCalled={metrics["graphToolCalled"]:0.###}");
                    // Apart from the scores: an outage of the judge is not a quality result.
                    if (metrics["judgeFailures"] > 0)
                    {
                        ctx.Progress($"{Name} {variant.Name}: {metrics["judgeFailures"]:0} judge failure(s), scored 0 and named in the report");
                    }
                }
                perVariant.Add((variant, metrics, failures));
            }
            if (!structuralOnly)
            {
                // Only once every variant has answered: the common cases are those that called the graph in all of them.
                var graphTurns = GraphTurnMetrics(cases, answersByVariant);
                foreach (var (variant, metrics, failures) in perVariant)
                {
                    foreach (var (key, value) in graphTurns.Metrics[variant.Name])
                    {
                        metrics[key] = value;
                    }
                    failures.AddRange(graphTurns.NotCommon.Select(n => new EvalCaseFailure(n.CaseId, $"not common: graph not called in {n.Variant}")));
                }
                ctx.Progress($"{Name}: {graphTurns.CommonCases} of {cases.Count} case(s) called the graph in every variant — " +
                    string.Join(" ", perVariant.Select(v => $"{v.Variant.Name} mentionRecall:common=" +
                        (v.Metrics.TryGetValue("mentionRecall:common", out var m) ? m.ToString("0.###") : "-"))));
            }
            bar.Succeed();
            // No thresholds: a comparison suite reports side by side and never gates.
            return [.. perVariant.Select(v => SuiteContext.Variant(v.Variant.Name, v.Metrics, new Dictionary<string, double>(), cases.Count, v.Failures))];
        }
        catch (OperationCanceledException)
        {
            bar.Cancel();
            throw;
        }
        catch (Exception ex)
        {
            bar.Fail(ex.GetType().Name);
            throw;
        }
    }

    private async Task<IReadOnlyList<GraphDepthProbe>> StructuralAsync(GraphDepthVariant variant, IReadOnlyList<GraphDepthCase> cases,
        GraphDepthProgress bar, CancellationToken ct)
    {
        bar.Layer(variant, "structural");
        var (server, endpoint) = await StartAsync(variant, () => EvalAgentHost.StartCodeServerAsync(configuration, PinOf(variant), ct));
        var clients = new Dictionary<string, McpClient>(StringComparer.Ordinal);
        try
        {
            if (cases.Count > 0)
            {
                // One unmeasured call first: a fresh server's first calls pay for JIT and connections, and the variant
                // that runs first would otherwise carry that cost in its latency.
                var first = cases[0];
                await ProbeAsync(variant, clients[first.TenantId] = await ConnectAsync(endpoint, EvalAgentHost.EvalToken(configuration, first.TenantId), ct), first, ct);
            }
            var probes = new List<GraphDepthProbe>();
            foreach (var c in cases)
            {
                bar.Working(c);
                if (!clients.TryGetValue(c.TenantId, out var client))
                {
                    client = clients[c.TenantId] = await ConnectAsync(endpoint, EvalAgentHost.EvalToken(configuration, c.TenantId), ct);
                }
                probes.Add(await ProbeAsync(variant, client, c, ct));
                bar.Advance();
            }
            // Whether a symbol or file resolves does not depend on the depth: one look is enough, and a dataset that
            // names something the graph does not have measures nothing until it is fixed.
            var unresolved = cases.Zip(probes).Where(x => x.Second.Unresolved is not null).ToList();
            if (unresolved.Count > 0)
            {
                throw new InvalidDataException($"graph-depth.jsonl: {unresolved.Count} case(s) do not resolve in the code graph: " +
                    string.Join("; ", unresolved.Select(x => $"{x.First.Id} ({x.Second.Unresolved})")));
            }
            return probes;
        }
        finally
        {
            foreach (var client in clients.Values)
            {
                await client.DisposeAsync();
            }
            // One variant's server at a time: stopped before the next one starts.
            await server.StopAsync(CancellationToken.None);
            await server.DisposeAsync();
        }
    }

    private async Task<GraphDepthProbe> ProbeAsync(GraphDepthVariant variant, McpClient client, GraphDepthCase c, CancellationToken ct)
    {
        var arguments = c.Kind == "trace"
            ? new Dictionary<string, object?> { ["symbol"] = c.Symbol, ["direction"] = c.Direction }
            : new Dictionary<string, object?> { ["path"] = c.Path };
        var watch = Stopwatch.StartNew();
        var result = await client.CallToolAsync(c.Kind == "trace" ? GraphTools.TraceCodeSymbol : GraphTools.ChangeImpact, arguments, cancellationToken: ct);
        var latency = watch.Elapsed.TotalMilliseconds;
        var text = string.Join("\n", result.Content.OfType<TextContentBlock>().Select(b => b.Text));
        if (result.IsError is true)
        {
            // The only error a well-formed case should meet is a file the graph does not have; anything else is the run's.
            if (c.Kind == "impact" && text.Contains("not a C# file of the code graph", StringComparison.Ordinal))
            {
                return new GraphDepthProbe([], 0, false, 0, latency, "not a C# file of the code graph");
            }
            throw new InvalidOperationException($"graph-depth {variant.Name}: {c.Id} failed: {text}");
        }
        var tokens = _tokens.Count(text);
        var structured = result.StructuredContent ?? JsonDocument.Parse(text).RootElement;
        if (c.Kind == "trace")
        {
            var trace = structured.Deserialize<CodeTrace>(JsonSerializerOptions.Web)!;
            if (trace.Depth != variant.Depth)
            {
                throw new InvalidOperationException($"graph-depth {variant.Name}: the server traced to depth {trace.Depth}, so its pin is not in effect.");
            }
            if (trace.Matched.Count == 0)
            {
                return new GraphDepthProbe([], 0, false, tokens, latency,
                    trace.Candidates.Count > 0 ? $"ambiguous: {trace.Candidates.Count} candidates" : "no such symbol");
            }
            return new GraphDepthProbe([.. trace.Reached.Select(h => h.Symbol).Distinct(StringComparer.Ordinal)], trace.Reached.Count,
                trace.Truncated, tokens, latency);
        }
        var impact = structured.Deserialize<ChangeImpact>(JsonSerializerOptions.Web)!;
        return new GraphDepthProbe([.. impact.Tests.Select(t => t.Path)], impact.ReachedFrom.Count + impact.Tests.Sum(t => t.Tests.Count),
            impact.Truncated, tokens, latency);
    }

    private async Task<IReadOnlyList<(TurnResult? Turn, JudgeScore Score, bool JudgeFailed)>> EndToEndAsync(GraphDepthVariant variant,
        IReadOnlyList<GraphDepthCase> cases, GraphDepthProgress bar, CancellationToken ct)
    {
        bar.Layer(variant, "end-to-end");
        var host = await StartAsync(variant, async () => await EvalAgentHost.StartAsync(configuration, ct, codeSettings: PinOf(variant)));
        await using var _ = host;
        host.RequireDomain("codebase", "code");
        var answers = new List<(TurnResult?, JudgeScore, bool)>();
        foreach (var c in cases)
        {
            bar.Working(c);
            var turn = await host.AskAsync(c.TenantId, c.Question, ct);
            // What the turn read, as the generation suite grades it; the labelled items are mentionRecall's, not the grade's.
            var outcome = await grader!.GradeAsync(new GradeInput(c.Question, turn.Answer, turn.Read, []), ct);
            answers.Add(outcome.Grade is { } g
                ? (turn, new JudgeScore(g.Faithfulness, g.Relevance, g.Reason()), false)
                : (turn, new JudgeScore(0, 0, $"judge failed: {outcome.Failure}"), true));
            bar.Advance();
        }
        return answers;
    }

    private static async Task<T> StartAsync<T>(GraphDepthVariant variant, Func<Task<T>> start)
    {
        try
        {
            return await start();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException($"graph-depth {variant.Name}: the codebase server pinned at {variant.Depth} could not start ({ex.GetType().Name}: {ex.Message}).", ex);
        }
    }

    private static Dictionary<string, string?> PinOf(GraphDepthVariant variant) =>
        new() { [PinSetting] = variant.Depth.ToString(System.Globalization.CultureInfo.InvariantCulture) };

    private static async Task<McpClient> ConnectAsync(string endpoint, string token, CancellationToken ct)
    {
        var http = new HttpClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(endpoint),
            TransportMode = HttpTransportMode.StreamableHttp,
        }, http, NullLoggerFactory.Instance, ownsHttpClient: true);
        return await McpClient.CreateAsync(transport, cancellationToken: ct);
    }

    /// <summary>
    /// recall (mean share of needed items reached), fullRecall, recall@needsK by the depth a case needs, signalShare
    /// (needed items reached ÷ items of that kind returned), meanNodes, meanTokens, truncatedRate, latency p50/p95.
    /// </summary>
    public static Dictionary<string, double> StructuralMetrics(IReadOnlyList<GraphDepthCase> cases, IReadOnlyList<GraphDepthProbe> probes,
        List<EvalCaseFailure> failures)
    {
        var n = Math.Max(1, cases.Count);
        double recall = 0, full = 0, reachedNeeded = 0, returnedItems = 0;
        var byDepth = new SortedDictionary<int, (double Recall, int Count)>();
        foreach (var (c, p) in cases.Zip(probes))
        {
            var items = p.Items.ToHashSet(StringComparer.Ordinal);
            var missing = c.Needed.Where(x => !items.Contains(x.Item)).ToList();
            var caseRecall = (double)(c.Needed.Count - missing.Count) / c.Needed.Count;
            recall += caseRecall;
            full += missing.Count == 0 ? 1 : 0;
            reachedNeeded += c.Needed.Count - missing.Count;
            returnedItems += items.Count;
            var seen = byDepth.GetValueOrDefault(c.RequiredDepth);
            byDepth[c.RequiredDepth] = (seen.Recall + caseRecall, seen.Count + 1);
            if (missing.Count > 0)
            {
                failures.Add(new EvalCaseFailure(c.Id, $"recall={caseRecall:0.##}; missing: " +
                    string.Join(", ", missing.Select(m => $"{m.Item} ({(m.Hops is { } h ? $"{h} calls" : "beyond 4")})"))));
            }
        }
        var metrics = new Dictionary<string, double>
        {
            ["recall"] = recall / n,
            ["fullRecall"] = full / n,
            ["signalShare"] = returnedItems == 0 ? 0 : reachedNeeded / returnedItems,
            ["meanNodes"] = probes.Count == 0 ? 0 : probes.Average(p => p.Nodes),
            ["meanTokens"] = probes.Count == 0 ? 0 : probes.Average(p => p.Tokens),
            ["truncatedRate"] = probes.Count == 0 ? 0 : (double)probes.Count(p => p.Truncated) / probes.Count,
            ["latencyP50Ms"] = Percentile(probes.Select(p => p.LatencyMs), 0.50),
            ["latencyP95Ms"] = Percentile(probes.Select(p => p.LatencyMs), 0.95),
        };
        foreach (var (depth, (sum, count)) in byDepth)
        {
            metrics[$"recall@needs{depth}"] = sum / count;
        }
        return metrics;
    }

    /// <summary>
    /// faithfulness and relevance from the Jev grade, mentionRecall (needed items the answer names), graphToolCalled (turns
    /// that called a graph tool at all; a turn that did not is still scored), and judgeFailures, a count.
    /// </summary>
    public static Dictionary<string, double> EndToEndMetrics(IReadOnlyList<GraphDepthCase> cases,
        IReadOnlyList<(TurnResult? Turn, JudgeScore Score, bool JudgeFailed)> answers, List<EvalCaseFailure> failures)
    {
        var n = Math.Max(1, cases.Count);
        double faithfulness = 0, relevance = 0, mentions = 0, called = 0, judgeFailures = 0;
        foreach (var (c, (turn, score, judgeFailed)) in cases.Zip(answers))
        {
            var caseMentions = Mentions(c, turn);
            var usedGraph = turn?.ToolCalls.Any(t => t.ToolName is GraphTools.TraceCodeSymbol or GraphTools.ChangeImpact) == true;
            faithfulness += score.Faithfulness;
            relevance += score.Relevance;
            mentions += caseMentions;
            called += usedGraph ? 1 : 0;
            judgeFailures += judgeFailed ? 1 : 0;
            if (score.Faithfulness < GenerationSuite.PassMark || score.Relevance < 1 || !usedGraph)
            {
                failures.Add(new EvalCaseFailure(c.Id, $"e2e faithfulness={score.Faithfulness:0.##} relevance={score.Relevance:0.##} " +
                    $"mentions={caseMentions:0.##} graph={(usedGraph ? "called" : "not called")}: {score.Reason}"));
            }
        }
        return new Dictionary<string, double>
        {
            ["faithfulness"] = faithfulness / n,
            ["relevance"] = relevance / n,
            ["mentionRecall"] = mentions / n,
            ["graphToolCalled"] = called / n,
            ["judgeFailures"] = judgeFailures,
        };
    }

    /// <summary>Per-variant metrics over the turns that called the graph, the common cases, and why a case is not common.</summary>
    public sealed record GraphTurnResult(IReadOnlyDictionary<string, Dictionary<string, double>> Metrics, int CommonCases,
        IReadOnlyList<(string CaseId, string Variant)> NotCommon);

    /// <summary>
    /// The end-to-end scores where depth can matter (graph-depth-e2e-on-graph-turns): for each variant over its own turns
    /// that called a code graph tool (<c>:graph</c>, with <c>graphTurns</c>), and over the common cases — those whose
    /// turns called one in every variant — so the depths are compared on the same questions (<c>:common</c>, with
    /// <c>commonCases</c>, and mention recall by required depth). A score over an empty set is absent, never 0.
    /// </summary>
    public static GraphTurnResult GraphTurnMetrics(IReadOnlyList<GraphDepthCase> cases,
        IReadOnlyDictionary<string, IReadOnlyList<(TurnResult? Turn, JudgeScore Score, bool JudgeFailed)>> answersByVariant)
    {
        static bool CalledGraph(TurnResult? t) => t?.ToolCalls.Any(c => c.ToolName is GraphTools.TraceCodeSymbol or GraphTools.ChangeImpact) == true;
        var notCommon = new List<(string CaseId, string Variant)>();
        var common = new HashSet<int>();
        for (var i = 0; i < cases.Count; i++)
        {
            var missing = answersByVariant.Where(v => !CalledGraph(v.Value[i].Turn)).Select(v => v.Key).ToList();
            if (missing.Count == 0)
            {
                common.Add(i);
            }
            notCommon.AddRange(missing.Select(v => (cases[i].Id, v)));
        }
        var metrics = new Dictionary<string, Dictionary<string, double>>();
        foreach (var (variant, answers) in answersByVariant)
        {
            var m = new Dictionary<string, double>();
            var graph = Enumerable.Range(0, cases.Count).Where(i => CalledGraph(answers[i].Turn)).ToList();
            m["graphTurns"] = graph.Count;
            Scores(m, ":graph", graph, cases, answers);
            m["commonCases"] = common.Count;
            Scores(m, ":common", [.. common.Order()], cases, answers);
            foreach (var depth in common.Select(i => cases[i].RequiredDepth).Distinct().Order())
            {
                var atDepth = common.Where(i => cases[i].RequiredDepth == depth).ToList();
                m[$"mentionRecall:common@needs{depth}"] = atDepth.Average(i => Mentions(cases[i], answers[i].Turn));
            }
            metrics[variant] = m;
        }
        return new GraphTurnResult(metrics, common.Count, notCommon);
    }

    private static void Scores(Dictionary<string, double> m, string suffix, IReadOnlyList<int> rows, IReadOnlyList<GraphDepthCase> cases,
        IReadOnlyList<(TurnResult? Turn, JudgeScore Score, bool JudgeFailed)> answers)
    {
        if (rows.Count == 0)
        {
            return;
        }
        m[$"faithfulness{suffix}"] = rows.Average(i => answers[i].Score.Faithfulness);
        m[$"relevance{suffix}"] = rows.Average(i => answers[i].Score.Relevance);
        m[$"mentionRecall{suffix}"] = rows.Average(i => Mentions(cases[i], answers[i].Turn));
    }

    private static double Mentions(GraphDepthCase c, TurnResult? turn) =>
        (double)c.Needed.Count(x => (turn?.Answer ?? "").Contains(MentionKey(x.Item), StringComparison.OrdinalIgnoreCase)) / c.Needed.Count;

    /// <summary>The member of 'Type.Member' (a constructor's display is 'Type.Type'), or a test file's name without extension.</summary>
    public static string MentionKey(string item) =>
        item.Contains('/') ? Path.GetFileNameWithoutExtension(item) : item[(item.LastIndexOf('.') + 1)..];

    /// <summary>Nearest-rank percentile; 0 for no values.</summary>
    public static double Percentile(IEnumerable<double> values, double p)
    {
        var sorted = values.Order().ToList();
        return sorted.Count == 0 ? 0 : sorted[Math.Clamp((int)Math.Ceiling(p * sorted.Count) - 1, 0, sorted.Count - 1)];
    }

    private static string Describe(Dictionary<string, double> m) =>
        string.Join(" ", m.Where(x => x.Key is "recall" or "fullRecall" or "signalShare" or "meanNodes" or "meanTokens" or "truncatedRate" or "latencyP95Ms"
            || x.Key.StartsWith("recall@", StringComparison.Ordinal)).Select(x => $"{x.Key}={x.Value:0.###}"));
}

/// <summary>
/// The suite's one determinate bar (progress-feedback): every case of every variant and layer, labelled with the
/// variant, the layer and the case id — never the question, the answer or a tool's output.
/// </summary>
public sealed class GraphDepthProgress
{
    private readonly ConsoleProgress _bar;
    private readonly int _total;
    private int _done;

    public GraphDepthProgress(ConsoleProgress bar, int cases, int variants, int layers)
    {
        _bar = bar;
        _total = cases * variants * layers;
        _bar.SetTotal(_total);
    }

    public void Layer(GraphDepthVariant variant, string layer)
    {
        _bar.Step($"{variant.Name} {layer}");
    }

    public void Working(GraphDepthCase c) => _bar.Working(c.Id);

    public void Advance()
    {
        _done++;
        _bar.Advance();
    }

    public void Succeed() => _bar.Succeed($"{_done}/{_total} case runs");

    public void Fail(string reason) => _bar.Fail(reason);

    public void Cancel() => _bar.Cancel();
}
