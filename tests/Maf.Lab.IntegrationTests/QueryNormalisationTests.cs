using Maf.Lab.Retrieval.Configuration;
using Maf.Lab.Retrieval.Search;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Retrieval.Auth;
using Microsoft.Extensions.DependencyInjection;

namespace Maf.Lab.IntegrationTests;

/// <summary>
/// The whole point of normalising the query: a question in another language must reach the same chunks as its
/// English twin, through the same tenant-scoped path, over a real index.
/// </summary>
[Collection(CorpusCollection.Name)]
public class QueryNormalisationTests(CorpusIndexFixture corpus)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Principal Advisor(string firm = "firm-a") =>
        new("adam", TenantId.Firm(firm), Role.ADVISOR, []);

    /// <summary>Stands in for the translation model: returns the English twin of the question under test.</summary>
    private sealed class FixedTranslator(string searched) : IQueryTranslator
    {
        public Task<TranslatedQuery> ToCorpusLanguageAsync(string query, CancellationToken ct) =>
            Task.FromResult(new TranslatedQuery(searched, query, 12));
    }

    [Fact]
    public async Task A_translated_query_retrieves_what_its_english_twin_retrieves()
    {
        const string english = "what is the procedure when a fee schedule is missing";
        const string bulgarian = "каква е процедурата когато липсва фий схема";

        await using var plain = corpus.Qdrant.Services(corpus.Collection, corpus.CorpusRoot);
        var baseline = await plain.GetRequiredService<DocumentSearchService>()
            .RankAsync(Advisor(), english, null, 5, plain.GetRequiredService<DocumentSearchService>().DefaultSettings, Ct);

        await using var normalising = corpus.Qdrant.Services(corpus.Collection, corpus.CorpusRoot,
            services: s => s.AddSingleton<IQueryTranslator>(new FixedTranslator(english)));
        var search = normalising.GetRequiredService<DocumentSearchService>();
        var translated = await search.RankAsync(Advisor(), bulgarian, null, 5, search.DefaultSettings, Ct);

        Assert.NotEmpty(baseline);
        Assert.Equal(baseline.Select(c => c.Chunk.ChunkId), translated.Select(c => c.Chunk.ChunkId));
    }

    [Fact]
    public async Task The_diagnostics_report_the_query_that_was_searched()
    {
        const string english = "how do I issue a billing credit to a client";
        const string bulgarian = "как се издава билинг кредит на клиент";

        await using var services = corpus.Qdrant.Services(corpus.Collection, corpus.CorpusRoot,
            services: s => s.AddSingleton<IQueryTranslator>(new FixedTranslator(english)));
        var search = services.GetRequiredService<DocumentSearchService>();
        var diagnostics = new SearchDiagnostics();

        await search.RankAsync(Advisor(), bulgarian, null, 5, search.DefaultSettings, Ct, diagnostics);

        Assert.Equal(english, diagnostics.Query["text"]!.GetValue<string>());
        Assert.Equal(bulgarian, diagnostics.Query["original"]!.GetValue<string>());
        Assert.True(diagnostics.Query["translated"]!.GetValue<bool>());
        Assert.NotNull(diagnostics.Query["translationMs"]);
        // The BM25 terms are the ones actually searched, which is what the monitor shows.
        Assert.Contains("credit", diagnostics.Query["terms"]!.ToJsonString());
    }

    [Fact]
    public async Task Without_normalisation_the_query_is_searched_as_written()
    {
        const string bulgarian = "каква е процедурата когато липсва фий схема";

        await using var services = corpus.Qdrant.Services(corpus.Collection, corpus.CorpusRoot,
            configure: v => v["Retrieval:NormalizeQueryLanguage"] = "false");
        var search = services.GetRequiredService<DocumentSearchService>();
        var diagnostics = new SearchDiagnostics();

        await search.RankAsync(Advisor(), bulgarian, null, 5, search.DefaultSettings, Ct, diagnostics);

        Assert.Equal(bulgarian, diagnostics.Query["text"]!.GetValue<string>());
        Assert.False(diagnostics.Query["translated"]!.GetValue<bool>());
        // Nothing was translated, so there is no duration to report.
        Assert.Null(diagnostics.Query["translationMs"]);
    }
}
