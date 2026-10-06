using Maf.Lab.Domain.History;
using Maf.Lab.Plugins.Abstractions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Maf.Lab.Plugins.ConversationHistory;

/// <summary>The list's three routes: a page of the caller's conversations, a rename and a delete. The core's store decides whose.</summary>
public static class ConversationEndpoints
{
    public const int DefaultPageSize = 30;

    public static void Map(IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/conversations").RequireAuthorization();

        api.MapGet("", async (string? search, int? limit, string? before, IConversationStore store, CancellationToken ct) =>
            Results.Ok(await store.PageAsync(search, limit ?? DefaultPageSize, before, ct)));

        api.MapPatch("/{id}", async (string id, RenameConversationRequest request, IConversationStore store, CancellationToken ct) =>
            ConversationTitles.Validate(request.Title) is not { } title
                ? Invalid()
                : await store.RenameAsync(id, title, ct) switch
                {
                    RenameOutcome.Renamed => Results.NoContent(),
                    RenameOutcome.NotFound => Results.NotFound(),
                    _ => Invalid(),
                });

        api.MapDelete("/{id}", async (string id, IConversationStore store, CancellationToken ct) =>
            await store.DeleteAsync(id, ct) ? Results.NoContent() : Results.NotFound());
    }

    private static IResult Invalid() =>
        Results.ValidationProblem(new Dictionary<string, string[]> { ["title"] = [$"title must be 1–{ConversationTitles.MaxChars} characters."] });
}
