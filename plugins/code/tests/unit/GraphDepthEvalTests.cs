using Maf.Lab.Api.Agent;
using Maf.Lab.Domain.Chat;
using Maf.Lab.Domain.Evals;
using Maf.Lab.Domain.Feedback;
using Maf.Lab.Domain.Graph;
using Maf.Lab.Eval;
using Maf.Lab.Eval.Datasets;
using Maf.Lab.Eval.Suites;
using Maf.Lab.Hosting.Cli;
using Maf.Lab.Retrieval.Jev;
using Microsoft.Extensions.Configuration;

namespace Maf.Lab.Tests;

/// <summary>The graph-depth comparison (add-graph-depth-eval): its dataset, its metrics, and how the harness runs it.</summary>
public class GraphDepthEvalTests : IDisposable
{
    // The three-domain view these tests were written in: billing and portfolio built in, codebase from this plugin.
    private readonly IDisposable _domains = CodePluginSupport.Use();

    public void Dispose() => _domains.Dispose();

    private static string WriteDataset(params string[] rows)
    {
        var dir = Directory.CreateTempSubdirectory("maf-graph-depth-").FullName;
        File.WriteAllLines(Path.Combine(dir, "graph-depth.jsonl"), rows);
        return dir;
    }

    [Fact]
    public void The_dataset_spans_every_required_depth_both_kinds_and_bulgarian()
    {
        var cases = DatasetLoader.GraphDepth(Path.Combine(CorpusLoaderTests.RepoRoot(), "evals"));

        // At least four cases per depth, or a variant's recall@needsK rests on one or two questions.
        for (var depth = 1; depth <= 4; depth++)
        {
            Assert.True(cases.Count(c => c.RequiredDepth == depth) >= 4, $"fewer than four cases need {depth} call(s)");
        }
        Assert.Contains(cases, c => c.Kind == "trace" && c.Direction == "callers");
        Assert.Contains(cases, c => c.Kind == "trace" && c.Direction == "callees");
        Assert.Contains(cases, c => c.Kind == "impact");
        Assert.True(cases.Count(c => c.Language == "bg") * 4 >= cases.Count, "a quarter of the questions are Bulgarian");
        // An item the graph cannot reach is kept and reported: search_codebase reaches Qdrant only through an interface.
        Assert.Contains(cases, c => c.Needed.Any(n => n.Hops is null));
    }

    private const string TraceRow =
        """{"id":"t1","kind":"trace","symbol":"A.B","direction":"callers","question":"Who calls A.B?","language":"en","needed":[{"item":"C.D","hops":1},{"item":"E.F","hops":3},{"item":"G.H","hops":null}],"reference":"C.D calls it; E.F through two more."}""";

    [Fact]
    public void A_trace_row_carries_its_symbol_direction_and_needed_items_with_their_hops()
    {
        var c = Assert.Single(DatasetLoader.GraphDepth(WriteDataset(TraceRow)));

        Assert.Equal(("trace", "A.B", "callers", null), (c.Kind, c.Symbol, c.Direction, c.Path));
        Assert.Equal([new GraphDepthNeed("C.D", 1), new GraphDepthNeed("E.F", 3), new GraphDepthNeed("G.H", null)], c.Needed);
        Assert.Equal(3, c.RequiredDepth);
        Assert.Equal("firm-a", c.TenantId);
    }

    [Fact]
    public void An_impact_row_carries_its_file_and_needed_test_files()
    {
        var c = Assert.Single(DatasetLoader.GraphDepth(WriteDataset(
            """{"id":"i1","kind":"impact","path":"src/X.cs","question":"Which tests cover src/X.cs?","language":"bg","needed":[{"item":"tests/XTests.cs","hops":2}],"reference":"XTests."}""")));

        Assert.Equal(("impact", null, "src/X.cs", 2), (c.Kind, c.Symbol, c.Path, c.RequiredDepth));
    }

