using System.Diagnostics;
using System.Text.RegularExpressions;
using Maf.Lab.Domain.Retrieval;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Retrieval.Configuration;
using Maf.Lab.Retrieval.Models;
using Maf.Lab.Retrieval.Rerank;
using Maf.Lab.Retrieval.Sparse;
using Maf.Lab.Retrieval.Store;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Maf.Lab.Hosting;

namespace Maf.Lab.Retrieval.Search;

/// <summary>Per-call overrides used by the eval harness to compare retrieval configurations.</summary>
/// <param name="DenseFloor">Lowest dense score a candidate may have and still count. Null leaves the branch unfiltered.</param>
/// <param name="SparseFloor">The same for the sparse branch. Dense and sparse are different scales, so never one value.</param>
/// <param name="RelevanceGate">Ask Jev whether any fused candidate addresses the query, and return nothing when none does.</param>
/// <param name="Reranker">Which reranker <paramref name="Rerank"/> uses (<see cref="RerankerKinds"/>); null means the configured one.</param>
public sealed record SearchSettings(string Mode, string Fusion, string DenseVector, bool Rerank,
    float? DenseFloor = null, float? SparseFloor = null, bool RelevanceGate = false, string? Reranker = null);

/// <param name="Relevance">
/// What the relevance judge said about this search, as numbers only (<see cref="SearchDiagnostics.SummaryOf"/>); null
/// when the search did not ask it.
/// </param>
public sealed record SearchOutcome(SearchDocumentsResult Result, IReadOnlyList<ScoredChunk> Chunks,
    System.Text.Json.Nodes.JsonObject? Relevance = null);

