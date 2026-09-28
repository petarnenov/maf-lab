namespace Maf.Lab.Api.Agent;

public enum Intent
{
    /// <summary>How / why / procedure / explain — documentation.</summary>
    Procedural,
    /// <summary>Procedural question about a specific run ("why did run 4417 fail").</summary>
    Mixed,
    /// <summary>Current data: run status, lists of runs.</summary>
    Data,
    /// <summary>Greetings, thanks, closings.</summary>
    ChitChat,
    Other,
}

/// <summary>
/// What the classifier concluded. <paramref name="Choice"/>, <paramref name="Probabilities"/> and
/// <paramref name="Confidence"/> are Jev's answer as given, kept even when it was not acted on; <paramref name="Reason"/>
/// says why the turn proceeded with no recognised intent (low confidence, outside the domain, timeout, rejection, no key)
/// and is null when the answer was used. <paramref name="InDomain"/> is Jev's probability that the question is about
/// the documented domain. With tool routing on, <paramref name="Routing"/> is Jev's answer to the routing questions,
/// <paramref name="Route"/> the read call to issue on the model's behalf, and <paramref name="RouteReason"/> why there is
/// none.
/// </summary>
public readonly record struct IntentDecision(
    Intent Intent,
    string? Choice = null,
    IReadOnlyDictionary<string, double>? Probabilities = null,
    double? Confidence = null,
    string? Model = null,
    double? DurationMs = null,
    string? Reason = null,
    double? InDomain = null,
    Jev.RoutingAnswer? Routing = null,
    Jev.ToolRoute? Route = null,
    string? RouteReason = null);

/// <summary>Classifies the question of a turn before the first model call, in any language.</summary>
public interface IIntentClassifier
{
    Task<IntentDecision> ClassifyAsync(string question, CancellationToken ct);
}

/// <summary>What an intent means for the turn. Only Procedural and Mixed force search_documents, and only for that turn.</summary>
public static class IntentClassifier
{
    public static bool ForcesRetrieval(Intent intent) => intent is Intent.Procedural or Intent.Mixed;

    public static bool IsHowWhy(Intent intent) => intent is Intent.Procedural or Intent.Mixed;
}