    [Theory]
    [InlineData("""{"id":"e1","kind":"trace","symbol":"A.B","direction":"callers","question":"q","language":"en","needed":[],"reference":"r"}""", "at least one item")]
    [InlineData("""{"id":"e2","kind":"trace","symbol":"A.B","direction":"callers","question":"q","language":"en","reference":"r"}""", "at least one item")]
    [InlineData("""{"id":"e3","kind":"trace","symbol":"A.B","direction":"callers","question":"q","language":"en","needed":[{"item":"C.D","hops":null}],"reference":"r"}""", "reachable")]
    [InlineData("""{"id":"e4","kind":"trace","symbol":"A.B","direction":"callers","question":"q","language":"en","needed":[{"item":"C.D","hops":5}],"reference":"r"}""", "'hops' must be 1 to 4")]
    [InlineData("""{"id":"e5","kind":"trace","symbol":"A.B","direction":"up","question":"q","language":"en","needed":[{"item":"C.D","hops":1}],"reference":"r"}""", "direction")]
    public void A_row_that_measures_nothing_or_is_mislabelled_fails_and_is_named(string row, string message)
    {
        var ex = Assert.Throws<InvalidDataException>(() => DatasetLoader.GraphDepth(WriteDataset(TraceRow, row)));
        Assert.Contains("graph-depth.jsonl:2", ex.Message);
        Assert.Contains(message, ex.Message);
    }

    private static GraphDepthCase Case(string id, params (string Item, int? Hops)[] needed) =>
        new(id, "trace", "A.B", "callers", null, $"question {id}", "en", [.. needed.Select(n => new GraphDepthNeed(n.Item, n.Hops))], "ref", "firm-a");

    private static GraphDepthProbe Probe(int tokens, double latency, bool truncated = false, params string[] items) =>
        new(items, items.Length, truncated, tokens, latency);

    [Fact]
    public void Structural_metrics_count_what_was_reached_and_what_it_cost()
    {
        GraphDepthCase[] cases =
        [
            Case("one", ("C.D", 1)),
            Case("three", ("C.D", 1), ("E.F", 3)),
            Case("beyond", ("C.D", 2), ("G.H", null)),
        ];
        // Depth 2: the 3-call item is not reached; the beyond-4 item never is.
        GraphDepthProbe[] probes =
        [
            Probe(100, 10, false, "C.D", "X.Y"),
            Probe(200, 20, true, "C.D", "X.Y", "Z.W"),
            Probe(300, 30, false, "C.D"),
        ];
        var failures = new List<EvalCaseFailure>();

        var m = GraphDepthSuite.StructuralMetrics(cases, probes, failures);

        Assert.Equal((1 + 0.5 + 0.5) / 3, m["recall"], 6);
        Assert.Equal(1.0 / 3, m["fullRecall"], 6);
        Assert.Equal(1.0, m["recall@needs1"]);
        Assert.Equal(0.5, m["recall@needs2"]);
        Assert.Equal(0.5, m["recall@needs3"]);
        Assert.False(m.ContainsKey("recall@needs4"));
        Assert.Equal(3.0 / 6, m["signalShare"], 6);
        Assert.Equal(2, m["meanNodes"]);
        Assert.Equal(200, m["meanTokens"]);
        Assert.Equal(1.0 / 3, m["truncatedRate"], 6);
        Assert.Equal(20, m["latencyP50Ms"]);
        Assert.Equal(30, m["latencyP95Ms"]);
        Assert.Equal(["three", "beyond"], failures.Select(f => f.CaseId));
        Assert.Contains("E.F (3 calls)", failures[0].Reason);
        Assert.Contains("G.H (beyond 4)", failures[1].Reason);
    }

    [Fact]
    public void A_case_that_needs_three_calls_is_fully_reached_only_from_depth_3()
    {
        GraphDepthCase[] cases = [Case("three", ("C.D", 1), ("E.F", 3))];

        var depth2 = GraphDepthSuite.StructuralMetrics(cases, [Probe(1, 1, false, "C.D")], []);
        var depth3 = GraphDepthSuite.StructuralMetrics(cases, [Probe(1, 1, false, "C.D", "E.F")], []);

        Assert.Equal((0.0, 0.5), (depth2["fullRecall"], depth2["recall@needs3"]));
        Assert.Equal((1.0, 1.0), (depth3["fullRecall"], depth3["recall@needs3"]));
    }

    private static TurnResult Turn(string answer, params string[] tools) =>
        new("c", "t", Intent.Procedural, false, answer, [.. tools.Select(t => new ToolCallRecord(t, "", "ok", 0, [], []))], [], [], null);

