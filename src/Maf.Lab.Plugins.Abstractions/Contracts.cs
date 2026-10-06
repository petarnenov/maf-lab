using Maf.Lab.Domain.Tenancy;
using Microsoft.Agents.AI;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Maf.Lab.Plugins.Abstractions;

/// <summary>
/// A plugin's identity, and nothing else (interface segregation, introduce-plugins "Principles and patterns"). What a
/// plugin gives the core is the set of <c>IContributes*</c> interfaces its class implements; the composition root asks
/// by type, so it never calls a method a plugin does not have and a reviewer sees a plugin's whole reach from its
/// declaration. The name is the plugin folder's, and so its manifest's.
/// </summary>
public interface IMafPlugin
{
    string Name { get; }
}

/// <summary>Services a plugin registers; called only for an installed plugin, in dependency order.</summary>
public interface IContributesServices
{
    void ConfigureServices(IServiceCollection services, IConfiguration configuration);
}

/// <summary>Routes a plugin maps; they go in its own route group, behind the core's gate (decision 2).</summary>
public interface IContributesEndpoints
{
    void MapEndpoints(IMafEndpoints endpoints);
}

/// <summary>A plugin's tables in the one store, created only while it is installed.</summary>
public interface IContributesModel
{
    void ConfigureModel(ModelBuilder model);
}

/// <summary>Behaviour of a domain that data cannot express (decision 6): the Strategy for one domain.</summary>
public interface IContributesDomainBehaviour
{
    IDomainBehaviour Behaviour { get; }
}

/// <summary>An observer of every turn (decision 7): how the monitor sees the full trace.</summary>
public interface IContributesTurnObserver
{
    ITurnObserver Observer { get; }
}

/// <summary>A plugin's long work, so removing the plugin can stop it first through its own store (decision 2).</summary>
public interface IContributesOpenWork
{
    Task<IReadOnlyList<OpenWorkItem>> ListOpenAsync(CancellationToken ct);

    /// <summary>Marks every open item cancelled in the store that owns it; the workers watching that store stop.</summary>
    Task CancelAllAsync(CancellationToken ct);
}

/// <summary>One item of open work: its kind, id and state, never content.</summary>
public sealed record OpenWorkItem(string Kind, string Id, string State);

/// <summary>The brand port's contribution (used by add-white-labeling). At most one contributor may be installed.</summary>
public interface IContributesBrandProvider
{
    IBrandProvider Provider { get; }
}

/// <summary>
/// Where a plugin maps its routes: a route group the core gates (404 while the plugin is not installed), and the one
/// way a plugin serves an AG-UI agent, which the core implements next to its AG-UI mappings (agui-protocol-only).
/// </summary>
public interface IMafEndpoints
{
    /// <summary>The plugin's route group. Everything mapped here is behind the core's gate.</summary>
    IEndpointRouteBuilder Routes { get; }

    /// <summary>An AG-UI agent for signed-in callers, with the core's official mappings; no AG-UI type in the plugin.</summary>
    IEndpointConventionBuilder MapPluginAgent(string pattern, AIAgent agent);
}

