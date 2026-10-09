using Maf.Lab.Plugins.Abstractions;
using Maf.Lab.Retrieval.Configuration;
using Maf.Lab.Retrieval.Store;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Maf.Lab.Retrieval.Rerank;

/// <summary>
/// The decision engine's answer about one search's candidates: for each of the first judged candidates, in fused order, the probability
/// that it addresses the subject of the query — or, when there is no usable answer, why. Null <see cref="Scores"/> means
/// the judge did not answer and the search must proceed as if it had not been asked.
/// </summary>
public sealed record RelevanceJudgement(IReadOnlyList<double>? Scores, string? Reason, string? Model, double DurationMs)
{
    public double? Max => Scores is { Count: > 0 } s ? s.Max() : null;
}

/// <summary>Judges already tenant-scoped candidates. Input must only ever come from TenantScopedSearch.</summary>
public interface IRelevanceJudge
{
    Task<RelevanceJudgement> JudgeAsync(string query, IReadOnlyList<ScoredChunk> candidates, CancellationToken ct);
}

/// <summary>
/// One decision request per search: the query and the first fused candidates as named data in the state, and one Noul per
/// candidate asking whether it addresses the subject of the query. The wording is the planning probe's, kept verbatim
/// because its numbers are the evidence for the gate. Anything but a complete answer — a timeout, an error status, a
/// missing or non-numeric probability, no key — is a failure with a reason, logged without the query or passage text.
/// </summary>
public sealed class DecisionRelevanceJudge(IDecisionEngine engine, IOptions<RetrievalOptions> options, ILogger<DecisionRelevanceJudge> logger) : IRelevanceJudge
{
    internal static string Question(int index) => $"Does `passages[{index}]` address the subject of `query`?";

    internal static string QuestionId(int index) => $"p{index}";

    public async Task<RelevanceJudgement> JudgeAsync(string query, IReadOnlyList<ScoredChunk> candidates, CancellationToken ct)
    {
        var o = options.Value;
        var judged = candidates.Take(Math.Max(0, o.RelevanceCandidates)).ToList();
        if (judged.Count == 0)
        {
            return new RelevanceJudgement([], null, null, 0);
        }
        if (o.RelevanceTimeoutSeconds <= 0)
        {
            return Failed("disabled", judged.Count, 0);
        }

        var state = new RelevanceState(query, judged.Select(c => new RelevancePassage(c.Chunk.SectionPath, Trim(c.Chunk.Text, o.RelevancePassageChars))).ToList());
        var questions = Enumerable.Range(0, judged.Count)
            .ToDictionary(QuestionId, i => (DecisionQuestion)new NoulQuestion(Question(i)));
        var outcome = await engine.DecideAsync(state, questions, TimeSpan.FromSeconds(o.RelevanceTimeoutSeconds), ct);
        Maf.Lab.Hosting.LabTelemetry.Instruments.RecordDecision("relevance", outcome.Usage?.InputTokens);
        if (outcome.Answers is not { } answers)
        {
            return Failed(outcome.Failure ?? "no answer", judged.Count, outcome.DurationMs);
        }

        var scores = new List<double>(judged.Count);
        for (var i = 0; i < judged.Count; i++)
        {
            if (answers.GetValueOrDefault(QuestionId(i))?.Probability is not { } p || double.IsNaN(p))
            {
                return Failed("incomplete answer", judged.Count, outcome.DurationMs, outcome.Engine);
            }
            scores.Add(Math.Clamp(p, 0, 1));
        }
        return new RelevanceJudgement(scores, null, outcome.Engine, outcome.DurationMs);
    }

    private RelevanceJudgement Failed(string reason, int count, double ms, string? model = null)
    {
        // Never the query or a passage: no message content in logs.
        logger.LogWarning("Relevance judge unavailable ({Reason}); the search proceeds ungated in fused order for {CandidateCount} candidates",
            reason, count);
        return new RelevanceJudgement(null, reason, model ?? engine.Engine, ms);
    }

    private static string Trim(string s, int max) => max <= 0 || s.Length <= max ? s : s[..max];
}

/// <summary>What the engine reads: the query as searched and the passages, as named fields — data, never instructions.</summary>
internal sealed record RelevanceState(string Query, IReadOnlyList<RelevancePassage> Passages);

internal sealed record RelevancePassage(string Section, string Text);
