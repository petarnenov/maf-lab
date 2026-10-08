using System.Net.Http.Headers;
using Maf.Lab.Domain.BulgarianHistory;
using Maf.Lab.Domain.Configuration;
using Maf.Lab.Domain.Retrieval;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Indexing.Pipeline;
using Maf.Lab.Retrieval.Auth;
using Maf.Lab.Retrieval.Configuration;
using Maf.Lab.Retrieval.Models;
using Maf.Lab.Retrieval.Rerank;
using Maf.Lab.Retrieval.Search;
using Maf.Lab.Retrieval.Store;
using Maf.Lab.TestSupport;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;
using Qdrant.Client.Grpc;
using Role = Maf.Lab.Domain.Tenancy.Role;

namespace Maf.Lab.IntegrationTests;

/// <summary>The Bulgarian history corpus (data-bulgarian-history/) indexed once, with the fake embedder, into its own collection.</summary>
public sealed class BulgarianHistoryIndexFixture(QdrantFixture qdrant) : IAsyncLifetime
{
    public string Collection { get; } = $"bg_history_{Guid.NewGuid():N}";
    public string MetaCollection => Collection + "_meta";
    public string CorpusRoot { get; } = Path.Combine(CorpusIndexFixture.RepoRoot(), "data-bulgarian-history");
    public QdrantFixture Qdrant => qdrant;

    /// <summary>
    /// The corpus is Bulgarian, and the retrieval core's defaults translate a non-Latin query into English (an LLM call)
    /// before searching — which would search English terms in a Bulgarian vocabulary and make the test call a model.
    /// The query is searched as written.
    /// </summary>
    public static void SearchAsWritten(Dictionary<string, string?> values) => values["Retrieval:NormalizeQueryLanguage"] = "false";

    /// <summary>Retrieval services over this collection, searching queries as written.</summary>
    public ServiceProvider Services(Action<IServiceCollection>? services = null) =>
        qdrant.Services(Collection, CorpusRoot, SearchAsWritten, services: services);

