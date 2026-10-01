using System.Text.Json;

namespace Maf.Lab.Api.Agent;

/// <summary>
/// What a run stops to ask a person: the protocol's interrupt, in this system's own terms, so that nothing outside the
/// AG-UI wiring names a protocol type (agui-protocol-only). <c>Agent/AGUI</c> turns it into the interrupt the official
/// server sends.
/// </summary>
/// <param name="Id">The proposal it is about; the person's answer resumes it by this id.</param>
/// <param name="ExpiresAt">When it stops being answerable, as ISO 8601; null when it does not expire.</param>
public sealed record PersonQuestion(
    string Id,
    string Message,
    string Reason,
    string ToolCallId,
    string? ExpiresAt,
    JsonElement? ResponseSchema,
    JsonElement Metadata);

/// <summary>A person's answer to a <see cref="PersonQuestion"/>, as it arrived.</summary>
/// <param name="Payload">What they sent: <c>{ approve, idempotencyKey? }</c>, or a bare <c>true</c>.</param>
public sealed record PersonAnswer(string QuestionId, JsonElement? Payload);