    [Fact]
    public void End_to_end_metrics_score_every_turn_and_count_whether_the_graph_was_used()
    {
        GraphDepthCase[] cases = [Case("used", ("Svc.RankCoreAsync", 1), ("Tool.SearchAsync", 2)), Case("skipped", ("Svc.RankCoreAsync", 1))];
        (TurnResult?, JudgeScore, bool)[] answers =
        [
            (Turn("It is called by rankcoreasync.", GraphTools.TraceCodeSymbol), new JudgeScore(1, 1, "ok"), false),
            (Turn("No idea.", "search_codebase"), new JudgeScore(0, 0, "judge failed: TimeoutException"), true),
        ];
        var failures = new List<EvalCaseFailure>();

        var m = GraphDepthSuite.EndToEndMetrics(cases, answers, failures);

        Assert.Equal(0.5, m["faithfulness"]);
        Assert.Equal(0.25, m["mentionRecall"]);
        Assert.Equal(0.5, m["graphToolCalled"]);
        Assert.Equal(1, m["judgeFailures"]);
        var skipped = Assert.Single(failures);
        Assert.Equal("skipped", skipped.CaseId);
        Assert.Contains("graph=not called", skipped.Reason);
    }

    [Theory]
    [InlineData("DocumentSearchService.RankCoreAsync", "RankCoreAsync")]
    [InlineData("TenantScopedSearch.TenantScopedSearch", "TenantScopedSearch")]
    [InlineData("tests/Maf.Lab.IntegrationTests/TenancyAcceptanceTests.cs", "TenancyAcceptanceTests")]
    public void An_answer_names_an_item_by_its_member_or_its_test_file(string item, string key) =>
        Assert.Equal(key, GraphDepthSuite.MentionKey(item));

    [Fact]
    public void A_graded_answer_that_does_not_address_the_question_fails_the_case_even_when_faithful()
    {
        GraphDepthCase[] cases = [Case("off", ("Svc.RankCoreAsync", 1))];
        (TurnResult?, JudgeScore, bool)[] answers = [(Turn("It is fast.", GraphTools.TraceCodeSymbol), new JudgeScore(1, 0, "does not address the question"), false)];
        var failures = new List<EvalCaseFailure>();

        GraphDepthSuite.EndToEndMetrics(cases, answers, failures);

        Assert.Contains("relevance=0", Assert.Single(failures).Reason);
    }

    [Fact]
    public async Task The_end_to_end_layer_refuses_to_run_without_the_jev_grade()
    {
        var suite = new GraphDepthSuite(new ConfigurationBuilder().Build(), grader: null);
        var ctx = new SuiteContext(Path.Combine(CorpusLoaderTests.RepoRoot(), "evals"), new EvalOptions(), 1, _ => { });

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => suite.RunAsync(ctx, structuralOnly: false, TestContext.Current.CancellationToken));

