using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Maf.Lab.Api.Agent.Decisions;
using Maf.Lab.Api.Agent;
using Maf.Lab.Api.BuiltIn;
using Maf.Lab.Eval;
using Maf.Lab.Eval.Judging;
using Maf.Lab.Eval.Suites;
using Maf.Lab.Plugins.Abstractions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Maf.Lab.Tests;

public class JevGradeTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly ReadItem FeeDoc = new("doc:fees›Missing", "fees › Missing: Assign the agreed schedule, then re-run.", [], BuiltInDomains.Billing);
    private static readonly ReadItem OtherDoc = new("doc:aum›Stale", "aum › Stale: A valuation older than three days is stale.", [], BuiltInDomains.Billing);
    private static readonly ReadItem CodeItem = new("code:src/A.cs:1-9", "src/A.cs:1-9 › A: class A {}", ["src/A.cs", "A.cs"], StandInDomains.CodeDomain.Id);

    private const string Answer = "Open the failed run. Assign the agreed schedule. Re-run it. Then pay the fee twice.";

    private static GradeInput Input(string answer = Answer, IReadOnlyList<ReadItem>? read = null, IReadOnlyList<string>? points = null) =>
        new("What do I do when a fee schedule is missing?", answer, read ?? [FeeDoc, OtherDoc], points ?? ["Assign the agreed schedule.", "Re-run the run."]);

    // ── the request (3.2) ─────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void One_question_per_sentence_point_and_source_plus_relevance_all_over_one_state()
    {
        var request = DecisionGradeRequest.Build(Input(), new JudgeOptions());

        Assert.Equal(4, request.State.AnswerSentences.Count);
        Assert.Equal(2, request.State.Sources.Count);
        Assert.Equal(1 + 4 * 2 + 2 * 2 + 2, request.Questions.Count);
        Assert.Contains(DecisionGradeRequest.RelevantId, request.Questions.Keys);
        Assert.Contains("claim_3", request.Questions.Keys);
        Assert.Contains("supported_3", request.Questions.Keys);
        Assert.Contains("stated_1", request.Questions.Keys);
        Assert.Contains("contradicts_1", request.Questions.Keys);
        Assert.Contains("on_subject_1", request.Questions.Keys);
        Assert.False(request.Codebase);
        Assert.False(request.Truncated);
    }

    [Fact]
    public void The_wire_shape_names_the_fields_and_every_question_is_a_noul_with_criteria()
    {
        var request = DecisionGradeRequest.Build(Input(), new JudgeOptions());
        var json = JsonNode.Parse(FakeDecisionEngine.Body("jev-1.13.0", request.State, request.Questions))!;

        var state = json["state"]!.AsObject();
        Assert.Equal(["user_question", "previous_question", "answer_sentences", "sources", "reference_points"], state.Select(p => p.Key));
        foreach (var (id, q) in json["questions"]!.AsObject())
        {
            Assert.Equal("noul", q!["type"]!.GetValue<string>());
            var question = q["instructions"]!["question"]!.GetValue<string>();
            Assert.Contains('`', question);
            // Never arithmetic or counting in Jev (§5): code counts the yeses.
            Assert.DoesNotMatch(@"(?i)\b(how many|count|number of|sum|percent|share)\b", question);
            Assert.NotNull(q["criteria"]!["true"]);
            Assert.NotNull(q["criteria"]!["false"]);
            // The question names a field by path; the text it asks about stays in the state.
            Assert.DoesNotContain("Assign the agreed schedule", question);
        }
    }

    [Fact]
    public void A_code_source_switches_to_the_code_context_and_criteria()
    {
        using var domains = StandInDomains.WithCodeDomain();
        var billing = DecisionGradeRequest.Build(Input(), new JudgeOptions());
        var code = DecisionGradeRequest.Build(Input("It is in `src/A.cs`.", [CodeItem]), new JudgeOptions());

        Assert.True(code.Codebase);
        var codeJson = FakeDecisionEngine.QuestionNode(code.Questions["supported_0"]).ToJsonString(FakeDecisionEngine.Json);
        var billingJson = FakeDecisionEngine.QuestionNode(billing.Questions["supported_0"]).ToJsonString(FakeDecisionEngine.Json);
        Assert.Contains("line range", codeJson);
        Assert.Contains("developer", codeJson);
        Assert.DoesNotContain("line range", billingJson);
    }

    [Fact]
    public void Sentences_and_sources_over_their_caps_are_left_out_and_the_case_says_so()
    {
        var answer = string.Join(" ", Enumerable.Range(1, 70).Select(i => $"Step {i} is done."));
        var request = DecisionGradeRequest.Build(Input(answer), new JudgeOptions { MaxSentences = 60, MaxSourceChars = FeeDoc.Text.Length });

        Assert.Equal(60, request.State.AnswerSentences.Count);
        Assert.Single(request.State.Sources);
        Assert.True(request.Truncated);
    }

    [Fact]
    public void At_sixty_sentences_the_request_stays_well_under_the_token_limit()
    {
        var answer = string.Join(" ", Enumerable.Range(1, 60).Select(i => $"Step {i} assigns the agreed fee schedule to the household and validates it."));
        var request = DecisionGradeRequest.Build(Input(answer, points: [.. Enumerable.Range(1, 8).Select(i => $"Point {i} holds.")]), new JudgeOptions());
        var chars = FakeDecisionEngine.Body("jev-1.13.0", request.State, request.Questions).Length;

        // jev-1.13: 64k tokens per request. Four characters a token is generous for English.
        Assert.True(chars / 4 < 48_000, $"{chars} characters");
    }

    // ── the metrics (3.3) ─────────────────────────────────────────────────────────────────────────────────────────────

    private static DecisionGrade Grade(Func<string, double> answer, GradeInput? input = null)
    {
        var request = DecisionGradeRequest.Build(input ?? Input(), new JudgeOptions());
        return DecisionGrade.From(request, request.Questions.Keys.ToDictionary(id => id, answer));
    }

    private static double Default(string id) => id.StartsWith("contradicts_", StringComparison.Ordinal) ? 0.02 : 0.95;

    [Fact]
    public void All_high_answers_score_one_and_add_nothing_to_the_band()
    {
        var grade = Grade(Default);

        Assert.Equal(1, grade.Faithfulness);
        Assert.Equal(1, grade.Relevance);
        Assert.Equal(1, grade.Completeness);
        Assert.Equal(1, grade.ReferenceAgreement);
        Assert.Equal(1, grade.RetrievalJudged);
        Assert.Equal(0, grade.Uncertain);
        Assert.Equal("", grade.Reason());
    }

    [Fact]
    public void One_unsupported_claim_of_four_is_three_quarters_and_is_quoted()
    {
        var grade = Grade(id => id == "supported_3" ? 0.1 : Default(id));

        Assert.Equal(0.75, grade.Faithfulness);
        Assert.Equal(["Then pay the fee twice."], grade.Unsupported);
        Assert.Contains("unsupported: Then pay the fee twice.", grade.Reason());
    }

    [Fact]
    public void An_answer_in_the_band_counts_as_no_and_as_uncertain()
    {
        var grade = Grade(id => id == "supported_1" ? 0.45 : Default(id));

        Assert.Equal(0.75, grade.Faithfulness);
        Assert.True(grade.Uncertain > 0);
    }

    [Fact]
    public void A_sentence_that_claims_nothing_is_not_counted()
    {
        var grade = Grade(id => id.StartsWith("claim_", StringComparison.Ordinal) ? 0.05 : id.StartsWith("supported_", StringComparison.Ordinal) ? 0.0 : Default(id));

        Assert.Equal(1, grade.Faithfulness);
        Assert.Empty(grade.Unsupported);
    }

    [Fact]
    public void A_missed_and_a_contradicted_point_lower_completeness_and_agreement()
    {
        var grade = Grade(id => id switch
        {
            "stated_1" => 0.1,
            "contradicts_0" => 0.9,
            _ => Default(id),
        });

        Assert.Equal(0.5, grade.Completeness);
        Assert.Equal(0.5, grade.ReferenceAgreement);
        Assert.Equal(["Re-run the run."], grade.Missed);
        Assert.Equal(["Assign the agreed schedule."], grade.Contradicted);
        Assert.Equal([true, false], grade.Stated);
        Assert.Equal([true, false], grade.PointContradicted);
    }

    [Fact]
    public void An_off_subject_source_lowers_judged_retrieval_and_no_source_reads_as_one()
    {
        Assert.Equal(0.5, Grade(id => id == "on_subject_1" ? 0.1 : Default(id)).RetrievalJudged);
        Assert.Equal(1, Grade(Default, Input(read: [])).RetrievalJudged);
    }

    [Fact]
    public void No_reference_points_leave_completeness_and_agreement_unset()
    {
        var grade = Grade(Default, Input(points: []));

        Assert.Null(grade.Completeness);
        Assert.Null(grade.ReferenceAgreement);
    }

    [Fact]
    public void An_unanswered_question_reads_as_no()
    {
        var request = DecisionGradeRequest.Build(Input(), new JudgeOptions());
        var grade = DecisionGrade.From(request, new Dictionary<string, double> { ["claim_0"] = 0.9 });

        Assert.Equal(0, grade.Faithfulness);
        Assert.Equal(0, grade.Relevance);
    }

    // ── the client, the fallback and the logs (3.4) ───────────────────────────────────────────────────────────────────

    private static (DecisionGrader Grader, FakeJev Jev, ListLogger Log) Grader(FakeJev? jev = null, bool key = true, JudgeOptions? options = null)
    {
        jev ??= new FakeJev();
        var log = new ListLogger();
        var engine = new FakeDecisionEngine(jev) { IsConfigured = key };
        return (new DecisionGrader(engine, options ?? new JudgeOptions(), log), jev, log);
    }

    [Fact]
    public async Task One_request_grades_the_answer_and_counts_its_tokens()
    {
        var (grader, jev, _) = Grader(new FakeJev { Grade = (id, _) => id == "supported_3" ? 0.1 : null });

        var outcome = await grader.GradeAsync(Input(), Ct);

        Assert.Single(jev.Requests);
        Assert.Null(outcome.Failure);
        Assert.Equal(0.75, outcome.Grade!.Faithfulness);
        Assert.Equal(400, outcome.InputTokens);
        Assert.Equal("jev-1.13.0", outcome.Model);
    }

    [Fact]
    public async Task Without_a_key_nothing_is_sent_and_the_reason_is_no_key()
    {
        var (grader, jev, _) = Grader(key: false);

        var outcome = await grader.GradeAsync(Input(), Ct);

        Assert.Empty(jev.Requests);
        Assert.Null(outcome.Grade);
        Assert.Equal("no key", outcome.Failure);
        Assert.False(grader.IsConfigured);
    }

    [Fact]
    public async Task An_error_status_is_a_failure_with_its_reason()
    {
        var (grader, _, _) = Grader(new FakeJev { Status = HttpStatusCode.UnprocessableEntity });

        var outcome = await grader.GradeAsync(Input(), Ct);

        Assert.Null(outcome.Grade);
        Assert.Equal("rejected (422)", outcome.Failure);
    }

    [Fact]
    public async Task A_timeout_is_a_failure_with_its_reason()
    {
        var (grader, _, _) = Grader(new FakeJev { Hang = TimeSpan.FromSeconds(2) }, options: new JudgeOptions { TimeoutSeconds = 0.2 });

        var outcome = await grader.GradeAsync(Input(), Ct);

        Assert.Null(outcome.Grade);
        Assert.StartsWith("timed out", outcome.Failure);
    }

    [Fact]
    public async Task A_partial_response_is_a_failure_not_a_grade()
    {
        var (grader, _, _) = Grader(new FakeJev { Grade = (id, _) => id == "on_subject_1" ? double.NaN : null });

        var outcome = await grader.GradeAsync(Input(), Ct);

        Assert.Null(outcome.Grade);
        Assert.StartsWith("incomplete answer (1 of", outcome.Failure);
    }

    [Fact]
    public async Task The_logs_carry_numbers_and_never_a_sentence_a_point_or_a_source()
    {
        var (grader, _, log) = Grader();

        await grader.GradeAsync(Input(), Ct);

        var line = Assert.Single(log.Lines);
        Assert.Contains("model=jev-1.13.0", line);
        Assert.Contains("inputTokens=400", line);
        foreach (var text in new[] { "Open the failed run", "Re-run the run", "Assign the agreed schedule", "valuation", "fee schedule is missing" })
        {
            Assert.DoesNotContain(text, line);
        }
    }

    // ── the evaluator (3.5) ───────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_evaluator_reports_one_metric_each_with_the_sentences_as_diagnostics_and_leaves_the_outcome()
    {
        var (grader, _, _) = Grader(new FakeJev { Grade = (id, _) => id switch { "supported_3" => 0.1, "stated_1" => 0.1, _ => null } });
        var evaluator = new DecisionGenerationEvaluator(grader);
        var context = new DecisionGradeContext([FeeDoc, OtherDoc], ["Assign the agreed schedule.", "Re-run the run."]);

        var result = await evaluator.EvaluateAsync([new ChatMessage(ChatRole.User, "What do I do when a fee schedule is missing?")],
            new ChatResponse(new ChatMessage(ChatRole.Assistant, Answer)), additionalContext: [context], cancellationToken: Ct);

        Assert.Equal(evaluator.EvaluationMetricNames.Order(), result.Metrics.Keys.Order());
        var faithfulness = result.Get<NumericMetric>(DecisionGenerationEvaluator.Faithfulness);
        Assert.Equal(0.75, faithfulness.Value);
        Assert.True(faithfulness.Interpretation!.Failed is false);
        Assert.Contains(faithfulness.Diagnostics!, d => d.Message == "unsupported: Then pay the fee twice.");
        Assert.Contains("\"supported_3\":0.1", faithfulness.Metadata!["answers"]);
        var completeness = result.Get<NumericMetric>(DecisionGenerationEvaluator.Completeness);
        Assert.Equal(0.5, completeness.Value);
        Assert.True(completeness.Interpretation!.Failed);
        Assert.Equal("400", completeness.Metadata!["inputTokens"]);
        Assert.Equal(0.75, context.Outcome!.Grade!.Faithfulness);
    }

    [Fact]
    public void A_failed_grade_scores_zero_on_every_judged_metric_and_says_why()
    {
        var result = DecisionGenerationEvaluator.ToResult(new GradeOutcome(null, "timed out after 10s", "jev-1.13.0", 10_000, 15, 0, false, false));

        foreach (var name in new[] { "faithfulness", "relevance", "completeness", "referenceAgreement", "retrievalJudged" })
        {
            var metric = result.Get<NumericMetric>(name);
            Assert.Equal(0, metric.Value);
            Assert.True(metric.Interpretation!.Failed);
            Assert.Equal("judge failed: timed out after 10s", metric.Reason);
        }
        Assert.False(result.Metrics.ContainsKey("judgeUncertain"));
    }

    [Fact]
    public void A_case_without_reference_points_reports_no_completeness()
    {
        var request = DecisionGradeRequest.Build(Input(points: []), new JudgeOptions());
        var grade = DecisionGrade.From(request, request.Questions.Keys.ToDictionary(id => id, Default));

        var result = DecisionGenerationEvaluator.ToResult(new GradeOutcome(grade, null, "jev-1.13.0", 300, request.Questions.Count, 400, false, false));

        Assert.False(result.Metrics.ContainsKey("completeness"));
        Assert.False(result.Metrics.ContainsKey("referenceAgreement"));
        Assert.Equal(1, result.Get<NumericMetric>("faithfulness").Value);
    }

    private sealed class ListLogger : ILogger<DecisionGrader>
    {
        public List<string> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Lines.Add(formatter(state, exception));
    }
}
