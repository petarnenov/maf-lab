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

namespace Maf.Lab.Retrieval.Search;

/// <summary>Per-call overrides used by the eval harness to compare retrieval configurations.</summary>
public sealed record SearchSettings(string Mode, string Fusion, string DenseVector, bool Rerank);

public sealed record SearchOutcome(SearchDocumentsResult Result, IReadOnlyList<ScoredChunk> Chunks);

public sealed partial class DocumentSearchService(
    TenantScopedSearch search,
    IDenseEncoder dense,
    Bm25Store bm25,
    IReranker reranker,
    IOptions<RetrievalOptions> options,
    ILogger<DocumentSearchService> logger)
{
    public const int MaxResultsCap = 10;
    private readonly RetrievalOptions _options = options.Value;

    public SearchSettings DefaultSettings => new(_options.Mode, _options.Fusion, _options.DenseVector, _options.RerankEnabled);

    public async Task<SearchOutcome> SearchAsync(
        Principal principal, string query, IReadOnlyList<string>? sourceTypes, int? maxResults, SearchSettings? settings, CancellationToken ct)
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
        var candidates = await RankAsync(principal, query, sourceTypes, candidateLimit, settings, ct);

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
        SearchSettings settings, CancellationToken ct)
    {
        var request = new SearchRequest
        {
            Dense = settings.Mode == RetrievalModes.Sparse ? null : await dense.EmbedQueryAsync(settings.DenseVector, query, ct),
            Sparse = settings.Mode == RetrievalModes.Dense ? null : Bm25Encoder.EncodeQuery(await bm25.LoadAsync(ct), query),
            DenseVector = settings.DenseVector,
            Mode = settings.Mode,
            Fusion = settings.Fusion,
            SourceTypes = sourceTypes,
            Limit = k,
            PrefetchLimit = Math.Max(_options.MinPrefetch, k * _options.PrefetchMultiplier),
        };
        var candidates = await search.QueryAsync(principal, request, ct);
        return settings.Rerank ? await reranker.RerankAsync(query, candidates, ct) : candidates;
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
