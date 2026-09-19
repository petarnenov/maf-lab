using Maf.Lab.Domain.Feedback;
using Maf.Lab.Retrieval.Sparse;

namespace Maf.Lab.Api.Agent;

/// <summary>Production signals that route a turn into the review queue.</summary>
public static class TurnSignals
{
    public static List<string> Compute(Intent intent, int toolCalls, bool zeroResults, int answerChars, int sourceCount, int longAnswerChars)
    {
        var signals = new List<string>();
        if (IntentClassifier.IsHowWhy(intent) && toolCalls == 0)
        {
            signals.Add(TurnSignal.NoToolOnHowWhy);
        }
        if (zeroResults)
        {
            signals.Add(TurnSignal.ZeroRetrievalResults);
        }
        if (answerChars > longAnswerChars && sourceCount == 0)
        {
            signals.Add(TurnSignal.LongAnswerWithoutSources);
        }
        return signals;
    }

    /// <summary>The user asked nearly the same thing again shortly after — the previous answer probably missed.</summary>
    public static bool IsRephrase(string previous, string current, TimeSpan gap) =>
        gap <= TimeSpan.FromMinutes(15) && Jaccard(previous, current) >= 0.5;

    public static double Jaccard(string a, string b)
    {
        var x = Bm25Tokenizer.Tokenize(a).ToHashSet();
        var y = Bm25Tokenizer.Tokenize(b).ToHashSet();
        if (x.Count == 0 || y.Count == 0)
        {
            return 0;
        }
        return (double)x.Intersect(y).Count() / x.Union(y).Count();
    }
}
