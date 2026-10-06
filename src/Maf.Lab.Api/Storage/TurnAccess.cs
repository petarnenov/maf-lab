using Maf.Lab.Plugins.Abstractions;
using Maf.Lab.Retrieval.Auth;
using Microsoft.EntityFrameworkCore;

namespace Maf.Lab.Api.Storage;

/// <summary>The core's side of <see cref="ITurnAccess"/>: the owner, or a tenant admin of the same tenant for a turn under review.</summary>
public sealed class TurnAccess(IPrincipalAccessor principals, IDbContextFactory<MafDbContext> db) : ITurnAccess
{
    public async Task<bool> MayReadAsync(string turnId, CancellationToken ct)
    {
        var principal = principals.Current;
        await using var ctx = await db.CreateDbContextAsync(ct);
        return await ctx.Turns.AnyAsync(t => t.Id == turnId && t.TenantId == principal.TenantId.Value
            && (t.UserId == principal.UserId || principal.IsTenantAdmin && t.SignalsJson != "[]"), ct);
    }
}
