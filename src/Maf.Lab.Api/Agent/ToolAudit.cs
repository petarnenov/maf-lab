using Maf.Lab.Api.Storage;
using Maf.Lab.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Maf.Lab.Api.Agent;

public sealed record AuditEntry(Principal Principal, string? ConversationId, string? TurnId, string ToolName, string Arguments, string Outcome, long DurationMs);

/// <summary>Records every tool invocation (including attempts to call tools that do not exist). Identifiers only.</summary>
public sealed class ToolAudit(IDbContextFactory<MafDbContext> db, ILogger<ToolAudit> logger, TimeProvider time)
{
    public async Task RecordAsync(AuditEntry entry, CancellationToken ct)
    {
        logger.LogInformation("tool_audit principal={PrincipalId} firm={FirmId} tool={Tool} args={Args} outcome={Outcome} ms={DurationMs}",
            entry.Principal.UserId, entry.Principal.FirmId.Value, entry.ToolName, entry.Arguments, entry.Outcome, entry.DurationMs);

        await using var context = await db.CreateDbContextAsync(ct);
        context.Audit.Add(new AuditRow
        {
            At = time.GetUtcNow().UtcDateTime,
            PrincipalId = entry.Principal.UserId,
            FirmId = entry.Principal.FirmId.Value,
            ConversationId = entry.ConversationId,
            TurnId = entry.TurnId,
            ToolName = entry.ToolName.Length > 100 ? entry.ToolName[..100] : entry.ToolName,
            Arguments = entry.Arguments,
            Outcome = entry.Outcome,
            DurationMs = entry.DurationMs,
        });
        await context.SaveChangesAsync(ct);
    }
}
