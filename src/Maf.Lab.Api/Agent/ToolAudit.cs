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
        await using var context = await db.CreateDbContextAsync(ct);
        await using var transaction = await context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var hash = await RecordInTransactionAsync(context, entry, ct);
        await transaction.CommitAsync(ct);
        return hash;
    }

    /// <summary>The caller owns the SQLite write transaction, including the state change being audited.</summary>
    internal async Task<string> RecordInTransactionAsync(MafDbContext context, AuditEntry entry, CancellationToken ct)
    {
        if (context.Database.CurrentTransaction is null) throw new InvalidOperationException("An audit append requires a write transaction.");
        logger.LogInformation("audit kind={Kind} principal={PrincipalId} tenant={TenantId} action={Action} args={Args} outcome={Outcome} ms={DurationMs}",
            entry.Kind, entry.Principal.UserId, entry.Principal.TenantId.Value, entry.ToolName, entry.Arguments, entry.Outcome, entry.DurationMs);

        var row = new AuditRow
        {
            At = time.GetUtcNow().UtcDateTime,
            PrincipalId = entry.Principal.UserId,
            TenantId = entry.Principal.TenantId.Value,
            ConversationId = entry.ConversationId,
            TurnId = entry.TurnId,
            Kind = entry.Kind,
            ToolName = entry.ToolName.Length > 100 ? entry.ToolName[..100] : entry.ToolName,
            Arguments = entry.Arguments,
            Outcome = entry.Outcome,
            DurationMs = entry.DurationMs,
        };

        var previous = await context.Audit.AsNoTracking()
            .Where(a => a.Hash != null)
            .OrderByDescending(a => a.Id)
            .Select(a => a.Hash)
            .FirstOrDefaultAsync(ct);
        row.PreviousHash = previous;
        row.Hash = AuditChain.Hash(row, previous);
        context.Audit.Add(row);
        await context.SaveChangesAsync(ct);
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
