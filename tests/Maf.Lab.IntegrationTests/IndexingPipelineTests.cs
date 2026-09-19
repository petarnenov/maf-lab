using Maf.Lab.Domain.Retrieval;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Indexing.Pipeline;
using Maf.Lab.Retrieval.Configuration;
using Maf.Lab.Retrieval.Search;
using Maf.Lab.Retrieval.Sparse;
using Maf.Lab.Retrieval.Store;
using Maf.Lab.TestSupport;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Qdrant.Client.Grpc;

namespace Maf.Lab.IntegrationTests;

public class IndexingPipelineTests(QdrantFixture qdrant)
{
    private static readonly Principal FirmA = new("adam", TenantId.Firm("firm-a"), Role.ADVISOR, []);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Every_chunk_carries_complete_metadata_and_documents_without_tenant_are_rejected()
    {
        using var corpus = TempCorpus.Small();
        corpus.Write("orphans/docs/x.md", "# Orphan");
        var collection = Name();
        await using var services = qdrant.Services(collection, corpus.Root);

        var summary = await services.GetRequiredService<IndexingPipeline>().RunAsync(new IndexRequest(), Ct);

        Assert.Contains(summary.Rejected, r => r.Path == "orphans/docs/x.md");
        var chunks = await AllChunksAsync(services, "shared", "firm-a", "firm-b", "firm-c");
        Assert.DoesNotContain(chunks, c => c.DocId.Contains("orphans"));
        Assert.All(chunks, c =>
        {
            Assert.False(string.IsNullOrEmpty(c.DocId));
            Assert.False(string.IsNullOrEmpty(c.ChunkId));
            Assert.True(TenantId.TryParse(c.TenantId, out _));
            Assert.True(SourceType.IsKnown(c.SourceType));
            Assert.False(string.IsNullOrEmpty(c.SourcePath));
            Assert.False(string.IsNullOrEmpty(c.SectionPath));
            Assert.Equal("nomic-embed-text", c.ModelVersion);
            Assert.NotEqual(DateTimeOffset.MinValue, c.UpdatedAt);
            Assert.False(string.IsNullOrEmpty(c.Text));
        });
        Assert.Contains(chunks, c => c.SourceType == "code" && c.Symbol == "Fees.Tiered");
    }

    [Fact]
    public async Task Points_have_dense_and_sparse_vectors()
    {
        using var corpus = TempCorpus.Small();
        var collection = Name();
        await using var services = qdrant.Services(collection, corpus.Root);
        await services.GetRequiredService<IndexingPipeline>().RunAsync(new IndexRequest(), Ct);

        var page = await qdrant.RawClient().ScrollAsync(collection, limit: 5, vectorsSelector: true, cancellationToken: Ct);
        Assert.All(page.Result, p =>
        {
            Assert.True(p.Vectors.Vectors.Vectors.ContainsKey("dense_v1"));
            Assert.True(p.Vectors.Vectors.Vectors.ContainsKey(ChunkSchema.SparseVector));
        });
    }

    [Fact]
    public async Task Reindexing_a_changed_document_leaves_exactly_one_version_and_unchanged_documents_are_skipped()
    {
        using var corpus = TempCorpus.Small();
        var collection = Name();
        await using var services = qdrant.Services(collection, corpus.Root);
        var pipeline = services.GetRequiredService<IndexingPipeline>();
        await pipeline.RunAsync(new IndexRequest(), Ct);
        var before = await AllChunksAsync(services, "shared");

        corpus.Write("shared/docs/billing.md", """
            # Billing overview
            ## Fee schedules
            A fee schedule defines the rate. Version two.
            ## Credits
            Credits reduce the next invoice.
            """, new DateTime(2026, 9, 2, 0, 0, 0, DateTimeKind.Utc));
        var second = await pipeline.RunAsync(new IndexRequest(), Ct);

        Assert.Equal(1, second.DocumentsIndexed);
        Assert.Equal(34, second.DocumentsUnchanged);
        var after = (await AllChunksAsync(services, "shared")).Where(c => c.DocId == "shared/docs/billing.md").ToList();
        Assert.Equal(after.Count, after.Select(c => c.ChunkId).Distinct().Count());
        Assert.All(after, c => Assert.DoesNotContain("FS-REQUIRED", c.Text));
        Assert.DoesNotContain(after, c => c.SectionPath.Contains("Proration"));
        Assert.Contains(after, c => c.SectionPath == "Billing overview > Credits");
        Assert.All(after, c => Assert.Equal(new DateTimeOffset(2026, 9, 2, 0, 0, 0, TimeSpan.Zero), c.UpdatedAt));

        var untouched = before.Where(c => c.DocId != "shared/docs/billing.md").Select(c => c.ChunkId).Order();
        var untouchedAfter = (await AllChunksAsync(services, "shared")).Where(c => c.DocId != "shared/docs/billing.md").Select(c => c.ChunkId).Order();
        Assert.Equal(untouched, untouchedAfter);
    }

