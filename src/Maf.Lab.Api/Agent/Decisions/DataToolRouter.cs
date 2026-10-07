using Maf.Lab.Plugins.Abstractions;

namespace Maf.Lab.Api.Agent.Decisions;

/// <summary>
/// Turns Jev's routing answer for a data question into a read call the turn can issue without asking the model — or
/// into the reason it cannot. The candidate tools and their descriptions come from the domains in use (introduce-plugins
/// decision 6); Jev names the tool, and the tool's domain takes the arguments from the question through its own fixed
/// patterns (<see cref="IDomainBehaviour.BindRead"/>). Anything those cannot pin down is left to the model, and a write
/// tool is never a candidate: its probability can only stop a route.
/// </summary>
public static class DataToolRouter
{
    private static DomainCatalogue Catalogue => DomainCatalogue.Current;

    /// <summary>The read tools a data question may be routed to, in the domains' order. Write tools are asked about only as a veto.</summary>
    public static IReadOnlyList<string> ReadTools => [.. Catalogue.All.SelectMany(d => d.ReadTools.Keys)];

    /// <summary>The domain each read tool belongs to: a question is routed only among the tools of its domains in scope.</summary>
    internal static IReadOnlyDictionary<string, string> ToolDomain =>
        Catalogue.All.SelectMany(d => d.ReadTools.Keys.Select(t => KeyValuePair.Create(t, d.Id))).DistinctBy(kv => kv.Key)
            .ToDictionary(StringComparer.Ordinal);

    /// <summary>The write tools, asked about only as a veto.</summary>
    public static IReadOnlyList<string> WriteTools => [.. Catalogue.All.SelectMany(d => d.WriteTools.Keys)];

    /// <summary>The first write tool, when any domain in use has one.</summary>
    public static string? WriteTool => WriteTools.FirstOrDefault();

    public static string ToolQuestionId(string tool) => $"tool_{tool}";

    /// <summary>What each tool does, in the words Jev reads beside the question (never the question itself).</summary>
    internal static IReadOnlyDictionary<string, string> ToolDescriptions =>
        Catalogue.All.SelectMany(d => d.ReadTools.Concat(d.WriteTools)).DistinctBy(kv => kv.Key).ToDictionary(StringComparer.Ordinal);

    /// <summary>
    /// The routing questions added to the intent request: a Noul per tool, the tool described beside it, and each domain's
    /// own closed questions (billing's run status).
    /// </summary>
    internal static IEnumerable<KeyValuePair<string, DecisionQuestion>> Questions() =>
        ToolDescriptions.Select(t => KeyValuePair.Create(ToolQuestionId(t.Key),
                (DecisionQuestion)new NoulQuestion(new ToolInstructions($"{t.Key}: {t.Value}", "To answer `user_question`, is it necessary to call `tool`?"))))
            .Concat(Catalogue.Behaviours.SelectMany(b => b.DataQuestions));

    /// <summary>Reads the engine's answers to the routing questions; null when any tool answer is missing.</summary>
    internal static RoutingAnswer? Read(IReadOnlyDictionary<string, DecisionAnswer> answers)
    {
        var tools = new Dictionary<string, double>();
        foreach (var tool in ToolDescriptions.Keys)
        {
            if (answers.GetValueOrDefault(ToolQuestionId(tool))?.Probability is not { } p || double.IsNaN(p))
            {
                return null;
            }
            tools[tool] = p;
        }
        var extra = new Dictionary<string, DecisionAnswer>(StringComparer.Ordinal);
        foreach (var key in Catalogue.Behaviours.SelectMany(b => b.DataQuestions.Keys))
        {
            if (answers.GetValueOrDefault(key) is { } a)
            {
                extra[key] = a;
            }
        }
        return new RoutingAnswer(tools, extra);
    }

    /// <summary>The route for a question Jev classified as data, or why there is none.</summary>
    public static (ToolRoute? Route, string? Reason) Route(string question, RoutingAnswer answer, IntentOptions o, DomainVerdict? domains = null,
        string? focus = null)
    {
        foreach (var write in WriteTools)
        {
            if (answer.Tools.GetValueOrDefault(write) >= 0.5)
            {
                return (null, $"a write is indicated ({answer.Tools[write]:F2})");
            }
        }
        // Only the tools of the domains Jev put the question in; with no verdict, every read tool, as before domains.
        var toolDomain = ToolDomain;
        var candidates = domains is { InScope.Count: > 0 } d
            ? ReadTools.Where(t => d.InScope.Contains(toolDomain[t])).ToList()
            : [.. ReadTools];
        if (candidates.Count == 0)
        {
            return (null, "no read tool belongs to a domain in scope");
        }
        var tool = candidates.OrderByDescending(t => answer.Tools.GetValueOrDefault(t)).First();
        var p = answer.Tools.GetValueOrDefault(tool);
        if (p < o.MinRouteProbability)
        {
            return (null, $"no read tool is clear ({tool} {p:F2})");
        }
        if (Catalogue.Behaviour(toolDomain[tool]) is not { } behaviour)
        {
            return (null, $"{tool}'s domain takes no arguments from a question");
        }
        var (arguments, reason) = behaviour.BindRead(tool, question, answer.Answers, focus, o.MinConfidence);
        return arguments is null ? (null, reason) : (new ToolRoute(tool, arguments, p), null);
    }
}

/// <summary>Instructions with the tool described beside the question, which names it in backticks.</summary>
internal sealed record ToolInstructions(string Tool, string Question);

/// <summary>Jev's answers to the routing questions: each tool's probability, and the domains' own questions' answers.</summary>
public sealed record RoutingAnswer(IReadOnlyDictionary<string, double> Tools, IReadOnlyDictionary<string, DecisionAnswer> Answers);

/// <summary>A read call to issue on the model's behalf, with the arguments taken from the question and Jev's probability.</summary>
public sealed record ToolRoute(string Tool, IReadOnlyDictionary<string, object?> Arguments, double Probability)
{
    public static ToolRoute From(DomainRoute route) => new(route.Tool, route.Arguments, route.Probability);
}