        Assert.Contains(JevCredential.EnvironmentVariable, error.Message);
        Assert.Contains("--structural-only", error.Message);
    }

    [Fact]
    public void The_progress_bar_counts_every_case_of_every_variant_and_layer_and_names_ids_only()
    {
        var output = new StringWriter();
        var bar = new ConsoleProgress("graph-depth", output, interactive: false, heartbeat: false);
        var progress = new GraphDepthProgress(bar, cases: 2, variants: 3, layers: 2);
        var c = Case("trace-qa-callers", ("C.D", 1));

        progress.Layer(GraphDepthSuite.Variants[0], "structural");
        progress.Working(c);
        var working = bar.Render();
        progress.Advance();
        var line = bar.Render();
        progress.Succeed();

        Assert.Contains("0/12 0%", working);
        Assert.Contains("depth-2 structural · trace-qa-callers", working);
        Assert.Contains("1/12 8%", line);
        var text = output.ToString();
        Assert.DoesNotContain(c.Question, working + line + text);
        Assert.DoesNotContain("\u001b", text);
        Assert.Contains("1/12 case runs", text.TrimEnd().Split('\n')[^1]);
    }

    [Fact]
    public void A_cancelled_run_ends_its_bar_saying_so()
    {
        var output = new StringWriter();
        var progress = new GraphDepthProgress(new ConsoleProgress("graph-depth", output, interactive: false, heartbeat: false), 1, 3, 1);
        progress.Cancel();
        Assert.Contains("cancelled", output.ToString().TrimEnd().Split('\n')[^1]);
    }

    [Fact]
    public void All_suites_do_not_include_the_graph_depth_comparison()
    {
        Assert.DoesNotContain(GraphDepthSuite.Name, Program.SuitesOf("all"));
        Assert.Equal([GraphDepthSuite.Name], Program.SuitesOf("graph-depth"));
    }

    private static EvalVariantResult Variant(double recall) =>
        new("depth-3", new Dictionary<string, double> { ["recall"] = recall }, new Dictionary<string, double>(), true, 1, []);

    [Fact]
    public void A_comparison_suite_is_never_accepted_into_the_baseline()
    {
        var accepted = Program.AcceptInto(EvalBaseline.Empty, GraphDepthSuite.Name, [Variant(0.9)], "run-1", DateTimeOffset.UnixEpoch);
        Assert.Same(EvalBaseline.Empty, accepted);
        Assert.False(accepted!.Suites.ContainsKey(GraphDepthSuite.Name));
    }

    [Fact]
    public void A_worse_comparison_run_is_not_a_regression()
    {
        // Even with a graph-depth entry somebody wrote by hand, a lower number is reported, never gated.
        var baseline = new EvalBaseline(new Dictionary<string, SuiteBaseline>
        {
            [GraphDepthSuite.Name] = new(new Dictionary<string, IReadOnlyDictionary<string, double>> { ["depth-3"] = new Dictionary<string, double> { ["recall"] = 0.9 } },
                "run-0", DateTimeOffset.UnixEpoch),
        });

        Assert.Empty(Program.CompareWithBaseline(baseline, GraphDepthSuite.Name, [Variant(0.1)], new EvalOptions()));
        Assert.NotEmpty(Program.CompareWithBaseline(baseline with { }, "selection", [Variant(0.1)], new EvalOptions()));
    }

    private static (TurnResult?, JudgeScore, bool) Answer(string answer, double faithfulness, bool graph) =>
        (Turn(answer, graph ? [GraphTools.TraceCodeSymbol] : ["search_codebase"]), new JudgeScore(faithfulness, faithfulness, "r"), false);

    [Fact]
    public void Graph_turn_scores_cover_each_variants_own_graph_turns_and_the_cases_common_to_all()
    {
        GraphDepthCase[] cases = [Case("a", ("X.Alpha", 1)), Case("b", ("Y.Beta", 3)), Case("c", ("Z.Gamma", 2))];
        var answers = new Dictionary<string, IReadOnlyList<(TurnResult?, JudgeScore, bool)>>
        {
            // "a" calls the graph everywhere; "b" misses it in depth-3; "c" never does.
            ["depth-2"] = [Answer("alpha", 1, true), Answer("beta", 0.5, true), Answer("-", 0, false)],
            ["depth-3"] = [Answer("alpha", 0.5, true), Answer("-", 0, false), Answer("-", 0, false)],
            ["depth-4"] = [Answer("-", 1, true), Answer("beta", 1, true), Answer("gamma", 1, false)],
        };

        var r = GraphDepthSuite.GraphTurnMetrics(cases, answers);

        Assert.Equal(1, r.CommonCases);
        Assert.Equal(2, r.Metrics["depth-2"]["graphTurns"]);
        Assert.Equal(0.75, r.Metrics["depth-2"]["faithfulness:graph"]);
        Assert.Equal(1.0, r.Metrics["depth-2"]["mentionRecall:graph"]);
        Assert.Equal(1.0, r.Metrics["depth-2"]["faithfulness:common"]);
        Assert.Equal(0.5, r.Metrics["depth-3"]["faithfulness:common"]);
        Assert.Equal(0.0, r.Metrics["depth-4"]["mentionRecall:common"]);
        Assert.Equal(1.0, r.Metrics["depth-2"]["mentionRecall:common@needs1"]);
        Assert.False(r.Metrics["depth-2"].ContainsKey("mentionRecall:common@needs3"));
        Assert.Contains(("b", "depth-3"), r.NotCommon);
        Assert.Equal(3, r.NotCommon.Count(n => n.CaseId == "c"));
        // graphTurns reconciles with graphToolCalled.
        Assert.Equal(GraphDepthSuite.EndToEndMetrics(cases, answers["depth-2"], [])["graphToolCalled"] * cases.Length, r.Metrics["depth-2"]["graphTurns"], 6);
    }

    [Fact]
    public void With_no_common_case_the_common_scores_are_absent_not_zero()
    {
        GraphDepthCase[] cases = [Case("a", ("X.Alpha", 1))];
        var answers = new Dictionary<string, IReadOnlyList<(TurnResult?, JudgeScore, bool)>>
        {
            ["depth-2"] = [Answer("alpha", 1, true)],
            ["depth-3"] = [Answer("alpha", 1, false)],
        };

        var r = GraphDepthSuite.GraphTurnMetrics(cases, answers);

        Assert.Equal(0, r.CommonCases);
        Assert.Equal(0, r.Metrics["depth-3"]["commonCases"]);
        Assert.DoesNotContain(r.Metrics["depth-2"].Keys, k => k.EndsWith(":common", StringComparison.Ordinal) || k.Contains(":common@", StringComparison.Ordinal));
        Assert.False(r.Metrics["depth-3"].ContainsKey("faithfulness:graph"));
    }
}