    [Fact]
    public async Task Removed_documents_are_removed_from_the_index()
    {
        using var corpus = TempCorpus.Small();
        var collection = Name();
        await using var services = qdrant.Services(collection, corpus.Root);
        var pipeline = services.GetRequiredService<IndexingPipeline>();
        await pipeline.RunAsync(new IndexRequest(), Ct);

        corpus.Delete("firm-a/docs/acme-policy.md");
        await pipeline.RunAsync(new IndexRequest(), Ct);

        Assert.Empty(await AllChunksAsync(services, "firm-a"));
    }

    [Fact]
    public async Task Drift_is_zero_after_indexing_and_positive_after_a_timestamp_bump()
    {
        using var corpus = TempCorpus.Small();
        var collection = Name();
        await using var services = qdrant.Services(collection, corpus.Root);
        await services.GetRequiredService<IndexingPipeline>().RunAsync(new IndexRequest(), Ct);
        var drift = services.GetRequiredService<DriftService>();

        var fresh = await drift.ComputeAsync(null, Ct);
        Assert.Equal(0, fresh.StalePercent);
        Assert.Equal(0, fresh.StaleDocuments);

        corpus.Touch("shared/code/fees.cs", new DateTime(2026, 9, 10, 0, 0, 0, DateTimeKind.Utc));
        var stale = await drift.ComputeAsync(null, Ct);
        Assert.True(stale.StalePercent > 0);
        Assert.Contains(stale.Stale, s => s.DocId == "shared/code/fees.cs");
    }

    [Fact]
    public async Task Contextual_retrieval_switch_prepends_a_generated_sentence_only_when_on()
    {
        using var corpus = TempCorpus.Small();
        var chat = new ScriptedChatClient((_, _, _) => ScriptedChatClient.Text("This chunk is from the billing guide."));

        var offCollection = Name();
        await using (var off = qdrant.Services(offCollection, corpus.Root, chat: chat))
        {
            await off.GetRequiredService<IndexingPipeline>().RunAsync(new IndexRequest { Contextual = false }, Ct);
            Assert.All(await AllChunksAsync(off, "shared"), c => Assert.Null(c.Context));
        }
        Assert.Empty(chat.Requests);

        var onCollection = Name();
        await using var on = qdrant.Services(onCollection, corpus.Root, chat: chat);
        await on.GetRequiredService<IndexingPipeline>().RunAsync(new IndexRequest { Contextual = true }, Ct);
        var chunks = await AllChunksAsync(on, "shared");
        Assert.All(chunks, c => Assert.Equal("This chunk is from the billing guide.", c.Context));
        Assert.NotEmpty(chat.Requests);
        Assert.Contains("data", chat.Requests[0].Messages[0].Text);
    }

    [Fact]
    public async Task Maintenance_refuses_chunks_of_another_tenant()
    {
        await using var services = qdrant.Services(Name());
        await services.GetRequiredService<CollectionBootstrapper>().EnsureAsync(Ct);
        var store = services.GetRequiredService<TenantScopedMaintenance>();
        var chunk = new ChunkRecord
        {
            TenantId = "firm-b", DocId = "firm-b/docs/x.md", ChunkId = "firm-b/docs/x.md#x", SourceType = "docs", SourcePath = "docs/x.md",
            SectionPath = "X", UpdatedAt = DateTimeOffset.UtcNow, ModelVersion = "m", Text = "t", ContentHash = "h",
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.ReplaceDocumentAsync(TenantId.Firm("firm-a"), "firm-b/docs/x.md", [new ChunkWrite(chunk, "dense_v1", new float[768], new SparseVectorData([], []))], Ct));
    }

    [Theory]
    [InlineData(RetrievalModes.Hybrid, FusionModes.Rrf)]
    [InlineData(RetrievalModes.Hybrid, FusionModes.Dbsf)]
    [InlineData(RetrievalModes.Dense, FusionModes.Rrf)]
    [InlineData(RetrievalModes.Sparse, FusionModes.Rrf)]
    public async Task Every_mode_finds_the_procedure_and_stays_within_the_principals_tenants(string mode, string fusion)
    {
        using var corpus = TempCorpus.Small();
        var collection = Name();
        await using var services = qdrant.Services(collection, corpus.Root);
        await services.GetRequiredService<IndexingPipeline>().RunAsync(new IndexRequest(), Ct);
        var search = services.GetRequiredService<DocumentSearchService>();

        var outcome = await search.SearchAsync(FirmA, "what to do when a fee schedule is missing FS-REQUIRED", null, 10,
            new SearchSettings(mode, fusion, "dense_v1", false), Ct);

        Assert.NotEmpty(outcome.Result.Results);
        Assert.All(outcome.Chunks, c => Assert.Contains(c.Chunk.TenantId, new[] { "firm-a", "shared" }));
        Assert.Contains(outcome.Result.Results.Take(3), r => r.DocId.StartsWith("shared/") && r.Snippet.Contains("FS-REQUIRED"));
    }

