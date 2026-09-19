using System.Text.Json.Serialization;

namespace Maf.Lab.Retrieval.Sparse;

/// <summary>
/// Persisted BM25 state: stable term ids, document frequencies and corpus length statistics.
/// Document vectors store only the TF-saturation part; queries carry IDF weights, so IDF can be
/// refreshed without re-encoding documents.
/// </summary>
public sealed class Bm25Model
{
    public const double K1 = 1.2;
    public const double B = 0.75;

    [JsonInclude] public int Version { get; private set; }
    [JsonInclude] public Dictionary<string, uint> Terms { get; private set; } = new(StringComparer.Ordinal);
    [JsonInclude] public Dictionary<uint, int> DocumentFrequency { get; private set; } = new();
    [JsonInclude] public int DocumentCount { get; private set; }
    [JsonInclude] public long TotalLength { get; private set; }

    public double AverageLength => DocumentCount == 0 ? 1 : (double)TotalLength / DocumentCount;

    public static Bm25Model Empty() => new();

    /// <summary>Rebuilds statistics from the full set of chunk texts. Existing term ids are kept stable.</summary>
    public void Rebuild(IEnumerable<string> chunkTexts)
    {
        DocumentFrequency.Clear();
        DocumentCount = 0;
        TotalLength = 0;
        foreach (var text in chunkTexts)
        {
            var tokens = Bm25Tokenizer.Tokenize(text).ToList();
            DocumentCount++;
            TotalLength += tokens.Count;
            foreach (var term in tokens.Distinct(StringComparer.Ordinal))
            {
                var id = GetOrAddTermId(term);
                DocumentFrequency[id] = DocumentFrequency.GetValueOrDefault(id) + 1;
            }
        }
        Version++;
    }

    public uint GetOrAddTermId(string term)
    {
        if (!Terms.TryGetValue(term, out var id))
        {
            id = (uint)Terms.Count;
            Terms[term] = id;
        }
        return id;
    }

    public bool TryGetTermId(string term, out uint id) => Terms.TryGetValue(term, out id);

    /// <summary>Okapi BM25 IDF with the +1 floor so frequent terms never go negative.</summary>
    public double Idf(uint termId)
    {
        var df = DocumentFrequency.GetValueOrDefault(termId);
        return Math.Log(1 + (DocumentCount - df + 0.5) / (df + 0.5));
    }
}
