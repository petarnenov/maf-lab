using Maf.Lab.Api.Storage;
using Maf.Lab.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Maf.Lab.Api.Agent;

/// <summary>
/// Conversations are bound to the principal that created them. Their ids are issued by the server, or — for a run whose
/// client names a thread nobody has (AG-UI clients name their own threads) — taken from the client
/// (agui-protocol-only).
/// </summary>
public sealed partial class ConversationService(IDbContextFactory<MafDbContext> db, TimeProvider time)
{
    public Task<string> CreateAsync(Principal principal, CancellationToken ct) => CreateAsync(principal, $"c_{Guid.NewGuid():N}", ct);

    private async Task<string> CreateAsync(Principal principal, string id, CancellationToken ct)
    {
        await using var ctx = await db.CreateDbContextAsync(ct);
        var row = new ConversationRow
        {
            Id = id,
            UserId = principal.UserId,
            FirmId = principal.FirmId.Value,
            CreatedAt = time.GetUtcNow().UtcDateTime,
            LastActivityAt = time.GetUtcNow().UtcDateTime,
        };
        ctx.Conversations.Add(row);
        await ctx.SaveChangesAsync(ct);
        return row.Id;
    }

    /// <summary>Returns the id when it belongs to the principal, a new id when none was given, or null (→ 404).</summary>
    public async Task<string?> ResolveAsync(Principal principal, string? conversationId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return await CreateAsync(principal, ct);
        }
        await using var ctx = await db.CreateDbContextAsync(ct);
        var owned = await ctx.Conversations.AnyAsync(
            c => c.Id == conversationId && c.UserId == principal.UserId && c.FirmId == principal.FirmId.Value && c.DeletedAt == null, ct);
        return owned ? conversationId : null;
    }

    /// <summary>
    /// The thread a run names, for this principal: theirs, a new one when none was named, a new one under the named id
    /// when nobody has it, or null (→ 404) when it is someone else's, was deleted, or is not a well-formed id.
    /// </summary>
    public async Task<string?> ResolveOrClaimAsync(Principal principal, string? threadId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(threadId))
        {
            return await CreateAsync(principal, ct);
        }
        if (!ThreadIdPattern().IsMatch(threadId))
        {
            return null;
        }
        await using var ctx = await db.CreateDbContextAsync(ct);
        var row = await ctx.Conversations.FirstOrDefaultAsync(c => c.Id == threadId, ct);
        if (row is null)
        {
            try
            {
                return await CreateAsync(principal, threadId, ct);
            }
            catch (DbUpdateException)
            {
                // Claimed by someone else in the meantime: theirs, not this caller's.
                return await ResolveAsync(principal, threadId, ct);
            }
        }
        return row.UserId == principal.UserId && row.FirmId == principal.FirmId.Value && row.DeletedAt == null ? threadId : null;
    }

    [System.Text.RegularExpressions.GeneratedRegex("^[A-Za-z0-9_-]{1,64}$")]
    internal static partial System.Text.RegularExpressions.Regex ThreadIdPattern();
}
