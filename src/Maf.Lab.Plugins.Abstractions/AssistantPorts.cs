using System.Text.Json;
using Maf.Lab.Domain.Tenancy;

namespace Maf.Lab.Plugins.Abstractions;

/// <summary>
/// The assistant answering a question for a principal (extract-a2a): the chat agent with its system prompt, the guard on
/// the question and on every tool result, and the tools of the domains in use. A Port, so a plugin that relays questions
/// (an A2A partner's) frames the answer in its own protocol without running a turn itself.
/// </summary>
public interface IAssistantAnswer
{
    /// <summary>The answer's text; the guard's fixed refusal when the question tries to steer the assistant.</summary>
    Task<string> AnswerAsync(Principal principal, string question, CancellationToken ct);
}

/// <summary>
/// One tool of a domain's MCP server, called for a principal through the core's tool source (extract-a2a): the same server
/// precedence, allow-list and observers as a turn. A Port, so a plugin reads another domain's data over MCP without a
/// client of its own.
/// </summary>
public interface IDomainToolCall
{
    /// <summary>The tool's structured result; null when the domain is absent, its server cannot answer, or the tool erred.</summary>
    Task<JsonElement?> CallAsync(Principal principal, string domain, string tool, IReadOnlyDictionary<string, object?> arguments,
        CancellationToken ct);
}

/// <summary>
/// The audit record of protocol activity with an explicit actor (extract-a2a): a partner's A2A request, recorded as the
/// partner, not as whoever the request's principal resolves to. Write flows, whose actor IS the request's principal, use
/// <see cref="IWriteAudit"/>. Identifiers only, never free text.
/// </summary>
public interface IActivityAudit
{
    Task RecordAsync(Principal actor, string kind, string action, string identifiers, string outcome, long elapsedMs,
        CancellationToken ct);
}
