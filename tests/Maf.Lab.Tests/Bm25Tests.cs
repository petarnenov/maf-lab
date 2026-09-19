using Maf.Lab.Retrieval.Sparse;
using System.Text.Json;

namespace Maf.Lab.Tests;

public class Bm25Tests
{
    [Fact]
    public void Tokenizer_lowercases_splits_and_drops_stopwords_keeping_ids()
    {
        var tokens = Bm25Tokenizer.Tokenize("What is the status of Run #4417? fee_schedule FS-REQUIRED").ToList();
        Assert.Equal(["status", "run", "4417", "fee", "schedule", "fs", "required"], tokens);
    }

    [Fact]
    public void Idf_is_higher_for_rarer_terms_and_never_negative()
    {
        var model = Bm25Model.Empty();
        model.Rebuild(["fee schedule missing", "fee schedule tiered", "fee proration rules", "custodian mismatch"]);
        model.TryGetTermId("fee", out var fee);
        model.TryGetTermId("custodian", out var custodian);

        Assert.True(model.Idf(custodian) > model.Idf(fee));
        Assert.True(model.Idf(fee) > 0);
        Assert.Equal(4, model.DocumentCount);
    }

    [Fact]
    public void Dot_product_of_query_and_document_vectors_ranks_matching_document_first()
    {
        var model = Bm25Model.Empty();
        string[] docs = ["procedure when a fee schedule is missing", "custodian fee deduction reconciliation", "household aggregation rules"];
        model.Rebuild(docs);
        var docVectors = docs.Select(d => Bm25Encoder.EncodeDocument(model, d)).ToList();
        var query = Bm25Encoder.EncodeQuery(model, "missing fee schedule");

        var scores = docVectors.Select(d => Dot(query, d)).ToList();
        Assert.Equal(0, scores.IndexOf(scores.Max()));
        Assert.Equal(0, scores[2]);
    }

    [Fact]
    public void Unknown_query_terms_are_dropped()
    {
        var model = Bm25Model.Empty();
        model.Rebuild(["fee schedule"]);
        Assert.True(Bm25Encoder.EncodeQuery(model, "zebra xylophone").IsEmpty);
    }

    [Fact]
    public void Model_round_trips_through_json_with_stable_term_ids()
    {
        var model = Bm25Model.Empty();
        model.Rebuild(["alpha beta", "beta gamma"]);
        var copy = JsonSerializer.Deserialize<Bm25Model>(JsonSerializer.Serialize(model))!;

        Assert.Equal(model.Terms, copy.Terms);
        Assert.Equal(model.DocumentCount, copy.DocumentCount);
        copy.TryGetTermId("gamma", out var id);
        Assert.Equal(model.Idf(id), copy.Idf(id), 6);

        copy.Rebuild(["delta alpha"]);
        Assert.Equal(model.Terms["alpha"], copy.Terms["alpha"]);
    }

    private static double Dot(SparseVectorData a, SparseVectorData b)
    {
        var map = b.Indices.Zip(b.Values).ToDictionary(x => x.First, x => x.Second);
        return a.Indices.Zip(a.Values).Sum(x => map.TryGetValue(x.First, out var v) ? x.Second * v : 0);
    }
}
