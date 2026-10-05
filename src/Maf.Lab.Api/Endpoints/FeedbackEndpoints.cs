using Maf.Lab.Api.Agent;
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
                && t.UserId == principal.UserId && t.TenantId == principal.TenantId.Value, ct);
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
                TenantId = principal.TenantId.Value,
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

        var admin = app.MapGroup("/api/admin/feedback").RequireAuthorization(AuthPolicies.TenantAdmin);

        admin.MapGet("/queue", async (IPrincipalAccessor principals, IDbContextFactory<MafDbContext> db, TenantScopedMaintenance store,
            [Microsoft.Extensions.DependencyInjection.FromKeyedServices(Domains.Portfolio)] TenantScopedMaintenance portfolioStore, CancellationToken ct) =>
        {
            var principal = principals.Current;
            await using var ctx = await db.CreateDbContextAsync(ct);
            var turns = await ctx.Turns.Where(t => t.TenantId == principal.TenantId.Value && t.SignalsJson != "[]")
                .OrderByDescending(t => t.CreatedAt).Take(100).ToListAsync(ct);
            var turnIds = turns.Select(t => t.Id).ToList();
            var feedback = await ctx.Feedback.Where(f => turnIds.Contains(f.TurnId)).ToListAsync(ct);

            var items = new List<ReviewQueueItem>();
            foreach (var t in turns)
            {
                var toolCalls = JsonSerializer.Deserialize<List<ToolCallRecord>>(t.ToolCallsJson, Json) ?? [];
                var sources = JsonSerializer.Deserialize<List<SourceKey>>(t.SourcesJson, Json) ?? [];
                // Each search's sources are resolved in its own domain's collection: a portfolio document's chunks are
                // not in billing's, and a label must point at chunks the eval of that domain can find.
                var resolved = new List<ToolCallRecord>();
                foreach (var c in toolCalls)
                {
                    // A codebase search's sources are places in files, not chunks of a labelled corpus: nothing to resolve.
                    if (!Domains.IsSearch(c.ToolName) || c.ToolName == Domains.SearchTool[Domains.Codebase])
                    {
                        resolved.Add(c);
                        continue;
                    }
                    var own = sources.Where(s => c.DocIds.Contains(s.DocId)).ToList();
                    var chunkIds = await ResolveChunkIdsAsync(principal, own, c.ToolName == Domains.SearchTool[Domains.Portfolio] ? portfolioStore : store, ct);
                    resolved.Add(c with { ChunkIds = chunkIds.Where(id => c.DocIds.Any(d => id.StartsWith(d + "#", StringComparison.Ordinal))).ToList() });
                }
                toolCalls = resolved;
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
            var turn = await ctx.Turns.FirstOrDefaultAsync(t => t.Id == turnId && t.TenantId == principal.TenantId.Value, ct);
            if (turn is null)
            {
                return Results.NotFound();
            }
            var (row, error) = BuildRow(turn, request, RetrievalDomain(turn, request));
            if (row is null)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["label"] = [error!] });
            }

            await datasets.AppendAsync(request.Dataset, row, ct);
            ctx.Labels.Add(new LabelRow
            {
                Id = $"l_{Guid.NewGuid():N}",
                TurnId = turn.Id,
                TenantId = turn.TenantId,
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
    /// <summary>
    /// The domain whose search found the chunks a retrieval label names — billing unless every labelled chunk belongs to a
    /// document a portfolio search returned. Null when they came from both: one row is scored against one collection.
    /// </summary>
    internal static string? RetrievalDomain(TurnRow turn, LabelRequest request)
    {
        if (request.Dataset != EvalDataset.Retrieval || request.RelevantChunkIds is not { Count: > 0 } chunks)
        {
            return Domains.Billing;
        }
        var calls = JsonSerializer.Deserialize<List<ToolCallRecord>>(turn.ToolCallsJson, Json) ?? [];
        var portfolioDocs = calls.Where(c => c.ToolName == Domains.SearchTool[Domains.Portfolio]).SelectMany(c => c.DocIds).ToHashSet();
        var inPortfolio = chunks.Count(id => portfolioDocs.Contains(id.Split('#')[0]));
        return inPortfolio == 0 ? Domains.Billing : inPortfolio == chunks.Count ? Domains.Portfolio : null;
    }

    public static (JsonObject? Row, string? Error) BuildRow(TurnRow turn, LabelRequest request, string? domain = Domains.Billing)
    {
        if (domain is null)
        {
            return (null, "the relevant chunks come from both domains; label billing and portfolio chunks separately.");
        }
        var id = $"fb-{request.Dataset}-{turn.Id}";
        switch (request.Dataset)
        {
            case EvalDataset.Selection when request.ExpectedTools is not null:
                return (new JsonObject
                {
                    ["id"] = id, ["question"] = turn.Question, ["expectedTools"] = Array(request.ExpectedTools),
                    ["category"] = "feedback", ["tenantId"] = turn.TenantId, ["source"] = "feedback",
                }, null);
            case EvalDataset.Retrieval when request.RelevantChunkIds is { Count: > 0 }:
                var retrieval = new JsonObject
                {
                    ["id"] = id, ["query"] = turn.Question, ["relevantChunkIds"] = Array(request.RelevantChunkIds),
                    ["tenantId"] = turn.TenantId, ["source"] = "feedback",
                };
                if (domain != Domains.Billing)
                {
                    retrieval["domain"] = domain;
                }
                return (retrieval, null);
            case EvalDataset.Generation when !string.IsNullOrWhiteSpace(request.ReferenceAnswer):
                return (new JsonObject
                {
                    ["id"] = id, ["question"] = turn.Question, ["referenceAnswer"] = request.ReferenceAnswer,
                    ["expectedDocIds"] = Array(request.ExpectedDocIds ?? []), ["tenantId"] = turn.TenantId, ["source"] = "feedback",
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
