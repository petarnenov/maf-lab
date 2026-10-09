using System.Text.Json.Nodes;
using Maf.Lab.Api.Agent;
using Maf.Lab.Api.Storage;
using Maf.Lab.Plugins.Abstractions;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Retrieval.Auth;
using Maf.Lab.Retrieval.Store;
using Microsoft.EntityFrameworkCore;

namespace Maf.Lab.Api.Feedback;

public sealed class CoreFeedbackReviewStore(IDbContextFactory<MafDbContext> db, IPrincipalAccessor principals,
    TimeProvider time, IServiceProvider services) : IFeedbackReviewStore
{
    public string? DomainOf(string tool) => Domains.OfTool(tool);

    public async Task<IReadOnlyList<ReviewStoredTurn>> FlaggedAsync(CancellationToken ct)
    {
        await using var context = await db.CreateDbContextAsync(ct);
        var scope = principals.Current.TenantId.Value;
        var turns = await context.Turns.AsNoTracking().Where(t => t.TenantId == scope && t.SignalsJson != "[]")
            .OrderByDescending(t => t.CreatedAt).Take(100).ToListAsync(ct);
        var ids = turns.Select(t => t.Id).ToList();
        var feedback = await context.Feedback.AsNoTracking().Where(f => ids.Contains(f.TurnId)).ToListAsync(ct);
        return [.. turns.Select(t => View(t, [.. feedback.Where(f => f.TurnId == t.Id).Select(f => f.Kind).Distinct()]))];
    }

    public async Task<ReviewStoredTurn?> FindAsync(string turnId, CancellationToken ct)
    {
        await using var context = await db.CreateDbContextAsync(ct);
        var scope = principals.Current.TenantId.Value;
        var row = await context.Turns.AsNoTracking().FirstOrDefaultAsync(t => t.Id == turnId && t.TenantId == scope, ct);
        return row is null ? null : View(row, []);
    }

    public async Task<bool> LabelAsync(string turnId, string dataset, JsonObject row, CancellationToken ct)
    {
        var principal = principals.Current;
        await using var context = await db.CreateDbContextAsync(ct);
        var turn = await context.Turns.FirstOrDefaultAsync(t => t.Id == turnId && t.TenantId == principal.TenantId.Value, ct);
        if (turn is null) return false;
        context.Labels.Add(new LabelRow
        {
            Id = $"l_{Guid.NewGuid():N}", TurnId = turn.Id, TenantId = turn.TenantId, ReviewerId = principal.UserId,
            Dataset = dataset, RowJson = row.ToJsonString(new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)),
            CreatedAt = time.GetUtcNow().UtcDateTime,
        });
        turn.Labeled = true;
        await context.SaveChangesAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<string>?> ResolveChunkIdsAsync(string tool, IReadOnlyList<ReviewSource> sources, CancellationToken ct)
    {
        if (!Domains.IsSearch(tool) || Domains.IsCodeSearch(tool) || Domains.OfTool(tool) is not { } domain
            || services.GetKeyedService<TenantScopedMaintenance>(domain) is not { } store) return null;
        var principal = principals.Current;
        var ids = new List<string>();
        foreach (var source in sources)
        {
            if (!TenantId.TryParse(source.DocId.Split('/')[0], out var scope) || !principal.ReadableTenants.Contains(scope)) continue;
            try { ids.AddRange(await store.ChunkIdsForSectionAsync(scope, source.DocId, source.SectionPath, ct)); }
            catch (Exception ex) when (ex is not OperationCanceledException) { }
        }
        return [.. ids.Distinct()];
    }

    private static ReviewStoredTurn View(TurnRow row, IReadOnlyList<string> feedback) =>
        new(row.Id, row.ConversationId, row.UserId, row.TenantId, row.Question, row.Answer, row.SignalsJson,
            row.ToolCallsJson, row.SourcesJson, new DateTimeOffset(DateTime.SpecifyKind(row.CreatedAt, DateTimeKind.Utc)), row.Labeled, feedback);
}
