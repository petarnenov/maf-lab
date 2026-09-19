namespace Maf.Lab.Retrieval.Sparse;

public sealed record SparseVectorData(uint[] Indices, float[] Values)
{
    public bool IsEmpty => Indices.Length == 0;
}

/// <summary>Turns text into sparse vectors whose dot product is the BM25 score.</summary>
public static class Bm25Encoder
{
    /// <summary>Document side: saturated term frequency, length-normalised. Adds unseen terms to the vocabulary.</summary>
    public static SparseVectorData EncodeDocument(Bm25Model model, string text)
    {
        var tokens = Bm25Tokenizer.Tokenize(text).ToList();
        if (tokens.Count == 0)
        {
            return new SparseVectorData([], []);
        }
        var lengthNorm = 1 - Bm25Model.B + Bm25Model.B * tokens.Count / model.AverageLength;
        var weights = tokens
            .GroupBy(t => t, StringComparer.Ordinal)
            .Select(g =>
            {
                var tf = g.Count();
                var weight = tf * (Bm25Model.K1 + 1) / (tf + Bm25Model.K1 * lengthNorm);
                return (Id: model.GetOrAddTermId(g.Key), Weight: (float)weight);
            })
            .OrderBy(x => x.Id)
            .ToArray();
        return new SparseVectorData(weights.Select(w => w.Id).ToArray(), weights.Select(w => w.Weight).ToArray());
    }

    /// <summary>Query side: IDF weight per known query term. Unknown terms cannot match and are dropped.</summary>
    public static SparseVectorData EncodeQuery(Bm25Model model, string text)
    {
        var weights = Bm25Tokenizer.Tokenize(text)
            .Distinct(StringComparer.Ordinal)
            .Select(t => model.TryGetTermId(t, out var id) ? (Id: id, Weight: (float)model.Idf(id)) : (Id: uint.MaxValue, Weight: 0f))
            .Where(x => x.Id != uint.MaxValue && x.Weight > 0)
            .OrderBy(x => x.Id)
            .ToArray();
        return new SparseVectorData(weights.Select(w => w.Id).ToArray(), weights.Select(w => w.Weight).ToArray());
    }
}
