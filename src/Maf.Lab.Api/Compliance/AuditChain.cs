using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Maf.Lab.Api.Storage;

namespace Maf.Lab.Api.Compliance;

/// <summary>The kinds of action the record covers. Rows written before kinds existed carry none.</summary>
public static class AuditKinds
{
    public const string Tool = "tool";
    public const string ConversationDelete = "conversation.delete";
    public const string ComplianceExport = "compliance.export";
    /// <summary>A request that arrived from another agent over A2A.</summary>
    public const string A2ARequest = "a2a.request";
    /// <summary>A request this system sent to another agent over A2A.</summary>
    public const string A2AConsultation = "a2a.consultation";
}

/// <param name="Intact">False when a row's content or a row's absence breaks a link.</param>
/// <param name="Checked">How many chained rows were walked.</param>
/// <param name="Unchained">Rows written before chaining began; reported, never rewritten.</param>
/// <param name="Head">Digest of the most recent chained row.</param>
public sealed record ChainReport(
    bool Intact,
    int Checked,
    int Unchained,
    DateTimeOffset? From,
    DateTimeOffset? To,
    string? Head,
    long? FirstBrokenId = null,
    string? Reason = null);

/// <summary>
/// Links each audited action to the one before it. The digest covers exactly the stored fields, so verification
/// needs nothing but the rows: no key, no side file. This proves tampering; it does not prevent it — that needs
/// storage the application cannot rewrite.
/// </summary>
public static class AuditChain
{
    /// <summary>The digest of a row, given the digest of the row before it. Order and formats are fixed.</summary>
    public static string Hash(AuditRow row, string? previousHash)
    {
        var canonical = string.Join('\u001f',
            previousHash ?? "",
            // Always as UTC: the store returns the timestamp with an unspecified kind, and "O" would then render it
            // differently from the value that was hashed on write.
            DateTime.SpecifyKind(row.At, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture),
            row.PrincipalId,
            row.FirmId,
            row.ConversationId ?? "",
            row.TurnId ?? "",
            row.Kind ?? "",
            row.ToolName,
            row.Arguments,
            row.Outcome,
            row.DurationMs.ToString(CultureInfo.InvariantCulture));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    /// <summary>
    /// Walks rows in id order — never in timestamp order, because two replicas' clocks can disagree while row ids
    /// cannot — and reports the first link that does not hold.
    /// </summary>
    public static ChainReport Verify(IReadOnlyList<AuditRow> rows)
    {
        var unchained = rows.Count(r => r.Hash is null);
        var chained = rows.Where(r => r.Hash is not null).OrderBy(r => r.Id).ToList();
        if (chained.Count == 0)
        {
            return new ChainReport(true, 0, unchained, null, null, null,
                Reason: unchained > 0 ? $"{unchained} row(s) predate the chain" : "no records");
        }

        string? previous = null;
        for (var i = 0; i < chained.Count; i++)
        {
            var row = chained[i];
            // The first chained row may follow unchained history, so its link is whatever it recorded.
            var expectedPrevious = i == 0 ? row.PreviousHash : previous;
            if (i > 0 && row.PreviousHash != previous)
            {
                return Broken(chained, unchained, row, "the previous row is missing or was replaced");
            }
            if (Hash(row, expectedPrevious) != row.Hash)
            {
                return Broken(chained, unchained, row, "the row's content does not match its digest");
            }
            previous = row.Hash;
        }

        return new ChainReport(true, chained.Count, unchained,
            Utc(chained[0].At), Utc(chained[^1].At), previous,
            Reason: unchained > 0 ? $"{unchained} row(s) predate the chain" : null);
    }

    private static ChainReport Broken(IReadOnlyList<AuditRow> chained, int unchained, AuditRow row, string reason) =>
        new(false, chained.Count, unchained, Utc(chained[0].At), Utc(chained[^1].At), null, row.Id, reason);

    private static DateTimeOffset Utc(DateTime at) => new(DateTime.SpecifyKind(at, DateTimeKind.Utc));
}
