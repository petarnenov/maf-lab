using System.Text.Json;
using Maf.Lab.Api.Storage;
using Maf.Lab.Domain.Feedback;
using Maf.Lab.Retrieval.Auth;
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

        return app;
    }
}