/// <param name="chunkSearch">
/// Measurement only (neo4j-retrieval-spike): another chunk search to use instead of <paramref name="search"/>, passed by
/// the eval and never registered. Each call site names <paramref name="search"/> directly when it is null, so the code
/// graph keeps the production path to <see cref="TenantScopedSearch.QueryAsync"/> — a call through the interface alone
/// would hide it.
/// </param>
public sealed partial class DocumentSearchService(
    TenantScopedSearch search,
    IDenseEncoder dense,
    Bm25Store bm25,
    IEnumerable<IReranker> rerankers,
    IRelevanceJudge judge,
    IQueryTranslator translator,
    IOptions<RetrievalOptions> options,
    IOptions<ModelOptions> models,
    ILogger<DocumentSearchService> logger,
    IChunkSearch? chunkSearch = null)
{
    public const int MaxResultsCap = 10;
    private readonly RetrievalOptions _options = options.Value;

    public SearchSettings DefaultSettings => new(_options.Mode, _options.Fusion, _options.DenseVector, _options.RerankEnabled,
        _options.DenseFloorFor(models.Value, _options.DenseVector), _options.SparseFloor, _options.RelevanceGateEnabled, _options.Reranker);

    public async Task<SearchOutcome> SearchAsync(
        Principal principal, string query, IReadOnlyList<string>? sourceTypes, int? maxResults, SearchSettings? settings, CancellationToken ct,
        SearchDiagnostics? diagnostics = null)
    {
        settings ??= DefaultSettings;
        var limit = Math.Clamp(maxResults ?? 5, 1, MaxResultsCap);

        if (IdentifierOnly().IsMatch(query))
        {
            return new SearchOutcome(new SearchDocumentsResult([], 0, false,
                "The query looks like a billing run identifier, not a question. Use get_billing_run_status for a run's current state, " +
                "or search_billing_runs to find runs. search_documents answers how/why/procedure questions."), []);
        }

        var sw = Stopwatch.StartNew();
        var candidateLimit = settings.Rerank ? Math.Max(_options.RerankCandidates, limit) : Math.Max(limit * 2, 20);
        var (candidates, relevance) = await RankCoreAsync(principal, query, sourceTypes, candidateLimit, settings, ct, diagnostics);
        diagnostics?.Settings.Add("limit", limit);

        var top = candidates.Take(limit).ToList();
        var truncated = candidates.Count > limit;
        var hint = top.Count == 0
            ? "No matching documentation. Rephrase as a question about a procedure, policy or term, or remove the sourceTypes filter."
            : truncated ? "More matches exist; narrow the query or filter by sourceTypes to see them." : null;

        logger.LogInformation("search_documents mode={Mode} fusion={Fusion} rerank={Rerank} gate={Gate} candidates={Candidates} returned={Returned} ms={Elapsed}",
            settings.Mode, settings.Fusion, settings.Rerank, settings.RelevanceGate, candidates.Count, top.Count, sw.ElapsedMilliseconds);

        var result = new SearchDocumentsResult(
            top.Select(c => new DocumentSnippet(Snippet(c.Chunk.Text), c.Chunk.SourcePath, c.Chunk.SectionPath, Math.Round(c.Score, 4), c.Chunk.UpdatedAt, c.Chunk.DocId)).ToList(),
            candidates.Count,
            truncated,
            hint);
        return new SearchOutcome(result, top, relevance);
    }

    /// <summary>
    /// Ranked, tenant-scoped candidates for a query (up to <paramref name="k"/>, beyond the tool's cap of 10).
    /// Used by search_documents and by the retrieval eval (recall@20).
    /// </summary>
    public async Task<IReadOnlyList<ScoredChunk>> RankAsync(Principal principal, string query, IReadOnlyList<string>? sourceTypes, int k,
        SearchSettings settings, CancellationToken ct, SearchDiagnostics? diagnostics = null) =>
        (await RankCoreAsync(principal, query, sourceTypes, k, settings, ct, diagnostics)).Ranked;

    private async Task<(IReadOnlyList<ScoredChunk> Ranked, System.Text.Json.Nodes.JsonObject? Relevance)> RankCoreAsync(
        Principal principal, string query, IReadOnlyList<string>? sourceTypes, int k,
        SearchSettings settings, CancellationToken ct, SearchDiagnostics? diagnostics)
    {
        var clock = Stopwatch.StartNew();
        // Both halves of hybrid search read the same text, so the query is brought into the corpus language before
        // either of them sees it. A query already in that language is returned untouched.
        var translation = await translator.ToCorpusLanguageAsync(query, ct);
        var searchedQuery = translation.Searched;
        // The stages no library instruments get a span each, so a slow search says which half was slow.
        var denseVector = settings.Mode == RetrievalModes.Sparse
            ? null
            : await LabTelemetry.InSpanAsync("retrieval.embed",
                () => dense.EmbedQueryAsync(settings.DenseVector, searchedQuery, ct),
                ("retrieval.dense_vector", settings.DenseVector));
        var embedMs = clock.ElapsedMilliseconds;
        clock.Restart();
        var model = await bm25.LoadAsync(ct);
        var sparseVector = settings.Mode == RetrievalModes.Dense
            ? null
            : await LabTelemetry.InSpanAsync("retrieval.sparse_encode",
                () => Task.FromResult(Bm25Encoder.EncodeQuery(model, searchedQuery)));
        var sparseMs = clock.ElapsedMilliseconds;

        var request = new SearchRequest
        {
            Dense = denseVector,
            Sparse = sparseVector,
            DenseVector = settings.DenseVector,
            Mode = settings.Mode,
            Fusion = settings.Fusion,
            SourceTypes = sourceTypes,
            Limit = k,
            PrefetchLimit = Math.Max(_options.MinPrefetch, k * _options.PrefetchMultiplier),
            DenseFloor = settings.DenseFloor,
            SparseFloor = settings.SparseFloor,
        };
        clock.Restart();
        var candidates = await LabTelemetry.InSpanAsync("retrieval.query",
            () => chunkSearch is null ? search.QueryAsync(principal, request, ct) : chunkSearch.QueryAsync(principal, request, ct),
            ("retrieval.mode", settings.Mode), ("retrieval.fusion", settings.Fusion));
        var qdrantMs = clock.ElapsedMilliseconds;

        IReadOnlyList<ScoredChunk> ranked = candidates;
        var rerankerKind = settings.Reranker ?? _options.Reranker;
        var reranker = settings.Rerank ? rerankers.FirstOrDefault(r => r.Kind == rerankerKind) : null;

        // One question to Jev per search, shared by the gate and the Jev reranker. The gate reads only the maximum —
        // the stable part of Jev's answer — and decides whether the corpus answers at all, never which chunks.
        RelevanceJudgement? judgement = null;
        var silenced = false;
        long relevanceMs = 0;
        if ((settings.RelevanceGate || reranker is JevReranker) && candidates.Count > 0)
        {
            clock.Restart();
            judgement = await LabTelemetry.InSpanAsync("retrieval.relevance",
                () => judge.JudgeAsync(searchedQuery, candidates, ct));
            relevanceMs = clock.ElapsedMilliseconds;
            silenced = settings.RelevanceGate && judgement.Max is { } max && max < _options.RelevanceFloor;
        }

        long rerankMs = 0;
        if (silenced)
        {
            ranked = [];
        }
        else if (reranker is not null)
        {
            clock.Restart();
            ranked = reranker is JevReranker
                ? JevReranker.Order(candidates, judgement)
                : await LabTelemetry.InSpanAsync("retrieval.rerank", () => reranker.RerankAsync(searchedQuery, candidates, ct));
            rerankMs = clock.ElapsedMilliseconds;
        }

        // The same stages the trace shows per turn, as numbers an operator can aggregate over many.
        LabTelemetry.Instruments.RetrievalStage.Record(embedMs, new KeyValuePair<string, object?>("stage", "embed"));
        LabTelemetry.Instruments.RetrievalStage.Record(sparseMs, new KeyValuePair<string, object?>("stage", "sparse_encode"));
        LabTelemetry.Instruments.RetrievalStage.Record(qdrantMs, new KeyValuePair<string, object?>("stage", "query"));
        if (reranker is not null)
        {
            LabTelemetry.Instruments.RetrievalStage.Record(rerankMs, new KeyValuePair<string, object?>("stage", "rerank"));
        }
        if (judgement is not null)
        {
            LabTelemetry.Instruments.RetrievalStage.Record(relevanceMs, new KeyValuePair<string, object?>("stage", "relevance"));
        }

        if (diagnostics is not null)
        {
            foreach (var tenant in principal.ReadableTenants)
            {
                diagnostics.TenantScope.Add(tenant.Value);
            }
            diagnostics.Settings["mode"] = settings.Mode;
            diagnostics.Settings["fusion"] = settings.Fusion;
            diagnostics.Settings["denseVector"] = settings.DenseVector;
            diagnostics.Settings["candidateLimit"] = k;
            diagnostics.Settings["prefetchLimit"] = request.PrefetchLimit;
            diagnostics.Settings["rerank"] = settings.Rerank;
            diagnostics.Settings["reranker"] = settings.Rerank ? rerankerKind : null;
            diagnostics.Settings["relevanceGate"] = settings.RelevanceGate;
            diagnostics.Settings["denseFloor"] = settings.DenseFloor;
            diagnostics.Settings["sparseFloor"] = settings.SparseFloor;
            diagnostics.Settings["sourceTypes"] = sourceTypes is null ? null : new System.Text.Json.Nodes.JsonArray(sourceTypes.Select(t => (System.Text.Json.Nodes.JsonNode)System.Text.Json.Nodes.JsonValue.Create(t)!).ToArray());
            diagnostics.Query["text"] = searchedQuery;
            diagnostics.Query["original"] = translation.Original;
            diagnostics.Query["translated"] = translation.Changed;
            diagnostics.Query["translationMs"] = translation.DurationMs;
            diagnostics.Query["translationNote"] = translation.Reason;
            diagnostics.Query["terms"] = new System.Text.Json.Nodes.JsonArray(model.Tokenize(searchedQuery).Distinct(StringComparer.Ordinal)
                .Select(t => (System.Text.Json.Nodes.JsonNode)new System.Text.Json.Nodes.JsonObject
                {
                    ["term"] = t,
                    ["idf"] = model.TryGetTermId(t, out var id) ? Math.Round(model.Idf(id), 4) : null,
                    ["inVocabulary"] = model.TryGetTermId(t, out _),
                }).ToArray());
            diagnostics.Query["denseModel"] = settings.Mode == RetrievalModes.Sparse ? null : dense.ModelVersion(settings.DenseVector);
            diagnostics.Query["denseDims"] = denseVector?.Length;
            diagnostics.Fused = SearchDiagnostics.Candidates(candidates);
            diagnostics.Rerank = settings.Rerank ? new System.Text.Json.Nodes.JsonArray(ranked.Select(c => (System.Text.Json.Nodes.JsonNode)System.Text.Json.Nodes.JsonValue.Create(c.Chunk.ChunkId)!).ToArray()) : null;
            diagnostics.Relevance = judgement is null ? null : SearchDiagnostics.RelevanceOf(judgement, candidates, settings.RelevanceGate, _options.RelevanceFloor, silenced);

            long branchMs = 0;
            if (!diagnostics.Branches)
            {
                // The caller wants what the search did, not the monitor's picture of each branch (the eval, counting
                // judge failures), so the branches are not queried again.
            }
            else if (_options.TraceBranches && settings.Mode == RetrievalModes.Hybrid)
            {
                // Per-branch lists go through the same tenant-scoped query path, but without the floors: the
                // operator needs to see the near misses the answer was denied, not the same list the model got.
                clock.Restart();
                var unfiltered = request with { DenseFloor = null, SparseFloor = null };
                if (denseVector is not null)
                {
                    diagnostics.Dense = SearchDiagnostics.Candidates(
                        await (chunkSearch is null ? search.QueryAsync(principal, unfiltered with { Mode = RetrievalModes.Dense }, ct) : chunkSearch.QueryAsync(principal, unfiltered with { Mode = RetrievalModes.Dense }, ct)), settings.DenseFloor);
                }
                if (sparseVector is { IsEmpty: false })
                {
                    diagnostics.Sparse = SearchDiagnostics.Candidates(
                        await (chunkSearch is null ? search.QueryAsync(principal, unfiltered with { Mode = RetrievalModes.Sparse }, ct) : chunkSearch.QueryAsync(principal, unfiltered with { Mode = RetrievalModes.Sparse }, ct)), settings.SparseFloor);
                }
                branchMs = clock.ElapsedMilliseconds;
            }
            else if (settings.Mode == RetrievalModes.Dense)
            {
                // `candidates` already cleared the floor, so re-query without it or the near misses are invisible.
                diagnostics.Dense = settings.DenseFloor is null
                    ? SearchDiagnostics.Candidates(candidates)
                    : SearchDiagnostics.Candidates(await (chunkSearch is null ? search.QueryAsync(principal, request with { DenseFloor = null }, ct) : chunkSearch.QueryAsync(principal, request with { DenseFloor = null }, ct)), settings.DenseFloor);
            }
            else if (settings.Mode == RetrievalModes.Sparse)
            {
                diagnostics.Sparse = settings.SparseFloor is null
                    ? SearchDiagnostics.Candidates(candidates)
                    : SearchDiagnostics.Candidates(await (chunkSearch is null ? search.QueryAsync(principal, request with { SparseFloor = null }, ct) : chunkSearch.QueryAsync(principal, request with { SparseFloor = null }, ct)), settings.SparseFloor);
            }
            diagnostics.Timings["embedMs"] = embedMs;
            diagnostics.Timings["sparseEncodeMs"] = sparseMs;
            diagnostics.Timings["qdrantMs"] = qdrantMs;
            diagnostics.Timings["rerankMs"] = rerankMs;
            diagnostics.Timings["relevanceMs"] = relevanceMs;
            diagnostics.Timings["branchQueriesMs"] = branchMs;
        }
        // The judge's verdict travels with every judged search, traced or not: it is one of the Jev requests this
        // search made, and whoever counts them must not depend on the monitor being switched on.
        var relevance = judgement is null ? null
            : SearchDiagnostics.SummaryOf(judgement, settings.RelevanceGate, _options.RelevanceFloor, silenced,
                settings.Rerank ? rerankerKind : null, rerankedByJev: reranker is JevReranker && !silenced && judgement.Scores is not null);
        return (ranked, relevance);
    }

    private string Snippet(string text)
    {
        if (text.Length <= _options.SnippetMaxChars)
        {
            return text;
        }
        var cut = text.LastIndexOf(' ', _options.SnippetMaxChars);
        return text[..(cut > _options.SnippetMaxChars / 2 ? cut : _options.SnippetMaxChars)] + " …";
    }

    [GeneratedRegex(@"^\s*(run\s*#?\s*)?#?\d{2,}\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex IdentifierOnly();
}
