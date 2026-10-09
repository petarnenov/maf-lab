using System.Net;
using System.Text.Json;
using Maf.Lab.Retrieval.Configuration;
using Maf.Lab.Plugins.Abstractions;
using Maf.Lab.Retrieval.Rerank;
using Maf.Lab.Retrieval.Store;
using Maf.Lab.TestSupport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Maf.Lab.Tests;

/// <summary>
/// The relevance judge: one Jev request per search, one Noul per candidate, the query and passages as data — and a
/// judge that cannot answer leaves the search as it was, saying why, without the text in any log.
/// </summary>
public class RelevanceJudgeTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ScoredChunk Chunk(int i, string text, string tenant = "firm-a") => new(new ChunkRecord
    {
        TenantId = tenant, DocId = $"{tenant}/docs/d{i}.md", ChunkId = $"{tenant}/docs/d{i}.md#s", SourceType = "docs",
        SourcePath = $"docs/d{i}.md", SectionPath = $"Section {i}", UpdatedAt = DateTimeOffset.UnixEpoch, ModelVersion = "m",
        Text = text, ContentHash = "h",
    }, 1.0 / (i + 1));

    private static readonly IReadOnlyList<ScoredChunk> Candidates =
    [
        Chunk(0, "Billing runs are scheduled monthly."),
        Chunk(1, "A missing fee schedule stops the billing run until one is assigned."),
        Chunk(2, "Households group accounts for breakpoints."),
    ];

    private sealed record Harness(DecisionRelevanceJudge Judge, FakeJev Jev, CapturingLoggerProvider Logs);

    private static Harness Build(FakeJev? jev = null, bool configured = true, RetrievalOptions? options = null, bool opensOnFirstFailure = false)
    {
        jev ??= new FakeJev();
        var logs = new CapturingLoggerProvider();
        var loggers = LoggerFactory.Create(b => b.AddProvider(logs).SetMinimumLevel(LogLevel.Trace));
        var engine = new FakeDecisionEngine(jev) { IsConfigured = configured, OpenAfterFailures = opensOnFirstFailure ? 1 : 0 };
        var judge = new DecisionRelevanceJudge(engine, Options.Create(options ?? new RetrievalOptions()), loggers.CreateLogger<DecisionRelevanceJudge>());
        return new Harness(judge, jev, logs);
    }

    [Fact]
    public async Task One_request_asks_one_question_per_candidate_with_the_text_as_data()
    {
        var h = Build();

        var judgement = await h.Judge.JudgeAsync("what happens when a fee schedule is missing", Candidates, Ct);

        var (authorization, body) = Assert.Single(h.Jev.Requests);
        Assert.Equal($"Bearer {FakeJev.TestKey}", authorization);
        var root = JsonDocument.Parse(body).RootElement;
        Assert.Equal("jev-1.13.0", root.GetProperty("model").GetString());
        Assert.Equal("what happens when a fee schedule is missing", root.GetProperty("state").GetProperty("query").GetString());
        var passages = root.GetProperty("state").GetProperty("passages");
        Assert.Equal(3, passages.GetArrayLength());
        Assert.Equal("Section 1", passages[1].GetProperty("section").GetString());
        var questions = root.GetProperty("questions");
        Assert.Equal(["p0", "p1", "p2"], questions.EnumerateObject().Select(q => q.Name));
        foreach (var q in questions.EnumerateObject())
        {
            Assert.Equal("noul", q.Value.GetProperty("type").GetString());
            var instructions = q.Value.GetProperty("instructions").GetString()!;
            Assert.Contains($"passages[{q.Name[1..]}]", instructions);
            // Neither the query nor any passage is ever part of an instruction.
            Assert.DoesNotContain("fee schedule", instructions);
            Assert.DoesNotContain("Billing runs", instructions);
        }
        Assert.DoesNotContain(FakeJev.TestKey, body);

        Assert.Equal([0.02, 0.9, 0.02], judgement.Scores);
        Assert.Equal(0.9, judgement.Max);
        Assert.Null(judgement.Reason);
        Assert.Equal("jev-1.13.0", judgement.Model);
    }

    [Fact]
    public async Task Only_the_configured_number_of_candidates_is_judged_and_passages_are_trimmed()
    {
        var h = Build(options: new RetrievalOptions { RelevanceCandidates = 2, RelevancePassageChars = 10 });

        var judgement = await h.Judge.JudgeAsync("fee schedule", Candidates, Ct);

        var passages = JsonDocument.Parse(Assert.Single(h.Jev.Requests).Body).RootElement.GetProperty("state").GetProperty("passages");
        Assert.Equal(2, passages.GetArrayLength());
        Assert.Equal("Billing ru", passages[0].GetProperty("text").GetString());
        Assert.Equal(2, judgement.Scores!.Count);
    }

    [Fact]
    public async Task No_candidates_means_no_request()
    {
        var h = Build();

        var judgement = await h.Judge.JudgeAsync("anything", [], Ct);

        Assert.Empty(h.Jev.Requests);
        Assert.Empty(judgement.Scores!);
        Assert.Null(judgement.Max);
    }

    [Fact]
    public async Task A_transport_that_hangs_gives_up_within_the_budget()
    {
        var h = Build(new FakeJev { Hang = TimeSpan.FromSeconds(30) }, options: new RetrievalOptions { RelevanceTimeoutSeconds = 0.2 });

        var started = DateTime.UtcNow;
        var judgement = await h.Judge.JudgeAsync("fee schedule", Candidates, Ct);

        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(5));
        Assert.Null(judgement.Scores);
        Assert.Equal("timed out after 0.2s", judgement.Reason);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, "rejected (503)")]
    [InlineData(HttpStatusCode.TooManyRequests, "rejected (429)")]
    public async Task A_rejected_request_leaves_no_scores_and_a_reason(HttpStatusCode status, string reason)
    {
        var h = Build(new FakeJev { Status = status });

        var judgement = await h.Judge.JudgeAsync("fee schedule", Candidates, Ct);

        Assert.Null(judgement.Scores);
        Assert.Equal(reason, judgement.Reason);
    }

    [Fact]
    public async Task With_the_circuit_open_nothing_is_sent_and_the_search_keeps_its_fused_order()
    {
        var h = Build(new FakeJev { Status = HttpStatusCode.ServiceUnavailable }, opensOnFirstFailure: true);
        await h.Judge.JudgeAsync("fee schedule", Candidates, Ct);
        var sent = h.Jev.Requests.Count;

        var judgement = await h.Judge.JudgeAsync("fee schedule", Candidates, Ct);
        var ranked = await new RelevanceReranker(h.Judge).RerankAsync("fee schedule", Candidates, Ct);

        Assert.Equal(sent, h.Jev.Requests.Count);
        Assert.Null(judgement.Scores);
        Assert.Equal("circuit open", judgement.Reason);
        Assert.Equal(0, judgement.DurationMs);
        Assert.Equal(Candidates, ranked);
    }

    [Fact]
    public async Task Without_a_key_nothing_is_sent()
    {
        var h = Build(configured: false);

        var judgement = await h.Judge.JudgeAsync("fee schedule", Candidates, Ct);

        Assert.Empty(h.Jev.Requests);
        Assert.Null(judgement.Scores);
        Assert.Equal("no key", judgement.Reason);
    }

    [Fact]
    public async Task A_timeout_of_zero_disables_the_judge()
    {
        var h = Build(options: new RetrievalOptions { RelevanceTimeoutSeconds = 0 });

        var judgement = await h.Judge.JudgeAsync("fee schedule", Candidates, Ct);

        Assert.Empty(h.Jev.Requests);
        Assert.Equal("disabled", judgement.Reason);
    }

    [Fact]
    public async Task Neither_the_query_nor_a_passage_nor_the_key_reaches_the_logs()
    {
        const string sentinel = "SENTINEL-QUERY-5c2a";
        var passages = new[] { Chunk(0, "SENTINEL-PASSAGE-91d0 text") };
        foreach (var jev in new[] { new FakeJev(), new FakeJev { Status = HttpStatusCode.ServiceUnavailable } })
        {
            var h = Build(jev);
            await h.Judge.JudgeAsync(sentinel, passages, Ct);
            Assert.DoesNotContain(h.Logs.Messages, m => m.Contains(sentinel) || m.Contains("SENTINEL-PASSAGE") || m.Contains(FakeJev.TestKey));
        }
    }

    [Fact]
    public async Task The_jev_reranker_orders_by_probability_and_keeps_fused_order_on_ties()
    {
        var h = Build(new FakeJev { Relevance = (_, text) => text.Contains("Households") ? 0.8 : text.Contains("missing") ? 0.8 : 0.1 });
        var reranker = new RelevanceReranker(h.Judge);

        var ranked = await reranker.RerankAsync("q", Candidates, Ct);

        // d1 and d2 tie at 0.8: the fused order (d1 before d2) decides; d0 falls behind them.
        Assert.Equal(["firm-a/docs/d1.md", "firm-a/docs/d2.md", "firm-a/docs/d0.md"], ranked.Select(r => r.Chunk.DocId));
        Assert.Equal([0.8, 0.8, 0.1], ranked.Select(r => r.Score));
        Assert.Equal("jev", reranker.Kind);
    }

    [Fact]
    public void Candidates_beyond_those_judged_follow_in_fused_order()
    {
        var ranked = RelevanceReranker.Order(Candidates, new RelevanceJudgement([0.1, 0.9], null, "jev-1.13.0", 1));

        Assert.Equal(["firm-a/docs/d1.md", "firm-a/docs/d0.md", "firm-a/docs/d2.md"], ranked.Select(r => r.Chunk.DocId));
    }

    [Fact]
    public async Task A_judge_that_did_not_answer_leaves_the_fused_order()
    {
        var h = Build(new FakeJev { Status = HttpStatusCode.ServiceUnavailable });

        var ranked = await new RelevanceReranker(h.Judge).RerankAsync("q", Candidates, Ct);

        Assert.Equal(Candidates, ranked);
    }
}

