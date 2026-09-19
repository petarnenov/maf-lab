using Maf.Lab.Api.Storage;
using Maf.Lab.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Maf.Lab.Api.Agent;

/// <summary>Conversation ids are issued by the server and bound to the principal that created them.</summary>
public sealed class ConversationService(IDbContextFactory<MafDbContext> db, TimeProvider time)
{
    public async Task<string> CreateAsync(Principal principal, CancellationToken ct)
    {
        await using var ctx = await db.CreateDbContextAsync(ct);
        var row = new ConversationRow
        {
            Id = $"c_{Guid.NewGuid():N}",
            UserId = principal.UserId,
            FirmId = principal.FirmId.Value,
            CreatedAt = time.GetUtcNow().UtcDateTime,
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
            c => c.Id == conversationId && c.UserId == principal.UserId && c.FirmId == principal.FirmId.Value, ct);
        return owned ? conversationId : null;
    }
}