/// <summary>
/// A domain's descriptor: the data the core reads to route, search, guard and render (decision 6). The core reads every
/// domain the same way; none is first, default or special (decision 5g).
/// </summary>
/// <param name="Id">The domain's id, as traces, stats and the decision engine's answers name it.</param>
/// <param name="SearchTool">The domain's documentation search: what a forcing intent calls in it.</param>
/// <param name="Tools">The allow-list of its server's tools; empty offers every tool the server lists.</param>
/// <param name="GraphTools">Its graph tools (add-neo4j-graph).</param>
/// <param name="RoutingQuestions">The manifest's routing questions, as written; the first describes the domain when
/// <see cref="Description"/> is not set.</param>
/// <param name="GuardContext">Which content battery screens its search results: <see cref="GuardContexts.Documents"/> or
/// <see cref="GuardContexts.Code"/>.</param>
/// <param name="CardTypes">Tool name → the AG-UI activity type its result travels as.</param>
public sealed record DomainDescriptor(
    string Id,
    string? SearchTool,
    IReadOnlyList<string> Tools,
    IReadOnlyList<string> GraphTools,
    IReadOnlyList<string> RoutingQuestions,
    string? GuardContext,
    IReadOnlyDictionary<string, string> CardTypes)
{
    /// <summary>The key of the "is the question in this domain?" question in the one routing request.</summary>
    public string QuestionKey { get; init; } = $"in_{Id}";

    /// <summary>What the domain covers, in the words the domain question reads beside the question.</summary>
    public string? Description { get; init; }

    /// <summary>Where the domain sits in traces and in the assembled prompt; lower first, then by id.</summary>
    public int Order { get; init; } = 100;

    /// <summary>The read tools a data question may be routed to, each described for the routing question.</summary>
    public IReadOnlyDictionary<string, string> ReadTools { get; init; } = new Dictionary<string, string>();

    /// <summary>Write tools, asked about only as a veto on routing: a write is never routed.</summary>
    public IReadOnlyDictionary<string, string> WriteTools { get; init; } = new Dictionary<string, string>();

    /// <summary>A question whose primary domain this is searches it whatever its intent but small talk.</summary>
    public bool SearchAnyIntent { get; init; }

    /// <summary>A tool offered only while the named plugin (or capability) is in use: tool → its name.</summary>
    public IReadOnlyDictionary<string, string> ToolRequires { get; init; } = new Dictionary<string, string>();

    /// <summary>The domain's part of the system prompt (Markdown with section markers), reviewed with the domain.</summary>
    public string? PromptFragment { get; init; }

    /// <summary>The domain named for a user, by language ("en", "bg"), as the out-of-scope reply lists it.</summary>
    public IReadOnlyDictionary<string, string> ScopeSummary { get; init; } = new Dictionary<string, string>();

    /// <summary>Every tool this descriptor names: search, reads, writes and graph tools, and the allow-list.</summary>
    public IEnumerable<string> NamedTools =>
        (SearchTool is null ? [] : new[] { SearchTool }).Concat(ReadTools.Keys).Concat(WriteTools.Keys).Concat(GraphTools).Concat(Tools);
}

/// <summary>The content guard's batteries a domain's search results are screened with.</summary>
public static class GuardContexts
{
    /// <summary>Document excerpts and records.</summary>
    public const string Documents = "documents";

    /// <summary>Source-code snippets, which legitimately speak about AI assistants.</summary>
    public const string Code = "code";
}

/// <summary>A decision engine's answer to one routing question, as the core passes it to a domain.</summary>
/// <param name="Choice">A choice question's answer.</param>
/// <param name="Confidence">How sure the engine is of <paramref name="Choice"/>.</param>
/// <param name="Probability">A yes/no question's probability of yes.</param>
/// <param name="Probabilities">Each option's probability, for a choice question.</param>
public sealed record DecisionAnswer(string? Choice, double? Confidence, double? Probability, IReadOnlyDictionary<string, double>? Probabilities);

/// <summary>A tool call the turn issues on the model's behalf, its arguments taken from the question by code.</summary>
public sealed record DomainRoute(string Tool, IReadOnlyDictionary<string, object?> Arguments, double Probability);

/// <summary>
/// A domain's behaviour that data cannot express (decision 6): the Strategy for one domain. Every member has a neutral
/// default, so a domain implements only what it needs. Questions are closed and fixed per deployment, never taken from a
/// request or the model (docs/rules/jev-usage.md); arguments are taken from the question by code, never by the engine.
/// </summary>
public interface IDomainBehaviour
{
    /// <summary>The domain this behaviour belongs to.</summary>
    string Domain { get; }

    /// <summary>Closed questions this domain adds to the routing request beside its read tools, keyed, in the engine's form.</summary>
    IReadOnlyDictionary<string, object> DataQuestions => new Dictionary<string, object>();

    /// <summary>The arguments of a read tool the routing chose, taken from the question; or why there are none.</summary>
    (IReadOnlyDictionary<string, object?>? Arguments, string? Reason) BindRead(string tool, string question,
        IReadOnlyDictionary<string, DecisionAnswer> answers, string? focus, double minConfidence) => (null, $"{tool} has no binding");

