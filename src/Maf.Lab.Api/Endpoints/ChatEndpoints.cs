using System.Net.ServerSentEvents;
using System.Threading.Channels;
using AGUI.Abstractions;
using Maf.Lab.Api.Agent;
using Maf.Lab.Api.Agent.Streaming;
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

        // A run of the agent. The thread is the conversation; the run is this turn. A resume answers something
        // a previous run stopped for, which is how a write gets its approval.
        api.MapPost("/chat", async (RunAgentInput input, HttpContext http, IPrincipalAccessor principals,
            ConversationService conversations, ChatTurnRunner runner, ConfirmationService confirmations,
            RunRegistry runs, CancellationToken ct) =>
        {
            var principal = principals.Current;
            var message = LastUserMessage(input);
            var resume = input.Resume?.FirstOrDefault();

            if (resume is null && (string.IsNullOrWhiteSpace(message) || message.Length > MaxMessageChars))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["messages"] = [$"a user message is required (max {MaxMessageChars} characters)."],
                });
            }

            var conversationId = await conversations.ResolveAsync(principal, input.ThreadId, ct);
            if (conversationId is null)
            {
                return Results.NotFound();
            }

            var runId = string.IsNullOrWhiteSpace(input.RunId) ? $"r_{Guid.NewGuid():N}" : input.RunId;
            var token = http.Request.Headers.Authorization.ToString()["Bearer ".Length..].Trim();

            return TypedResults.ServerSentEvents(
                Stream(runner, confirmations, runs, principal, token, conversationId, runId, message?.Trim() ?? "", resume, ct));
        });

        // Stopping a run this caller started, wherever it is running. Abandoning the stream does the same thing
        // through the request itself, which is what a browser actually does.
        api.MapPost("/chat/{runId}/stop", async (string runId, HttpContext http, RunStopper stopper, CancellationToken ct) =>
        {
            var localOnly = http.Request.Query[RunStopper.LocalOnlyQuery] == "true";
            var token = http.Request.Headers.Authorization.ToString()["Bearer ".Length..].Trim();
            return await stopper.StopAsync(runId, token, localOnly, ct) ? Results.Accepted() : Results.NotFound();
        });

        return app;
    }

    /// <summary>The turn's question: the last thing the user said. Parts are read as their text, in order.</summary>
    private static string? LastUserMessage(RunAgentInput input) =>
        input.Messages?.OfType<AGUIUserMessage>().LastOrDefault() is { } last ? last.Content.ToString() : null;

    private static async IAsyncEnumerable<SseItem<object>> Stream(
        ChatTurnRunner runner, ConfirmationService confirmations, RunRegistry runs, Principal principal, string token,
        string conversationId, string runId, string message, AGUIResume? resume,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var channel = Channel.CreateUnbounded<BaseEvent>(new UnboundedChannelOptions { SingleReader = true });
        using var registration = runs.Start(runId, ct);

        var run = Task.Run(async () =>
        {
            try
            {
                if (resume is not null)
                {
                    await confirmations.ResumeAsync(principal, token, conversationId, runId, resume, channel.Writer, registration.Token);
                }
                else
                {
                    await runner.RunAsync(principal, token, conversationId, message, runId, channel.Writer, registration.Token);
                }
            }
            catch (OperationCanceledException)
            {
                // A stopped run still ends once, and says so: the client asked, and is still listening.
                channel.Writer.TryWrite(new RunFinishedEvent
                {
                    ThreadId = conversationId,
                    RunId = runId,
                    Outcome = new RunFinishedCancelledOutcome(),
                });
            }
            finally
            {
                channel.Writer.TryComplete();
            }
        }, registration.Token);

        await foreach (var ev in channel.Reader.ReadAllAsync(ct))
        {
            yield return new SseItem<object>(ev, AGUIStream.FrameName(ev));
        }

        try
        {
            await run;
        }
        catch (OperationCanceledException)
        {
            // Stopped, or the client walked away. Either way the run is over and the stream ends here.
        }
    }
}
