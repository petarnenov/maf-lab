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

/// <summary>A domain's descriptor: the data the core reads to route, search, guard and render (decision 6).</summary>
public sealed record DomainDescriptor(
    string Id,
    string? SearchTool,
    IReadOnlyList<string> Tools,
    IReadOnlyList<string> GraphTools,
    IReadOnlyList<string> RoutingQuestions,
    string? GuardContext,
    IReadOnlyDictionary<string, string> CardTypes);

/// <summary>A domain's behaviour that is not data (decision 6); defined in full when the first domain moves (task 4.2).</summary>
public interface IDomainBehaviour
{
    /// <summary>The domain this behaviour belongs to.</summary>
    string Domain { get; }
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