    /// <summary>Read calls a question with this intent needs beside its documentation search.</summary>
    IReadOnlyList<DomainRoute> Alongside(string intent, string question, double confidence) => [];

    /// <summary>A question asked when this domain may be the primary one: what the question needs, beside its search.</summary>
    KeyValuePair<string, object>? PrimaryRouteQuestion => null;

    /// <summary>The call a question whose primary domain this is starts with instead of the search; or why there is none.</summary>
    (DomainRoute? Route, string? Reason) PrimaryRoute(string question, string intent, DecisionAnswer? answer, double minConfidence) =>
        (null, "no primary route");

    /// <summary>A one-line summary of a tool's structured result, for the trace; null when the core's default applies.</summary>
    string? Summarize(string tool, System.Text.Json.JsonElement result) => null;

    /// <summary>Whether this domain owns the conversation's focus (an entity the conversation is about).</summary>
    bool OwnsFocus => false;

    /// <summary>Whether a value is a well-formed focus id.</summary>
    bool IsFocusId(string id) => false;

    /// <summary>The focus ids a question names, in order, distinct.</summary>
    IReadOnlyList<string> FocusIds(string question) => [];

    /// <summary>The focus ids a stored card's content offered to the user.</summary>
    IEnumerable<string> FocusIdsIn(System.Text.Json.JsonElement cardContent) => [];

    /// <summary>The focus a tool's result moves the conversation to; null when it moves none.</summary>
    string? FocusFrom(string tool, System.Text.Json.JsonElement result) => null;

    /// <summary>Whether a tool reads the entity in focus when the question names none.</summary>
    bool NeedsFocus(string tool) => false;

    /// <summary>The note the model gets with a focus.</summary>
    string FocusNote(string focus) => "";

    /// <summary>Said right before the question on the turn the user cleared the focus.</summary>
    string ClearedFocusNote => "";

    /// <summary>What the model is told when a tool needing the focus was not called because the user cleared it.</summary>
    string ClearedFocusToolNote => "";
}

/// <summary>One event of a turn, as an observer sees it: its kind and its structured payload, never message content
/// unless the observer is the dev-only monitor, which the core never installs in stage or prod (decision 5d).</summary>
public sealed record TurnEvent(string RunId, string Kind, System.Text.Json.JsonElement Payload);

/// <summary>An observer of turns (decision 7); the core registers none of its own.</summary>
public interface ITurnObserver
{
    Task OnEventAsync(TurnEvent turnEvent, CancellationToken ct);
}

/// <summary>A conversation as the list shows it (decision 5y).</summary>
public sealed record ConversationSummary(string Id, string Title, DateTimeOffset CreatedAt, DateTimeOffset LastActivityAt, int TurnCount);

/// <summary>A page of the caller's own conversations, and where the next page starts.</summary>
public sealed record ConversationPage(IReadOnlyList<ConversationSummary> Items, string? NextCursor);

/// <summary>
/// The conversation store the core owns (decision 5y): the list plugin reaches storage, titles and the audit only through
/// it. Every method acts on the principal's own conversations; none takes a tenant or a user.
/// </summary>
public interface IConversationStore
{
    Task<ConversationPage> PageAsync(Principal principal, string? search, int limit, string? cursor, CancellationToken ct);

    /// <summary>False when the conversation is not the principal's or does not exist.</summary>
    Task<bool> RenameAsync(Principal principal, string conversationId, string title, CancellationToken ct);

    /// <summary>A soft delete, recorded as <c>conversation.delete</c> in the audit; false when not found.</summary>
    Task<bool> DeleteAsync(Principal principal, string conversationId, CancellationToken ct);
}

/// <summary>A brand, as add-white-labeling defines it in full; the port exists now so the seam list is closed.</summary>
public sealed record Brand(string Name);

/// <summary>The brand port (add-white-labeling): null means the installation brand applies.</summary>
public interface IBrandProvider
{
    Task<Brand?> ForAsync(Principal principal, CancellationToken ct);
}
