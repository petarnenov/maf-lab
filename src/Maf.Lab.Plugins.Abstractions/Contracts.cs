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

/// <summary>
/// An observer of every turn (decision 7): how the monitor sees the full trace. A factory method, so the observer takes
/// its dependencies from the composed services; created once.
/// </summary>
public interface IContributesTurnObserver
{
    ITurnObserver CreateObserver(IServiceProvider services);
}

/// <summary>A plugin's long work, so removing the plugin can stop it first through its own store (decision 2).</summary>
public interface IContributesOpenWork
{
    Task<IReadOnlyList<OpenWorkItem>> ListOpenAsync(CancellationToken ct);

    /// <summary>Marks every open item cancelled in the store that owns it; the workers watching that store stop.</summary>
    Task CancelAllAsync(CancellationToken ct);
}

/// <summary>
/// A read port onto the installed set (introduce-plugins 5.2): what a plugin may ask the core about installed plugins,
/// without reaching the core's catalogue. Implemented by the core, re-read as the installed set changes.
/// </summary>
public interface IInstalledPlugins
{
    bool IsInstalled(string plugin);

    /// <summary>
    /// The MCP endpoint the core connects to for a plugin's server, by the one precedence rule the agent uses: a
    /// configured <c>Agent:Servers:&lt;plugin&gt;</c> first (the twelve-factor override `make dev` needs), then the first
    /// remote of the plugin's server.json; null when it has neither or is not installed.
    /// </summary>
    string? McpEndpoint(string plugin);
}

/// <summary>
/// The core's rule for who may read a turn, for a plugin that serves something of it (the monitor's kept trace): the
/// turn's owner, or a tenant admin of its tenant while the turn is in the review queue. The caller is the request's
/// principal; the rule stays the core's, as the turn's data does.
/// </summary>
public interface ITurnAccess
{
    Task<bool> MayReadAsync(string turnId, CancellationToken ct);
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

/// <summary>
/// An observer of turns (decision 7; the shape of DiagnosticListener.IsEnabled / ILogger.IsEnabled): it declares by
/// name what it wants, and the core checks before it pays for building it — the full model capture
/// (<see cref="Maf.Lab.Domain.Tracing.TraceKinds.ModelRequest"/>), the prompt's tool schemas
/// (<see cref="Maf.Lab.Domain.Tracing.TraceKinds.Prompt"/>), retrieval diagnostics
/// (<see cref="Maf.Lab.Domain.Tracing.TraceKinds.Retrieval"/>), the run's AG-UI frames (<see cref="RunFrames.Kind"/>).
/// Events reach it in order, off the turn's path: a slow or failing observer never holds up or fails a turn. It is a
/// singleton that keys what it keeps per run by the run id; the core registers none of its own. Events may carry
/// message content, which is why the monitor that wants them is a dev tool never installed in stage or prod (5d).
/// </summary>
public interface ITurnObserver
{
    bool IsEnabled(string kind);

    Task OnEventAsync(string runId, Maf.Lab.Domain.Tracing.TraceEvent e, CancellationToken ct);

    /// <summary>The run's AG-UI frames as the client received them, once the run's response has ended.</summary>
    Task OnFramesAsync(string runId, string? turnId, IReadOnlyList<Maf.Lab.Domain.Tracing.RunFrame> frames, CancellationToken ct);
}

/// <summary>The name an observer is asked about for a run's AG-UI frames (a stream of its own, not a trace kind).</summary>
public static class RunFrames
{
    public const string Kind = "agui.frames";
}

/// <summary>What a rename came to: done, no such conversation of the caller's, or a title outside 1–MaxChars characters.</summary>
public enum RenameOutcome
{
    Renamed,
    NotFound,
    Invalid,
}

/// <summary>
/// The conversation store the core owns (decision 5y): the list plugin reaches storage, titles and the audit only through
/// it. Every method acts on the caller's own conversations, read from the request's principal; none takes a principal, a
/// tenant or a user, so a plugin cannot name anyone else. The records are <see cref="Maf.Lab.Domain.History"/>'s, the
/// shape the web reads.
/// </summary>
public interface IConversationStore
{
    /// <summary>A page of the caller's conversations that have turns, newest activity first, matching <paramref name="search"/> in a title, question or answer.</summary>
    Task<Maf.Lab.Domain.History.ConversationPage> PageAsync(string? search, int limit, string? before, CancellationToken ct);

    Task<RenameOutcome> RenameAsync(string conversationId, string title, CancellationToken ct);

    /// <summary>A soft delete, recorded as <c>conversation.delete</c> in the audit once done; false when not found (and nothing is recorded).</summary>
    Task<bool> DeleteAsync(string conversationId, CancellationToken ct);
}

/// <summary>A brand, as add-white-labeling defines it in full; the port exists now so the seam list is closed.</summary>
public sealed record Brand(string Name);

/// <summary>The brand port (add-white-labeling): null means the installation brand applies.</summary>
public interface IBrandProvider
{
    Task<Brand?> ForAsync(Principal principal, CancellationToken ct);
}