    [Fact]
    public async Task Search_contract_caps_results_filters_source_types_and_hints_on_identifiers()
    {
        using var corpus = TempCorpus.Small();
        var collection = Name();
        await using var services = qdrant.Services(collection, corpus.Root);
        await services.GetRequiredService<IndexingPipeline>().RunAsync(new IndexRequest(), Ct);
        var search = services.GetRequiredService<DocumentSearchService>();

        var capped = await search.SearchAsync(FirmA, "fee schedule billing", null, 50, null, Ct);
        Assert.True(capped.Result.Results.Count <= 10);

        var code = await search.SearchAsync(FirmA, "tiered fee calculation", [SourceType.Code], 5, null, Ct);
        Assert.NotEmpty(code.Result.Results);
        Assert.All(code.Chunks, c => Assert.Equal("code", c.Chunk.SourceType));

        var truncated = await search.SearchAsync(FirmA, "fee schedule billing run", null, 1, null, Ct);
        Assert.True(truncated.Result.Truncated);
        Assert.NotNull(truncated.Result.RefineHint);

        var id = await search.SearchAsync(FirmA, "4417", null, 5, null, Ct);
        Assert.Empty(id.Result.Results);
        Assert.Contains("get_billing_run_status", id.Result.RefineHint);
    }

    [Fact]
    public async Task Migration_is_restartable_idempotent_and_queries_succeed_throughout()
    {
        using var corpus = TempCorpus.Small();
        var collection = Name();
        await using var services = qdrant.Services(collection, corpus.Root);
        await services.GetRequiredService<IndexingPipeline>().RunAsync(new IndexRequest(), Ct);
        var tenants = new HashSet<TenantId> { TenantId.Shared, TenantId.Firm("firm-a"), TenantId.Firm("firm-b"), TenantId.Firm("firm-c") };
        var countBefore = (await qdrant.RawClient().CountAsync(collection, exact: true, cancellationToken: Ct));
        var migration = services.GetRequiredService<MigrationService>();
        var search = services.GetRequiredService<DocumentSearchService>();
        var queriesDuringMigration = 0;

        // First run is "killed" after two batches.
        await Assert.ThrowsAsync<OperationCanceledException>(() => migration.RunAsync(tenants, "dense_v2", 5, Ct, async batch =>
        {
            var hits = await search.SearchAsync(FirmA, "fee schedule missing", null, 5, null, Ct);
            Assert.NotEmpty(hits.Result.Results);
            queriesDuringMigration++;
            if (batch == 2)
            {
                throw new OperationCanceledException("simulated kill");
            }
        }));

        var partial = (await VersionsAsync(services, tenants)).GetValueOrDefault("all-minilm");
        Assert.InRange(partial, 1, (long)countBefore - 1);

        var summary = await migration.RunAsync(tenants, "dense_v2", 5, Ct, async _ =>
        {
            var hits = await search.SearchAsync(FirmA, "fee schedule missing", null, 5, null, Ct);
            Assert.NotEmpty(hits.Result.Results);
            queriesDuringMigration++;
        });

        Assert.Equal((long)countBefore - partial, summary.Migrated);
        var final = await VersionsAsync(services, tenants);
        Assert.Equal((long)countBefore, final.GetValueOrDefault("all-minilm"));
        Assert.False(final.ContainsKey("nomic-embed-text"));
        Assert.Equal(countBefore, await qdrant.RawClient().CountAsync(collection, exact: true, cancellationToken: Ct));
        Assert.True(queriesDuringMigration > 2);

        var again = await migration.RunAsync(tenants, "dense_v2", 5, Ct);
        Assert.Equal(0, again.Migrated);

        // Switching queries to the new vector works.
        var v2 = await search.SearchAsync(FirmA, "fee schedule missing", null, 5, new SearchSettings(RetrievalModes.Dense, FusionModes.Rrf, "dense_v2", false), Ct);
        Assert.NotEmpty(v2.Result.Results);
    }

    private static string Name() => $"t_{Guid.NewGuid():N}";

    private static async Task<List<ChunkRecord>> AllChunksAsync(IServiceProvider services, params string[] tenants)
    {
        var store = services.GetRequiredService<TenantScopedMaintenance>();
        var all = new List<ChunkRecord>();
        foreach (var t in tenants)
        {
            TenantId.TryParse(t, out var tenant);
            await foreach (var c in store.ListChunksAsync(tenant, Ct))
            {
                all.Add(c);
            }
        }
        return all;
    }

    private static async Task<Dictionary<string, long>> VersionsAsync(IServiceProvider services, IEnumerable<TenantId> tenants)
    {
        var store = services.GetRequiredService<TenantScopedMaintenance>();
        var result = new Dictionary<string, long>();
        foreach (var t in tenants)
        {
            foreach (var v in await store.ModelVersionsAsync(t, Ct))
            {
                result[v.ModelVersion] = result.GetValueOrDefault(v.ModelVersion) + v.Chunks;
            }
        }
        return result;
    }
}
