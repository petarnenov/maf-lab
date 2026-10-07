using Maf.Lab.Plugins.Abstractions;
using Maf.Lab.Retrieval.Auth;
using Microsoft.EntityFrameworkCore;

namespace Maf.Lab.Api.Storage;

/// <summary>
/// The core's side of <see cref="ITurnRecords"/>: the core records of the caller's tenant's turns, for a tenant admin of
/// that tenant only. The tenant comes from the request's principal; there is no way to name another.
/// </summary>
public sealed class TurnRecords(IPrincipalAccessor principals, IDbContextFactory<MafDbContext> db) : ITurnRecords
{
    public async Task<IReadOnlyList<StoredTurnRecord>> SinceAsync(DateTimeOffset from, CancellationToken ct)
    {
        var principal = principals.Current;
        if (!principal.IsTenantAdmin)
        {
            throw new UnauthorizedAccessException("Only a tenant admin reads the tenant's turn records.");
        }
        var since = from.UtcDateTime;
        await using var ctx = await db.CreateDbContextAsync(ct);
        return await ctx.Turns.AsNoTracking()
            .Where(t => t.TenantId == principal.TenantId.Value && t.CreatedAt >= since)
            .Select(t => new StoredTurnRecord(t.CreatedAt, t.RecordJson))
            .ToListAsync(ct);
    }
}
