using System.Text.Json;
using Maf.Lab.Plugins.Abstractions;

namespace Maf.Lab.Tests.Plugins;

/// <summary>
/// The fixture domain's write-confirmation flow (generalize-write-confirmation): a test double for the seam's mechanics —
/// ask, confirm, reject, expire, replay on rejoin, the store's guarded updates — not billing's rules. Its rule is the
/// fixture's own and deliberately simple: every proposal of the fakes' write tool is put to the person, with the tool's
/// own question, and every step is recorded under <see cref="AuditKind"/>. Billing's own flow is tested in its folder.
/// </summary>
public sealed class FixtureWriteFlow(IWriteAudit audit) : IWriteConfirmationFlow
{
    /// <summary>The kind of the fixture's audit records.</summary>
    public const string AuditKind = "fixture.write";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>The write tool the shared fakes offer.</summary>
    public string ToolName => "propose_fee_adjustment";

    public JsonElement SummarySchema { get; } = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new Dictionary<string, object>
        {
            ["accountId"] = new { title = "Account", type = "string" },
            ["amount"] = new { title = "Amount", type = "number" },
        },
    }, Json);

    public async Task<WriteFlowOutcome> ProposedAsync(WriteProposal proposal, CancellationToken ct)
    {
        await audit.RecordAsync(AuditKind, $"{AuditKind}.proposed", $"writeId={proposal.WriteId}", "proposed", ct);
        return new WriteFlowOutcome.AskPerson(null, null);
    }

    /// <summary>What the fakes' confirmed call reads: the account and the amount the summary carries.</summary>
    public IReadOnlyDictionary<string, object?> ConfirmArguments(JsonElement summary) => new Dictionary<string, object?>
    {
        ["accountId"] = summary.TryGetProperty("accountId", out var a) ? a.GetString() : null,
        ["amount"] = summary.TryGetProperty("amount", out var m) && m.TryGetDecimal(out var d) ? d : 0m,
    };

    public Task ResolvedAsync(WriteProposal proposal, WriteResolution resolution, string outcome, CancellationToken ct) =>
        audit.RecordAsync(AuditKind, $"{AuditKind}.{resolution.ToString().ToLowerInvariant()}", $"writeId={proposal.WriteId}", outcome, ct);
}
