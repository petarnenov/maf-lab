using System.Text.Json;
using System.Text.Json.Nodes;
using Maf.Lab.Api.Feedback;
using Maf.Lab.Api.Storage;
using Maf.Lab.Domain.Feedback;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Retrieval.Auth;
using Maf.Lab.Retrieval.Store;
using Microsoft.EntityFrameworkCore;

namespace Maf.Lab.Api.Endpoints;

public static class FeedbackEndpoints
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapFeedback(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/feedback", async (FeedbackRequest request, IPrincipalAccessor principals, IDbContextFactory<MafDbContext> db, TimeProvider time, CancellationToken ct) =>
        {
            if (!FeedbackKind.IsKnown(request.Kind))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["kind"] = ["kind must be wrong_tool, wrong_document or wrong_answer."] });
            }
            var principal = principals.Current;
            await using var ctx = await db.CreateDbContextAsync(ct);
            var turn = await ctx.Turns.FirstOrDefaultAsync(t => t.Id == request.TurnId && t.ConversationId == request.ConversationId
                && t.UserId == principal.UserId && t.FirmId == principal.FirmId.Value, ct);
            if (turn is null)
            {
                return Results.NotFound();
            }

            var row = new FeedbackRow
            {
                Id = $"f_{Guid.NewGuid():N}",
                TurnId = turn.Id,
                ConversationId = turn.ConversationId,
                UserId = principal.UserId,
                FirmId = principal.FirmId.Value,
                Kind = request.Kind,
                Comment = request.Comment is { Length: > 1000 } c ? c[..1000] : request.Comment,
                CreatedAt = time.GetUtcNow().UtcDateTime,
            };
            ctx.Feedback.Add(row);
            var signals = JsonSerializer.Deserialize<List<string>>(turn.SignalsJson, Json) ?? [];
            if (!signals.Contains(TurnSignal.NegativeFeedback))
            {
                signals.Add(TurnSignal.NegativeFeedback);
                turn.SignalsJson = JsonSerializer.Serialize(signals, Json);
            }
            await ctx.SaveChangesAsync(ct);
            return Results.Accepted(value: new FeedbackAccepted(row.Id));
        }).RequireAuthorization();

        var admin = app.MapGroup("/api/admin/feedback").RequireAuthorization(AuthPolicies.FirmAdmin);

        admin.MapGet("/queue", async (IPrincipalAccessor principals, IDbContextFactory<MafDbContext> db, TenantScopedMaintenance store, CancellationToken ct) =>
        {
            var principal = principals.Current;
            await using var ctx = await db.CreateDbContextAsync(ct);
            var turns = await ctx.Turns.Where(t => t.FirmId == principal.FirmId.Value && t.SignalsJson != "[]")
                .OrderByDescending(t => t.CreatedAt).Take(100).ToListAsync(ct);
            var turnIds = turns.Select(t => t.Id).ToList();
            var feedback = await ctx.Feedback.Where(f => turnIds.Contains(f.TurnId)).ToListAsync(ct);

            var items = new List<ReviewQueueItem>();
            foreach (var t in turns)
            {
                var toolCalls = JsonSerializer.Deserialize<List<ToolCallRecord>>(t.ToolCallsJson, Json) ?? [];
                var sources = JsonSerializer.Deserialize<List<SourceKey>>(t.SourcesJson, Json) ?? [];
                var chunkIds = await ResolveChunkIdsAsync(principal, sources, store, ct);
                toolCalls = toolCalls.Select(c => c.ToolName == "search_documents"
                    ? c with { ChunkIds = chunkIds.Where(id => c.DocIds.Any(d => id.StartsWith(d + "#", StringComparison.Ordinal))).ToList() }
                    : c).ToList();
                items.Add(new ReviewQueueItem(t.Id, t.ConversationId, t.UserId, t.Question, t.Answer,
                    JsonSerializer.Deserialize<List<string>>(t.SignalsJson, Json) ?? [], toolCalls,
                    feedback.Where(f => f.TurnId == t.Id).Select(f => f.Kind).Distinct().ToList(),
                    new DateTimeOffset(DateTime.SpecifyKind(t.CreatedAt, DateTimeKind.Utc)), t.Labeled));
            }
            return Results.Ok(items);
        });

        admin.MapPost("/{turnId}/label", async (string turnId, LabelRequest request, IPrincipalAccessor principals,
            IDbContextFactory<MafDbContext> db, DatasetWriter datasets, TimeProvider time, CancellationToken ct) =>
        {
            var principal = principals.Current;
            await using var ctx = await db.CreateDbContextAsync(ct);
            var turn = await ctx.Turns.FirstOrDefaultAsync(t => t.Id == turnId && t.FirmId == principal.FirmId.Value, ct);
            if (turn is null)
            {
                return Results.NotFound();
            }
            var (row, error) = BuildRow(turn, request);
            if (row is null)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["label"] = [error!] });
            }

            await datasets.AppendAsync(request.Dataset, row, ct);
            ctx.Labels.Add(new LabelRow
            {
                Id = $"l_{Guid.NewGuid():N}",
                TurnId = turn.Id,
                FirmId = turn.FirmId,
                ReviewerId = principal.UserId,
                Dataset = request.Dataset,
                RowJson = row.ToJsonString(Json),
                CreatedAt = time.GetUtcNow().UtcDateTime,
            });
            turn.Labeled = true;
            await ctx.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        return app;
    }

    /// <summary>Builds the dataset row for a labeled turn. Row ids are stable per turn and dataset.</summary>
    public static (JsonObject? Row, string? Error) BuildRow(TurnRow turn, LabelRequest request)
    {
        var id = $"fb-{request.Dataset}-{turn.Id}";
        switch (request.Dataset)
        {
            case EvalDataset.Selection when request.ExpectedTools is not null:
                return (new JsonObject
                {
                    ["id"] = id, ["question"] = turn.Question, ["expectedTools"] = Array(request.ExpectedTools),
                    ["category"] = "feedback", ["firmId"] = turn.FirmId, ["source"] = "feedback",
                }, null);
            case EvalDataset.Retrieval when request.RelevantChunkIds is { Count: > 0 }:
                return (new JsonObject
                {
                    ["id"] = id, ["query"] = turn.Question, ["relevantChunkIds"] = Array(request.RelevantChunkIds),
                    ["firmId"] = turn.FirmId, ["source"] = "feedback",
                }, null);
            case EvalDataset.Generation when !string.IsNullOrWhiteSpace(request.ReferenceAnswer):
                return (new JsonObject
                {
                    ["id"] = id, ["question"] = turn.Question, ["referenceAnswer"] = request.ReferenceAnswer,
                    ["expectedDocIds"] = Array(request.ExpectedDocIds ?? []), ["firmId"] = turn.FirmId, ["source"] = "feedback",
                }, null);
            default:
                return (null, "selection needs expectedTools, retrieval needs relevantChunkIds, generation needs referenceAnswer.");
        }
    }

    private static JsonArray Array(IEnumerable<string> values) => new(values.Select(v => (JsonNode?)JsonValue.Create(v)).ToArray());

    private sealed record SourceKey(string DocId, string SectionPath);

    private static async Task<List<string>> ResolveChunkIdsAsync(Principal principal, List<SourceKey> sources, TenantScopedMaintenance store, CancellationToken ct)
    {
        var ids = new List<string>();
        foreach (var source in sources)
        {
            var tenantPart = source.DocId.Split('/')[0];
            if (!TenantId.TryParse(tenantPart, out var tenant) || !principal.ReadableTenants.Contains(tenant))
            {
                continue;
            }
            try
            {
                ids.AddRange(await store.ChunkIdsForSectionAsync(tenant, source.DocId, source.SectionPath, ct));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Store unavailable: the reviewer can still type chunk ids.
            }
        }
        return ids.Distinct().ToList();
    }
}
