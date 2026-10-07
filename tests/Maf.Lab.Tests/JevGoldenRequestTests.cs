using System.Text.Json;
using Maf.Lab.Api.Agent;
using Maf.Lab.Api.Agent.Jev;
using Maf.Lab.Api.BuiltIn;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Eval;
using Maf.Lab.Eval.Judging;
using Maf.Lab.Retrieval.Configuration;
using Maf.Lab.Retrieval.Jev;
using Maf.Lab.Retrieval.Rerank;
using Maf.Lab.Retrieval.Store;
using Maf.Lab.TestSupport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Maf.Lab.Tests;

/// <summary>
/// Golden files (introduce-provider-plugins, Q8): the exact body every Jev call site sends to <c>POST /v1/systemone</c>,
/// recorded once from main before the decision engine moved behind <c>IDecisionEngine</c>. A refactor of the transport
/// must leave every body byte-identical, so what Jev reads — its questions, its state, the pinned model — and therefore
/// every measured threshold stay as they are. The files are in <c>tests/Maf.Lab.Tests/Golden/jev/</c>; set
/// <c>MAF_UPDATE_GOLDEN=1</c> to rewrite them, which is a change to what Jev reads and needs its evals.
/// </summary>
public class JevGoldenRequestTests
{
    private const string Procedural = "what is the procedure when a fee schedule is missing";
    private const string Answer = "Assign the missing fee schedule and re-run, per Procedure: Missing fee schedule.";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string GoldenDir => Path.Combine(CorpusLoaderTests.RepoRoot(), "tests", "Maf.Lab.Tests", "Golden", "jev");

    private static void AssertGolden(string name, string body)
    {
        var path = Path.Combine(GoldenDir, name + ".json");
        if (Environment.GetEnvironmentVariable("MAF_UPDATE_GOLDEN") == "1")
        {
            Directory.CreateDirectory(GoldenDir);
            File.WriteAllText(path, body);
        }
        Assert.True(File.Exists(path), $"no golden file {name}.json: record it with MAF_UPDATE_GOLDEN=1");
        Assert.Equal(File.ReadAllText(path), body);
    }

    private static JsonElement Questions(string body) => JsonDocument.Parse(body).RootElement.GetProperty("questions");

    private static bool Asks(string body, string id) => Questions(body).TryGetProperty(id, out _);

    [Fact]
    public async Task A_turn_sends_the_recorded_intent_screening_and_answer_check_requests()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel(Answer));

        await ApiFactory.ChatAsync(api.ClientFor("adam", "firm-a", Role.USER), Procedural);

        var bodies = api.Jev.Requests.Select(r => r.Body).ToList();
        AssertGolden("turn-intent", Assert.Single(bodies, b => Asks(b, "intent")));
        AssertGolden("turn-answer-check", Assert.Single(bodies, b => JsonDocument.Parse(b).RootElement.GetProperty("state").TryGetProperty("answer", out _)));
        // Content screening runs concurrently, one request per tool result: compared in a fixed order.
        var screens = bodies.Where(b => !Asks(b, "intent") && Questions(b).EnumerateObject().All(q => q.Name.StartsWith("guard_", StringComparison.Ordinal)))
            .Order(StringComparer.Ordinal).ToList();
        Assert.NotEmpty(screens);
        AssertGolden("turn-content-screens", "[" + string.Join(",\n", screens) + "]");
    }

    [Fact]
    public async Task A_partner_question_sends_the_recorded_prompt_screen()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel(Answer));

        await api.Services.GetRequiredService<Guardrail>().ScreenPromptAsync("What is the status of billing run 4417?", Ct);

        AssertGolden("partner-prompt-screen", Assert.Single(api.Jev.Requests).Body);
    }

    [Fact]
    public async Task A_search_sends_the_recorded_relevance_request()
    {
        var jev = new FakeJev();
        var judge = new JevRelevanceJudge(Client(jev), Options.Create(new RetrievalOptions()), LoggerFactory.Create(_ => { }).CreateLogger<JevRelevanceJudge>());

        await judge.JudgeAsync("what happens when a fee schedule is missing",
        [
            Chunk(0, "Billing runs are scheduled monthly."),
            Chunk(1, "A missing fee schedule stops the billing run until one is assigned."),
        ], Ct);

        AssertGolden("search-relevance", Assert.Single(jev.Requests).Body);
    }

    [Fact]
    public async Task An_eval_grade_sends_the_recorded_request()
    {
        var jev = new FakeJev();
        var grader = new JevGrader(Client(jev), new JudgeOptions(), NullLogger<JevGrader>.Instance);

        await grader.GradeAsync(new GradeInput("What do I do when a fee schedule is missing?",
            "Assign the agreed schedule. Then re-run the run.",
            [new ReadItem("doc:fees›Missing", "fees › Missing: Assign the agreed schedule, then re-run.", [], BuiltInDomains.Billing)],
            ["Assign the agreed schedule.", "Re-run the run."]), Ct);

        AssertGolden("eval-grade", Assert.Single(jev.Requests).Body);
    }

    private static ScoredChunk Chunk(int i, string text) => new(new ChunkRecord
    {
        TenantId = "firm-a", DocId = $"firm-a/docs/d{i}.md", ChunkId = $"firm-a/docs/d{i}.md#s", SourceType = "docs",
        SourcePath = $"docs/d{i}.md", SectionPath = $"Section {i}", UpdatedAt = DateTimeOffset.UnixEpoch, ModelVersion = "m",
        Text = text, ContentHash = "h",
    }, 1.0 / (i + 1));

    private static JevClient Client(FakeJev jev)
    {
        var loggers = LoggerFactory.Create(_ => { });
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [JevCredential.EnvironmentVariable] = FakeJev.TestKey }).Build();
        var credential = new JevCredential(configuration, loggers.CreateLogger<JevCredential>());
        var client = new HttpClient(new JevAuthHandler(credential) { InnerHandler = jev }) { BaseAddress = new Uri("https://jev.test/") };
        return new JevClient(new GoldenClients(client), credential, Options.Create(new JevOptions()));
    }
}

file sealed class GoldenClients(HttpClient client) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => client;
}
