using System.Net.ServerSentEvents;
using System.Threading.Channels;
using Maf.Lab.Api.Agent;
using Maf.Lab.Domain.Chat;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Retrieval.Auth;

namespace Maf.Lab.Api.Endpoints;

public static class ChatEndpoints
{
    public const int MaxMessageChars = 4000;

    public static IEndpointRouteBuilder MapChat(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api").RequireAuthorization();

        api.MapGet("/me", (IPrincipalAccessor principals) =>
        {
            var p = principals.Current;
            return Results.Ok(new { p.UserId, FirmId = p.FirmId.Value, Role = p.Role.ToString(), p.AllowedAdvisorIds });
        });

        api.MapPost("/conversations", async (IPrincipalAccessor principals, ConversationService conversations, CancellationToken ct) =>
            Results.Created((string?)null, new ConversationCreated(await conversations.CreateAsync(principals.Current, ct))));

        api.MapPost("/chat", async (ChatRequest request, HttpContext http, IPrincipalAccessor principals, ConversationService conversations,
            ChatTurnRunner runner, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Message) || request.Message.Length > MaxMessageChars)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["message"] = [$"message is required (max {MaxMessageChars} characters)."] });
            }
            var principal = principals.Current;
            var conversationId = await conversations.ResolveAsync(principal, request.ConversationId, ct);
            if (conversationId is null)
            {
                return Results.NotFound();
            }
            var token = http.Request.Headers.Authorization.ToString()["Bearer ".Length..].Trim();
            return TypedResults.ServerSentEvents(Stream(runner, principal, token, conversationId, request.Message.Trim(), ct));
        });

        return app;
    }

    private static async IAsyncEnumerable<SseItem<object>> Stream(ChatTurnRunner runner, Principal principal, string token, string conversationId,
        string message, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var channel = Channel.CreateUnbounded<ChatEvent>(new UnboundedChannelOptions { SingleReader = true });
        var run = Task.Run(async () =>
        {
            try
            {
                await runner.RunAsync(principal, token, conversationId, message, channel.Writer, ct);
            }
            finally
            {
                channel.Writer.TryComplete();
            }
        }, ct);

        await foreach (var ev in channel.Reader.ReadAllAsync(ct))
        {
            yield return new SseItem<object>(ev, ev.EventName);
        }
        await run;
    }
}
