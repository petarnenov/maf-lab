using Maf.Lab.Plugins.Abstractions;

namespace Maf.Lab.Plugins.Code;

/// <summary>
/// The codebase domain's behaviour: what a codebase question needs, asked beside the others, and the code-graph call a
/// structural question starts with instead of the search (route-structural-code-questions).
/// </summary>
public sealed class CodebaseBehaviour : IDomainBehaviour
{
    public string Domain => CodePlugin.DomainId;

    public KeyValuePair<string, object>? PrimaryRouteQuestion => CodeToolRouter.Question();

    public (DomainRoute? Route, string? Reason) PrimaryRoute(string question, string intent, DecisionAnswer? answer, double minConfidence) =>
        CodeToolRouter.Route(question, answer, intent, minConfidence);

    /// <summary>A codebase search found nothing: it says so in the code's words.</summary>
    public string? Summarize(string tool, System.Text.Json.JsonElement result) => null;
}
