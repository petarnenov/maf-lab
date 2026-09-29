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
/// the documented domain. <paramref name="Screen"/> holds the answers to the prompt-screening questions that rode in the
/// same request (injection-defense), kept whether or not the intent was used; null when Jev gave none. With tool routing
/// on, <paramref name="Routing"/> is Jev's answer to the routing questions, <paramref name="Route"/> the read call to
/// issue on the model's behalf, and <paramref name="RouteReason"/> why there is none. <paramref name="Domains"/> is Jev's
/// verdict on which domains the question belongs to — null when Jev gave no usable domain answer.
/// <paramref name="OutsideDomains"/> is true when Jev put the question in no domain and did not read it as small talk; it
/// is never true on a missing answer, so a failed classification never refuses a question.
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
    IReadOnlyDictionary<string, double>? Screen = null,
    Jev.RoutingAnswer? Routing = null,
    Jev.ToolRoute? Route = null,
    string? RouteReason = null,
    DomainVerdict? Domains = null,
    bool OutsideDomains = false);

/// <summary>
/// Which domains a question belongs to, as Jev answered: each domain's probability, the floor a domain must reach to be
/// in scope, and the domains in scope, most probable first. Two or more in scope and the question crosses the boundary.
/// </summary>
public sealed record DomainVerdict(IReadOnlyDictionary<string, double> Probabilities, double ScopeFloor, IReadOnlyList<string> InScope)
{
    public bool Crossing => InScope.Count > 1;

    /// <summary>The most probable domain in scope; null when none is.</summary>
    public string? Primary => InScope.Count > 0 ? InScope[0] : null;

    /// <summary>The highest probability of any domain: what the domain gate compares with its floor.</summary>
    public double Highest => Probabilities.Count == 0 ? 0 : Probabilities.Values.Max();

    /// <summary>
    /// Every domain at or above <paramref name="scopeFloor"/>; when none is, the most probable domain alone provided it
    /// reaches <paramref name="gateFloor"/> — the gate that lets a forcing intent act at all.
    /// </summary>
    public static DomainVerdict From(IReadOnlyDictionary<string, double> probabilities, double scopeFloor, double gateFloor)
    {
        var ranked = probabilities.OrderByDescending(p => p.Value).ThenBy(p => Agent.Domains.All.ToList().IndexOf(p.Key)).ToList();
        var inScope = ranked.Where(p => p.Value >= scopeFloor).Select(p => p.Key).ToList();
        if (inScope.Count == 0 && ranked.Count > 0 && ranked[0].Value >= gateFloor && ranked[0].Value > 0)
        {
            inScope.Add(ranked[0].Key);
        }
        return new DomainVerdict(probabilities, scopeFloor, inScope);
    }
}

/// <summary>Classifies the question of a turn before the first model call, in any language.</summary>
public interface IIntentClassifier
{
    /// <param name="focusAccountId">
    /// The conversation's account in focus (add-focus-state). It never changes what is asked; routing uses it when a
    /// portfolio question names no account.
    /// </param>
    Task<IntentDecision> ClassifyAsync(string question, CancellationToken ct, string? focusAccountId = null);
}

/// <summary>What an intent means for the turn. Only Procedural and Mixed force search_documents, and only for that turn.</summary>
public static class IntentClassifier
{
    public static bool ForcesRetrieval(Intent intent) => intent is Intent.Procedural or Intent.Mixed;

    public static bool IsHowWhy(Intent intent) => intent is Intent.Procedural or Intent.Mixed;
}

/// <summary>
/// The fixed reply to a question outside every domain (refuse-off-domain-questions). Like the guard's refusal it repeats
/// nothing of the question, and it says what the assistant does answer, so the user can ask that instead.
/// </summary>
public static class OutOfScope
{
    public const string ReplyEnglish =
        "I can only help with your firm's billing (fees, fee schedules, billing runs, fee adjustments) and portfolios "
        + "(holdings, model portfolios, drift, rebalancing, quarter-end AUM). Please ask about one of those.";

    public const string ReplyBulgarian =
        "Мога да помагам само с таксуването на Вашата фирма (такси, тарифи, билинг цикли, корекции на такси) и с "
        + "портфейлите (позиции, моделни портфейли, отклонение, ребалансиране, AUM към края на тримесечието). "
        + "Моля, задайте въпрос по една от тези теми.";

    /// <summary>In Bulgarian when the question is written in Cyrillic, as the guard's refusal is.</summary>
    public static string Reply(string question) =>
        question.Any(c => c is >= 'Ѐ' and <= 'ӿ') ? ReplyBulgarian : ReplyEnglish;
}