    public async ValueTask InitializeAsync()
    {
        await using var services = qdrant.Services(Collection, CorpusRoot);
        await services.GetRequiredService<IndexingPipeline>().RunAsync(new IndexRequest(), CancellationToken.None);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

[CollectionDefinition(Name)]
public sealed class BulgarianHistoryCollection : ICollectionFixture<BulgarianHistoryIndexFixture>
{
    public const string Name = "indexed-bulgarian-history";
}

/// <summary>
/// The shared-only corpus every principal reads whole (bulgarian-history-mcp): every chunk is shared, and two firms'
/// advisors and a third firm's admin with no advisor ids get the same documents, sections and snippets in the same order.
/// </summary>
[Collection(BulgarianHistoryCollection.Name)]
public sealed class BulgarianHistoryAcceptanceTests(BulgarianHistoryIndexFixture corpus)
{
    private const string Query = "покръстването на българите";

    private static readonly Principal[] Principals =
    [
        new("adam", TenantId.Firm("firm-a"), Role.ADVISOR, ["adv-a-1"]),
        new("bea", TenantId.Firm("firm-b"), Role.ADVISOR, ["adv-b-1"]),
        new("carla", TenantId.Firm("firm-c"), Role.FIRM_ADMIN, []),
    ];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Every_chunk_of_the_corpus_is_shared()
    {
        var client = corpus.Qdrant.RawClient();
        var tenants = new List<string>();
        PointId? offset = null;
        do
        {
            var page = await client.ScrollAsync(corpus.Collection, limit: 256, offset: offset, payloadSelector: true, cancellationToken: Ct);
            tenants.AddRange(page.Result.Select(p => p.Payload[ChunkSchema.TenantId].StringValue));
            offset = page.NextPageOffset;
        }
        while (offset is not null);

        Assert.NotEmpty(tenants);
        Assert.Equal((ulong)tenants.Count, await client.CountAsync(corpus.Collection, exact: true, cancellationToken: Ct));
        Assert.All(tenants, t => Assert.Equal("shared", t));
    }

    [Fact]
    public async Task Every_firm_and_every_advisor_gets_the_same_result()
    {
        await using var services = corpus.Services();
        var search = services.GetRequiredService<DocumentSearchService>();

        foreach (var mode in RetrievalModes.All)
        {
            var settings = new SearchSettings(mode, FusionModes.Rrf, "dense_v3", false);
            var outcomes = new List<SearchOutcome>();
            foreach (var principal in Principals)
            {
                outcomes.Add(await search.SearchAsync(principal, Query, null, 10, settings, Ct));
            }
            AssertSameSharedResult(outcomes, mode);
        }
    }

    [Fact]
    public async Task Every_firm_gets_the_same_result_through_the_relevance_gate_and_the_reranker()
    {
        // As TenancyAcceptanceTests: a recording reranker and a judge that finds everything relevant, so no network.
        await using var services = corpus.Services(s => s.AddSingleton<IReranker>(new ReversingReranker()).AddSingleton<IRelevanceJudge>(new AllRelevantJudge()));
        var search = services.GetRequiredService<DocumentSearchService>();

        var settings = new SearchSettings(RetrievalModes.Hybrid, FusionModes.Rrf, "dense_v3", true, RelevanceGate: true, Reranker: ReversingReranker.Name);
        var outcomes = new List<SearchOutcome>();
        foreach (var principal in Principals)
        {
            outcomes.Add(await search.SearchAsync(principal, Query, null, 10, settings, Ct));
        }
        AssertSameSharedResult(outcomes, "hybrid+gate+rerank");
    }

    [Fact]
    public async Task The_server_answers_the_same_snippets_for_two_firms()
    {
        var app = Maf.Lab.BulgarianHistory.Program.BuildApp([], b =>
        {
            var values = corpus.Qdrant.Config(corpus.Collection, corpus.CorpusRoot);
            values["BulgarianHistory:Collection"] = corpus.Collection;
            values["BulgarianHistory:MetaCollection"] = corpus.MetaCollection;
            values["Auth:SigningKey"] = new AuthOptions().SigningKey;
            values["Urls"] = "http://127.0.0.1:0";
            BulgarianHistoryIndexFixture.SearchAsWritten(values);
            // Added after the server pins Qdrant:Collection from bulgarian-history.json: this test's collection wins.
            b.Configuration.AddInMemoryCollection(values);
            b.Logging.SetMinimumLevel(LogLevel.Warning);
            b.Services.RemoveAll<IDenseEncoder>();
            b.Services.AddSingleton<IDenseEncoder>(FakeDenseEncoder.Default());
        });
        await app.StartAsync(Ct);
        try
        {
            var endpoint = new Uri(app.Urls.First().TrimEnd('/') + "/mcp");
            var answers = new List<string>();
            foreach (var (user, firm) in new[] { ("adam", "firm-a"), ("bea", "firm-b") })
            {
                var (token, _) = DevJwt.Issue(new AuthOptions(), user, TenantId.Firm(firm), Role.ADVISOR, []);
                var http = new HttpClient();
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
                await using var client = await McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions
                {
                    Endpoint = endpoint, TransportMode = HttpTransportMode.StreamableHttp,
                }, http, NullLoggerFactory.Instance, ownsHttpClient: true), cancellationToken: Ct);

                var result = await client.CallToolAsync(BulgarianHistoryTools.Search,
                    new Dictionary<string, object?> { ["query"] = Query, ["maxResults"] = 10 }, cancellationToken: Ct);
                Assert.NotEqual(true, result.IsError);
                var structured = result.StructuredContent!.Value;
                var results = structured.GetProperty("results");
                Assert.True(results.GetArrayLength() > 0);
                Assert.All(results.EnumerateArray(), r => Assert.StartsWith("shared/", r.GetProperty("docId").GetString()));
                answers.Add(structured.GetRawText());
            }
            Assert.Equal(answers[0], answers[1]);
        }
        finally
        {
            await app.StopAsync(CancellationToken.None);
            await app.DisposeAsync();
        }
    }

    private static void AssertSameSharedResult(IReadOnlyList<SearchOutcome> outcomes, string label)
    {
        foreach (var outcome in outcomes)
        {
            Assert.True(outcome.Result.Results.Count > 0, $"{label}: no results");
            Assert.All(outcome.Chunks, c => Assert.Equal("shared", c.Chunk.TenantId));
            Assert.All(outcome.Result.Results, r => Assert.StartsWith("shared/", r.DocId));
        }
        var expected = Key(outcomes[0].Result);
        foreach (var outcome in outcomes.Skip(1))
        {
            Assert.True(expected.SequenceEqual(Key(outcome.Result)), $"{label}: results differ between principals");
        }
    }

    private static List<(string DocId, string SectionPath, string SourcePath, string Snippet)> Key(SearchDocumentsResult result) =>
        result.Results.Select(r => (r.DocId, r.SectionPath, r.SourcePath, r.Snippet)).ToList();

    private sealed class ReversingReranker : IReranker
    {
        public const string Name = "reversing";

        public string Kind => Name;

        public Task<IReadOnlyList<ScoredChunk>> RerankAsync(string query, IReadOnlyList<ScoredChunk> candidates, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ScoredChunk>>(candidates.Reverse().ToList());
    }

    /// <summary>Finds every candidate relevant, so the gate never silences and no Jev call is made.</summary>
    private sealed class AllRelevantJudge : IRelevanceJudge
    {
        public Task<RelevanceJudgement> JudgeAsync(string query, IReadOnlyList<ScoredChunk> candidates, CancellationToken ct) =>
            Task.FromResult(new RelevanceJudgement(candidates.Select(_ => 1.0).ToList(), null, "all-relevant", 0));
    }
}
