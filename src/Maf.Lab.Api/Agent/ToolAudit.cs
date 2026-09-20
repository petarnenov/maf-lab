using Maf.Lab.Api.Compliance;
using Maf.Lab.Api.Storage;
using Maf.Lab.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Maf.Lab.Api.Agent;

/// <param name="ToolName">A tool name for a tool call, otherwise the action (e.g. conversation.delete).</param>
/// <param name="Arguments">Identifiers only (key=value), never free text.</param>
public sealed record AuditEntry(Principal Principal, string? ConversationId, string? TurnId, string ToolName, string Arguments,
    string Outcome, long DurationMs, string Kind = AuditKinds.Tool);

/// <summary>
/// Records every audited action — tool invocations (including attempts to call tools that do not exist), deletions
/// and exports — as one chained history. Identifiers only.
/// </summary>
public sealed class ToolAudit(IDbContextFactory<MafDbContext> db, ILogger<ToolAudit> logger, TimeProvider time)
{
    /// <summary>Appends the action and returns the digest it was chained with.</summary>
    public async Task<string> RecordAsync(AuditEntry entry, CancellationToken ct)
    {
        logger.LogInformation("audit kind={Kind} principal={PrincipalId} firm={FirmId} action={Action} args={Args} outcome={Outcome} ms={DurationMs}",
            entry.Kind, entry.Principal.UserId, entry.Principal.FirmId.Value, entry.ToolName, entry.Arguments, entry.Outcome, entry.DurationMs);

        var row = new AuditRow
        {
            At = time.GetUtcNow().UtcDateTime,
            PrincipalId = entry.Principal.UserId,
            FirmId = entry.Principal.FirmId.Value,
            ConversationId = entry.ConversationId,
            TurnId = entry.TurnId,
            Kind = entry.Kind,
            ToolName = entry.ToolName.Length > 100 ? entry.ToolName[..100] : entry.ToolName,
            Arguments = entry.Arguments,
            Outcome = entry.Outcome,
            DurationMs = entry.DurationMs,
        };

        await using var context = await db.CreateDbContextAsync(ct);
        // The head must be read and the row written under one write lock: two replicas appending at the same moment
        // must not link to the same predecessor. SQLite allows one writer at a time, so an immediate transaction is
        // enough — and the chain follows row ids, never clocks.
        await using var transaction = await context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var previous = await context.Audit.AsNoTracking()
            .Where(a => a.Hash != null)
            .OrderByDescending(a => a.Id)
            .Select(a => a.Hash)
            .FirstOrDefaultAsync(ct);
        row.PreviousHash = previous;
        row.Hash = AuditChain.Hash(row, previous);
        context.Audit.Add(row);
        await context.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return row.Hash;
    }

    /// <summary>The digest of the most recent chained action, or null when nothing is chained yet.</summary>
    public async Task<string?> HeadAsync(CancellationToken ct)
    {
        await using var context = await db.CreateDbContextAsync(ct);
        return await context.Audit.AsNoTracking()
            .Where(a => a.Hash != null)
            .OrderByDescending(a => a.Id)
            .Select(a => a.Hash)
            .FirstOrDefaultAsync(ct);
    }
}
