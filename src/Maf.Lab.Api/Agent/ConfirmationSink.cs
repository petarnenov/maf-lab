using System.Text.Json;
using Maf.Lab.Domain.Billing;
using ModelContextProtocol.Protocol;

namespace Maf.Lab.Api.Agent;

/// <summary>A confirmation the server asked for and nobody has answered yet.</summary>
public sealed record CapturedConfirmation(FeeAdjustmentSummary Adjustment, string State, string Question);

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
            || meta[FeeAdjustmentTool.SummaryKey] is not { } summary
            || meta[FeeAdjustmentTool.StateKey]?.GetValue<string>() is not { Length: > 0 } state)
        {
            return null;
        }

        var adjustment = JsonSerializer.Deserialize<FeeAdjustmentSummary>(summary.ToJsonString(), Json);
        return adjustment is null ? null : new CapturedConfirmation(adjustment, state, request.Message ?? "");
    }
}
