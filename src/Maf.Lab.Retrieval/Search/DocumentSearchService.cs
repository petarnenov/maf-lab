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
public sealed record SearchSettings(string Mode, string Fusion, string DenseVector, bool Rerank,
    float? DenseFloor = null, float? SparseFloor = null);

public sealed record SearchOutcome(SearchDocumentsResult Result, IReadOnlyList<ScoredChunk> Chunks);

public sealed partial class DocumentSearchService(
    TenantScopedSearch search,
    IDenseEncoder dense,
    Bm25Store bm25,
    IReranker reranker,
    IQueryTranslator translator,
    IOptions<RetrievalOptions> options,
    ILogger<DocumentSearchService> logger)
{
    public const int MaxResultsCap = 10;
    private readonly RetrievalOptions _options = options.Value;

    public SearchSettings DefaultSettings => new(_options.Mode, _options.Fusion, _options.DenseVector, _options.RerankEnabled,
        _options.DenseFloor, _options.SparseFloor);

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
        var candidates = await RankAsync(principal, query, sourceTypes, candidateLimit, settings, ct, diagnostics);
        diagnostics?.Settings.Add("limit", limit);

        var top = candidates.Take(limit).ToList();
        var truncated = candidates.Count > limit;
        var hint = top.Count == 0
            ? "No matching documentation. Rephrase as a question about a procedure, policy or term, or remove the sourceTypes filter."
            : truncated ? "More matches exist; narrow the query or filter by sourceTypes to see them." : null;

        logger.LogInformation("search_documents mode={Mode} fusion={Fusion} rerank={Rerank} candidates={Candidates} returned={Returned} ms={Elapsed}",
            settings.Mode, settings.Fusion, settings.Rerank, candidates.Count, top.Count, sw.ElapsedMilliseconds);

        var result = new SearchDocumentsResult(
            top.Select(c => new DocumentSnippet(Snippet(c.Chunk.Text), c.Chunk.SourcePath, c.Chunk.SectionPath, Math.Round(c.Score, 4), c.Chunk.UpdatedAt, c.Chunk.DocId)).ToList(),
            candidates.Count,
            truncated,
            hint);
        return new SearchOutcome(result, top);
    }

    /// <summary>
    /// Ranked, tenant-scoped candidates for a query (up to <paramref name="k"/>, beyond the tool's cap of 10).
    /// Used by search_documents and by the retrieval eval (recall@20).
    /// </summary>
    public async Task<IReadOnlyList<ScoredChunk>> RankAsync(Principal principal, string query, IReadOnlyList<string>? sourceTypes, int k,
        SearchSettings settings, CancellationToken ct, SearchDiagnostics? diagnostics = null)
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
            () => search.QueryAsync(principal, request, ct),
            ("retrieval.mode", settings.Mode), ("retrieval.fusion", settings.Fusion));
        var qdrantMs = clock.ElapsedMilliseconds;

        IReadOnlyList<ScoredChunk> ranked = candidates;
        long rerankMs = 0;
        if (settings.Rerank)
        {
            clock.Restart();
            ranked = await LabTelemetry.InSpanAsync("retrieval.rerank",
                () => reranker.RerankAsync(searchedQuery, candidates, ct));
            rerankMs = clock.ElapsedMilliseconds;
        }

        // The same stages the trace shows per turn, as numbers an operator can aggregate over many.
        LabTelemetry.Instruments.RetrievalStage.Record(embedMs, new KeyValuePair<string, object?>("stage", "embed"));
        LabTelemetry.Instruments.RetrievalStage.Record(sparseMs, new KeyValuePair<string, object?>("stage", "sparse_encode"));
        LabTelemetry.Instruments.RetrievalStage.Record(qdrantMs, new KeyValuePair<string, object?>("stage", "query"));
        if (settings.Rerank)
        {
            LabTelemetry.Instruments.RetrievalStage.Record(rerankMs, new KeyValuePair<string, object?>("stage", "rerank"));
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
            diagnostics.Settings["denseFloor"] = settings.DenseFloor;
            diagnostics.Settings["sparseFloor"] = settings.SparseFloor;
            diagnostics.Settings["sourceTypes"] = sourceTypes is null ? null : new System.Text.Json.Nodes.JsonArray(sourceTypes.Select(t => (System.Text.Json.Nodes.JsonNode)System.Text.Json.Nodes.JsonValue.Create(t)!).ToArray());
            diagnostics.Query["text"] = searchedQuery;
            diagnostics.Query["original"] = translation.Original;
            diagnostics.Query["translated"] = translation.Changed;
            diagnostics.Query["translationMs"] = translation.DurationMs;
            diagnostics.Query["translationNote"] = translation.Reason;
            diagnostics.Query["terms"] = new System.Text.Json.Nodes.JsonArray(Bm25Tokenizer.Tokenize(searchedQuery).Distinct(StringComparer.Ordinal)
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

            long branchMs = 0;
            if (_options.TraceBranches && settings.Mode == RetrievalModes.Hybrid)
            {
                // Per-branch lists go through the same tenant-scoped query path, but without the floors: the
                // operator needs to see the near misses the answer was denied, not the same list the model got.
                clock.Restart();
                var unfiltered = request with { DenseFloor = null, SparseFloor = null };
                if (denseVector is not null)
                {
                    diagnostics.Dense = SearchDiagnostics.Candidates(
                        await search.QueryAsync(principal, unfiltered with { Mode = RetrievalModes.Dense }, ct), settings.DenseFloor);
                }
                if (sparseVector is { IsEmpty: false })
                {
                    diagnostics.Sparse = SearchDiagnostics.Candidates(
                        await search.QueryAsync(principal, unfiltered with { Mode = RetrievalModes.Sparse }, ct), settings.SparseFloor);
                }
                branchMs = clock.ElapsedMilliseconds;
            }
            else if (settings.Mode == RetrievalModes.Dense)
            {
                // `candidates` already cleared the floor, so re-query without it or the near misses are invisible.
                diagnostics.Dense = settings.DenseFloor is null
                    ? SearchDiagnostics.Candidates(candidates)
                    : SearchDiagnostics.Candidates(await search.QueryAsync(principal, request with { DenseFloor = null }, ct), settings.DenseFloor);
            }
            else if (settings.Mode == RetrievalModes.Sparse)
            {
                diagnostics.Sparse = settings.SparseFloor is null
                    ? SearchDiagnostics.Candidates(candidates)
                    : SearchDiagnostics.Candidates(await search.QueryAsync(principal, request with { SparseFloor = null }, ct), settings.SparseFloor);
            }
            diagnostics.Timings["embedMs"] = embedMs;
            diagnostics.Timings["sparseEncodeMs"] = sparseMs;
            diagnostics.Timings["qdrantMs"] = qdrantMs;
            diagnostics.Timings["rerankMs"] = rerankMs;
            diagnostics.Timings["branchQueriesMs"] = branchMs;
        }
        return ranked;
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
