using System.Data;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Maf.Lab.Api.Agent;
using Maf.Lab.Api.Storage;
using Maf.Lab.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Maf.Lab.Api.Compliance;

/// <summary>One tenant-visible entry per validated operator session, durable across API replicas and refreshes.</summary>
public sealed class OperatorSessionAudit(IDbContextFactory<MafDbContext> database, ToolAudit audit, TimeProvider clock)
{
    public async Task<bool> RecordAsync(Principal principal, string issuer, string sessionId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!principal.IsPlatformAdmin || principal.TenantId.IsShared
            || string.IsNullOrWhiteSpace(principal.TenantId.Value) || string.IsNullOrWhiteSpace(principal.UserId))
            throw new UnauthorizedAccessException("Only an organization-scoped operator may record an entry.");
        if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(sessionId))
            throw new ArgumentException("Operator entry requires a validated issuer and session.");
        // Do not store or log the raw session identifier. JSON gives the tuple unambiguous field boundaries.
        var sessionKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new[] { issuer, sessionId, principal.UserId, principal.TenantId.Value }))));
        await using var context = await database.CreateDbContextAsync(ct);
        // SQLite serializable transactions acquire the writer before the read, as other atomic audit writers do.
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        if (await context.OperatorSessions.AnyAsync(row => row.SessionKey == sessionKey, ct)) return false;
        context.OperatorSessions.Add(new OperatorSessionRow { SessionKey = sessionKey, EnteredAt = clock.GetUtcNow().UtcDateTime });
        await audit.RecordInTransactionAsync(context, new AuditEntry(principal, null, null, AuditKinds.OperatorEnter,
            "", "entered", 0, AuditKinds.OperatorEnter), ct);
        await transaction.CommitAsync(ct);
        return true;
    }

    /// <summary>Only validated token claims supply session and selected organization; no request parameter participates.</summary>
    public static bool TryReadSession(ClaimsPrincipal user, Principal principal, bool company, out string sessionId)
    {
        sessionId = "";
        if (user.Identity?.IsAuthenticated != true || !principal.IsPlatformAdmin) return false;
        var sessions = user.FindAll("sid").ToArray();
        if (sessions.Length != 1 || sessions[0].ValueType != ClaimValueTypes.String
            || string.IsNullOrWhiteSpace(sessions[0].Value)) return false;
        if (company)
        {
            var scopes = user.FindAll("scope").ToArray();
            if (scopes.Length != 1 || scopes[0].ValueType != ClaimValueTypes.String) return false;
            var organizations = scopes[0].Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .Where(scope => scope == "organization" || scope.StartsWith("organization:", StringComparison.Ordinal)).ToArray();
            if (organizations.Length != 1 || organizations[0] != $"organization:{principal.TenantId.Value}") return false;
        }
        sessionId = sessions[0].Value;
        return true;
    }
}
