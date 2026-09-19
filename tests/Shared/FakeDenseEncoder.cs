using Maf.Lab.Retrieval.Models;
using Maf.Lab.Retrieval.Sparse;

namespace Maf.Lab.TestSupport;

/// <summary>
/// Deterministic embedding by feature hashing of tokens and bigrams, L2-normalised. Texts that share words are close,
/// which is enough to exercise tenant scoping, fusion and ranking without a model server.
/// </summary>
public sealed class FakeDenseEncoder(IReadOnlyDictionary<string, int> dimensions, IReadOnlyDictionary<string, string> models) : IDenseEncoder
{
    public static FakeDenseEncoder Default() => new(
        new Dictionary<string, int> { ["dense_v1"] = 768, ["dense_v2"] = 384 },
        new Dictionary<string, string> { ["dense_v1"] = "nomic-embed-text", ["dense_v2"] = "all-minilm" });

    public int Calls;

    public Task<float[]> EmbedQueryAsync(string vectorName, string text, CancellationToken ct) => Task.FromResult(Embed(vectorName, text));

    public Task<IReadOnlyList<float[]>> EmbedDocumentsAsync(string vectorName, IReadOnlyList<string> texts, CancellationToken ct)
    {
        Interlocked.Add(ref Calls, texts.Count);
        return Task.FromResult<IReadOnlyList<float[]>>(texts.Select(t => Embed(vectorName, t)).ToList());
    }

    public string ModelVersion(string vectorName) => models[vectorName];

    public float[] Embed(string vectorName, string text)
    {
        var dims = dimensions[vectorName];
        var v = new float[dims];
        var tokens = Bm25Tokenizer.Tokenize(text).ToList();
        for (var i = 0; i < tokens.Count; i++)
        {
            Add(v, tokens[i], 1f);
            if (i + 1 < tokens.Count)
            {
                Add(v, tokens[i] + "_" + tokens[i + 1], 0.5f);
            }
        }
        var norm = MathF.Sqrt(v.Sum(x => x * x));
        if (norm == 0)
        {
            v[0] = 1;
            return v;
        }
        for (var i = 0; i < dims; i++)
        {
            v[i] /= norm;
        }
        return v;
    }

    private static void Add(float[] v, string feature, float weight)
    {
        var h = (uint)StableHash(feature);
        v[h % (uint)v.Length] += (h & 0x80000000) == 0 ? weight : -weight;
    }

    private static int StableHash(string s)
    {
        unchecked
        {
            var hash = (int)2166136261;
            foreach (var c in s)
            {
                hash = (hash ^ c) * 16777619;
            }
            return hash;
        }
    }
}
