using System.Text.Json;
using Maf.Lab.Api.Agent;
using Maf.Lab.Domain.Chat;
using Maf.Lab.Domain.SharedState;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Retrieval.Auth;

namespace Maf.Lab.Api.Endpoints;

public static class ChatEndpoints
{
    public static IEndpointRouteBuilder MapChat(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api").RequireAuthorization();

        api.MapGet("/me", (IPrincipalAccessor principals) =>
        {
            var p = principals.Current;
            return Results.Ok(new { p.UserId, TenantId = p.TenantId.Value, Role = p.Role.ToString() });
        });

        api.MapPost("/conversations", async (IPrincipalAccessor principals, ConversationService conversations, CancellationToken ct) =>
            Results.Created((string?)null, new ConversationCreated(await conversations.CreateAsync(principals.Current, ct))));

        return app;
    }
}
