using System.Text.Json;
using Maf.Lab.Api.Storage;

namespace Maf.Lab.Api.Agent.Writes;

/// <summary>
/// A write waiting for a person, as the browser and the eval see it: the tool's summary, never its opaque state — an
/// answer names the write by its id and the server uses the state it kept for it.
/// </summary>
public sealed record PendingWrite(string WriteId, string ToolName, JsonElement Summary, JsonElement? SummarySchema, string Question,
    DateTimeOffset? ExpiresAt)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static PendingWrite From(PendingWriteRow row, JsonElement? schema) => new(
        row.Id, row.ToolName, Parse(row.Summary), schema, row.Question ?? "",
        row.ExpiresAt is { } at ? new DateTimeOffset(DateTime.SpecifyKind(at, DateTimeKind.Utc)) : null);

    /// <summary>
    /// The pause a run ends on while this waits: the one place its metadata is composed, for the turn that proposed it and
    /// for a rejoin alike (<see cref="TurnContents.Ask"/> hands it to the official AG-UI server).
    /// </summary>
    public PersonQuestion ToQuestion(string callId, JsonElement? answerSchema) => new(
        Id: WriteId,
        Message: Question,
        Reason: "approval_required",
        ToolCallId: callId,
        ExpiresAt: ExpiresAt?.ToString("O"),
        ResponseSchema: answerSchema,
        Metadata: JsonSerializer.SerializeToElement(this, Json));

    private static JsonElement Parse(string json)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
        return doc.RootElement.Clone();
    }
}
