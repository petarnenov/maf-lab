using System.Text.Json;
using System.Text.Json.Nodes;
using Maf.Lab.Domain.Feedback;
using Maf.Lab.Domain.Tenancy;

namespace Maf.Lab.Plugins.FeedbackReview;

public static class FeedbackReviewEndpoints
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapFeedbackReview(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/admin/feedback").RequireAuthorization(PolicyNames.TenantAdmin);
        admin.MapGet("/queue", async Task<IResult> (IFeedbackReviewStore store, CancellationToken ct) =>
        {
            var items = new List<ReviewQueueItem>();
            foreach (var t in await store.FlaggedAsync(ct))
            {
                var calls = JsonSerializer.Deserialize<List<ToolCallRecord>>(t.ToolCallsJson, Json) ?? [];
                var sources = JsonSerializer.Deserialize<List<ReviewSource>>(t.SourcesJson, Json) ?? [];
                var resolved = new List<ToolCallRecord>();
                foreach (var call in calls)
                {
                    var own = sources.Where(source => call.DocIds.Contains(source.DocId)).ToList();
                    var ids = await store.ResolveChunkIdsAsync(call.ToolName, own, ct);
                    resolved.Add(ids is null ? call : call with
                    {
                        ChunkIds = ids.Where(id => call.DocIds.Any(doc => id.StartsWith(doc + "#", StringComparison.Ordinal))).ToList(),
                    });
                }
                items.Add(new ReviewQueueItem(t.Id, t.ConversationId, t.UserId, t.Question, t.Answer,
                    JsonSerializer.Deserialize<List<string>>(t.SignalsJson, Json) ?? [], resolved,
                    t.FeedbackKinds, t.CreatedAt, t.Labeled));
            }
            return Results.Ok(items);
        });
        admin.MapPost("/{turnId}/label", async Task<IResult> (string turnId, LabelRequest request, IFeedbackReviewStore store,
            IAppendEvalDataset datasets, CancellationToken ct) =>
        {
            var turn = await store.FindAsync(turnId, ct);
            if (turn is null) return Results.NotFound();
            var (row, error) = BuildRow(turn, request, RetrievalDomain(turn, request, store.DomainOf));
            if (row is null) return Results.ValidationProblem(new Dictionary<string, string[]> { ["label"] = [error!] });
            await datasets.AppendAsync(request.Dataset, row, ct);
            return await store.LabelAsync(turnId, request.Dataset, row, ct) ? Results.NoContent() : Results.NotFound();
        });
        return app;
    }

    /// <summary>Builds the dataset row for a labeled turn. Row ids are stable per turn and dataset.</summary>
    /// <summary>
    /// The domain whose search found the chunks a retrieval label names — billing unless every labelled chunk belongs to a
    /// document a portfolio search returned. Null when they came from both: one row is scored against one collection.
    /// </summary>
    internal static string? RetrievalDomain(ReviewStoredTurn turn, LabelRequest request, Func<string, string?> domainOf)
    {
        if (request.Dataset != EvalDataset.Retrieval || request.RelevantChunkIds is not { Count: > 0 } chunks)
        {
            return "billing";
        }
        var calls = JsonSerializer.Deserialize<List<ToolCallRecord>>(turn.ToolCallsJson, Json) ?? [];
        var portfolioDocs = calls.Where(c => domainOf(c.ToolName) == "portfolio").SelectMany(c => c.DocIds).ToHashSet();
        var inPortfolio = chunks.Count(id => portfolioDocs.Contains(id.Split('#')[0]));
        return inPortfolio == 0 ? "billing" : inPortfolio == chunks.Count ? "portfolio" : null;
    }

    public static (JsonObject? Row, string? Error) BuildRow(ReviewStoredTurn turn, LabelRequest request, string? domain = "billing")
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
                if (domain != "billing")
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

}
