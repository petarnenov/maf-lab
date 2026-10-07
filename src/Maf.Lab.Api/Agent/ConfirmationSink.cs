using System.Text.Json;
using Maf.Lab.Domain.Writes;
using ModelContextProtocol.Protocol;

namespace Maf.Lab.Api.Agent;

// The fee-typed write confirmation, which has no seam yet (extract-billing part 3).
/// <summary>
/// A confirmation a write tool asked for and nobody has answered yet. The summary is the tool's own: the core keeps it,
/// shows it and hands it back, and reads none of its fields.
/// </summary>
public sealed record CapturedConfirmation(
    JsonElement Summary,
    string State,
    string Question,
    DateTimeOffset? ExpiresAt,
    JsonElement? AnswerSchema);

/// <summary>
/// The MCP client resolves a server's request for input itself, which would mean answering on the user's
/// behalf. This does not answer: it takes the question down and tells the server nobody chose, so the turn
/// can end and a person can decide in their own time.
/// </summary>
public sealed class ConfirmationSink
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public CapturedConfirmation? Captured { get; private set; }

    public ElicitResult Capture(ElicitRequestParams? request)
    {
        if (Read(request) is { } captured)
        {
            Captured = captured;
        }

        // Not a decline: nobody has been asked yet, and a dismissal must never be recorded as a refusal.
        return new ElicitResult { Action = "cancel" };
    }

    private static CapturedConfirmation? Read(ElicitRequestParams? request)
    {
        if (request?.Meta is not { } meta
            || meta[WriteConfirmationKeys.Summary] is not { } summary
            || meta[WriteConfirmationKeys.State]?.GetValue<string>() is not { Length: > 0 } state)
        {
            return null;
        }

        var written = JsonSerializer.SerializeToElement(summary, Json);
        var expiresAt = meta[WriteConfirmationKeys.ExpiresAt]?.GetValue<string>() is { Length: > 0 } text
            && DateTimeOffset.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : (DateTimeOffset?)null;
        var schema = request.RequestedSchema is { } requested
            ? JsonSerializer.SerializeToElement(requested, Json)
            : (JsonElement?)null;

        return new CapturedConfirmation(written, state, request.Message ?? "", expiresAt, schema);
    }
}
