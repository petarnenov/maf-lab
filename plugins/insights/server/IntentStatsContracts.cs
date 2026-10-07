namespace Maf.Lab.Plugins.Insights;

/// <summary>What happened to one classification: Jev's answer was acted on, answered but overruled, or never usable.</summary>
public static class IntentOutcome
{
    public const string Used = "used";
    public const string Gated = "gated";
    public const string Failed = "failed";
}

/// <summary>The configuration every number is judged against, as the running service has it.</summary>
public sealed record IntentStatsSettings(string Model, double MinConfidence, double MinInDomain, double TimeoutSeconds);

/// <param name="Classified">Jev events in the window: used + gated + failed.</param>
/// <param name="Forced">Turns whose intent forced retrieval.</param>
/// <param name="ExcludedEvents">Intent events in the window written by an earlier classifier, left out of everything else.</param>
public sealed record IntentStatsTotals(int Classified, int Used, int Gated, int Failed, int Forced, int ExcludedEvents);

/// <summary>
/// Counts along the edges of the classification pipeline: answered → confident → forcing intent → in the domain →
/// forced. Each count is the number of events that left the previous step by that edge.
/// </summary>
public sealed record IntentPipelineCounts(
    int Classified,
    int Failed,
    int Answered,
    int UnknownChoice,
    int BelowConfidence,
    int NotForcingIntent,
    int ForcingIntent,
    int OutsideDomain,
    int Forced);

/// <param name="Label">The reason with its number removed (a rejection keeps its status).</param>
public sealed record IntentReasonCount(string Outcome, string Label, int Count);

/// <param name="Choice">Jev's raw choice, or <c>none</c> when there was no answer.</param>
/// <param name="Intent">The intent the turn proceeded with.</param>
public sealed record IntentChoiceCount(string Choice, string Intent, int Count);

/// <summary>One time bucket. Latencies are null when no call in the bucket was timed.</summary>
public sealed record IntentTimelineBucket(DateTimeOffset Start, int Used, int Gated, int Failed, int TimedOut, double? P50Ms, double? P90Ms);

/// <summary>One bin of a probability histogram, [From, To), the last bin closed at 1.</summary>
public sealed record IntentProbabilityBin(double From, double To, int Used, int Gated);

/// <summary>One classification Jev answered, as numbers only.</summary>
public sealed record IntentPoint(double Confidence, double InDomain, string Outcome, string Choice);

/// <param name="Mean">Mean probability Jev gave this intent over every answer that carried probabilities.</param>
/// <param name="Chosen">How often it was Jev's choice.</param>
public sealed record IntentMeanProbability(string Intent, double Mean, int Chosen);

/// <param name="ToMs">Exclusive upper bound; null for the overflow bin at and past the timeout.</param>
public sealed record IntentLatencyBin(double FromMs, double? ToMs, int Count);

public sealed record IntentLatency(int Count, double? P50, double? P90, double? P99, double? Max, IReadOnlyList<IntentLatencyBin> Bins);

public sealed record IntentModelCount(string Model, int Count);

/// <summary>
/// Aggregates of the intent classifier's answers for one firm over one window. Numbers only: no question, answer or
/// identifier of a turn, conversation or user.
/// </summary>
public sealed record IntentStatsReport(
    string Window,
    DateTimeOffset From,
    DateTimeOffset To,
    int BucketMinutes,
    IntentStatsSettings Settings,
    IntentStatsTotals Totals,
    IntentPipelineCounts Pipeline,
    IReadOnlyList<IntentReasonCount> Reasons,
    IReadOnlyList<IntentChoiceCount> Choices,
    IReadOnlyList<IntentTimelineBucket> Timeline,
    IReadOnlyList<IntentProbabilityBin> Confidence,
    IReadOnlyList<IntentProbabilityBin> InDomain,
    IReadOnlyList<IntentPoint> Points,
    IReadOnlyList<IntentMeanProbability> MeanProbabilities,
    IntentLatency Latency,
    IReadOnlyList<IntentModelCount> Models);
