using Maf.Lab.Api.Agent.Jev;
using Maf.Lab.Domain.Evals;
using Maf.Lab.Domain.Graph;
using Maf.Lab.Eval;
using Maf.Lab.Eval.Datasets;
using Maf.Lab.Eval.Suites;

namespace Maf.Lab.Tests;

/// <summary>The code-route suite (route-structural-code-questions): its dataset and what it counts.</summary>
public class CodeRouteEvalTests
{
    private static string WriteDataset(params string[] rows)
    {
        var dir = Directory.CreateTempSubdirectory("maf-code-route-").FullName;
        File.WriteAllLines(Path.Combine(dir, "code-route.jsonl"), rows);
        return dir;
    }

    private const string Row =
        """{"id":"r1","question":"Who calls A.B?","expected":"callers","hasArgument":true,"language":"en","split":"design"}""";

    [Fact]
    public void A_row_carries_its_expected_option_argument_language_and_split()
    {
        var c = Assert.Single(DatasetLoader.CodeRoute(WriteDataset(Row)));
        Assert.Equal(("callers", true, "en", "design"), (c.Expected, c.HasArgument, c.Language, c.Split));
    }

    [Theory]
    [InlineData("""{"id":"r2","question":"q","expected":"maybe","hasArgument":true,"language":"en","split":"design"}""", "unknown expected option")]
    [InlineData("""{"id":"r2","question":"q","expected":"text","language":"en","split":"design"}""", "'hasArgument' must be true or false")]
    [InlineData("""{"id":"r2","question":"q","expected":"text","hasArgument":false,"language":"de","split":"design"}""", "language")]
    [InlineData("""{"id":"r2","question":"q","expected":"text","hasArgument":false,"language":"en","split":"later"}""", "split")]
    public void A_mislabelled_row_fails_and_is_named(string row, string message)
    {
        var ex = Assert.Throws<InvalidDataException>(() => DatasetLoader.CodeRoute(WriteDataset(Row, row)));
        Assert.Contains("code-route.jsonl:2", ex.Message);
        Assert.Contains(message, ex.Message);
    }

    [Fact]
    public void The_dataset_spans_every_option_language_and_split_with_rows_that_name_no_argument()
    {
        var cases = DatasetLoader.CodeRoute(Path.Combine(CorpusLoaderTests.RepoRoot(), "evals"));

        foreach (var option in DatasetLoader.CodeRouteOptions)
        {
            Assert.Contains(cases, c => c.Expected == option);
        }
        foreach (var language in DatasetLoader.IntentLanguages)
        {
            Assert.Contains(cases, c => c.Language == language && c.Split == "design");
            Assert.Contains(cases, c => c.Language == language && c.Split == "holdout");
        }
        Assert.True(cases.Count(c => c.Expected is "callers" or "callees" or "impact" && !c.HasArgument) >= 6);
        // Every argument a structural row claims is one the router's patterns take, and nothing else.
        foreach (var c in cases.Where(c => c.Expected is "callers" or "callees" or "impact"))
        {
            var found = c.Expected == "impact" ? CodeToolRouter.Paths(c.Question).Count : CodeToolRouter.Symbols(c.Question).Count;
            Assert.True((found == 1) == c.HasArgument, c.Id);
        }
    }

    private static CodeRouteCase Case(string id, string expected, bool hasArgument = true, string language = "en", string split = "design") =>
        new(id, "q", expected, hasArgument, language, split);

    private static ToolRoute Trace(string direction) => new(GraphTools.TraceCodeSymbol, new Dictionary<string, object?> { ["symbol"] = "A.B", ["direction"] = direction }, 0.9);

    [Fact]
    public void Scores_count_what_code_would_route_and_leave_failed_requests_out()
    {
        CodeRouteOutcome[] outcomes =
        [
            new(Case("ok", "callers"), "callers", 0.9, Trace("callers"), null, false),
            new(Case("wrong-direction", "callers", language: "bg"), "callees", 0.9, Trace("callees"), null, false),
            new(Case("impact", "impact", split: "holdout"), "impact", 0.8,
                new(GraphTools.ChangeImpact, new Dictionary<string, object?> { ["path"] = "src/X.cs" }, 0.8), null, false),
            new(Case("text-kept", "text", hasArgument: false), "text", 0.9, null, "needs text, not the graph", false),
            new(Case("text-routed", "text"), "callers", 0.7, Trace("callers"), null, false),
            new(Case("no-arg", "callers", hasArgument: false), "callers", 0.9, null, "no Type.Member symbol in the question", false),
            new(Case("timeout", "callers"), null, null, null, "timed out after 2s", true),
        ];
        var failures = new List<EvalCaseFailure>();

        var m = CodeRouteSuite.Score(outcomes, failures);

        Assert.Equal(4.0 / 6, m["accuracy"], 6);
        Assert.Equal(2.0 / 3, m["structuralRecall"], 6);
        Assert.Equal(0.5, m["textKept"]);
        Assert.Equal(1.0, m["noArgumentKept"]);
        Assert.Equal(0.0, m["structuralRecall:bg"]);
        Assert.Equal(1.0, m["structuralRecall:holdout"]);
        Assert.False(m.ContainsKey("failed"));
        Assert.Equal(["wrong-direction", "text-routed", "timeout"], failures.Select(f => f.CaseId));
        Assert.StartsWith("failed:", failures[^1].Reason);
    }

    [Fact]
    public void Code_route_is_a_gated_suite_in_all() =>
        Assert.Contains(CodeRouteSuite.Name, Program.SuitesOf("all"));
}
