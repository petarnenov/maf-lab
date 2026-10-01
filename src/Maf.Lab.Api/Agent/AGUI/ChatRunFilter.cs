using AGUI.Abstractions;
using Maf.Lab.Retrieval.Auth;

namespace Maf.Lab.Api.Agent.AGUI;

/// <summary>
/// What is decided about a chat run before the official server starts it (agui-protocol-only): a request it cannot
/// serve is refused with a status code and no run, as before. The thread becomes the caller's conversation; a run id is
/// well formed, since it names the turn the run records; a turn has a message of reasonable length; and a rejoin names a
/// run of this caller's thread that is still kept.
/// </summary>
public sealed class ChatRunFilter : IEndpointFilter
{
    public const int MaxMessageChars = 4000;

    /// <summary>Where the run's thread and id are left for whatever watches the run's events (<see cref="RunTap"/>).</summary>
    public const string RunKey = "maf-lab.chat-run";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (context.Arguments.OfType<RunAgentInput>().FirstOrDefault() is not { } input)
        {
            return Results.BadRequest();
        }
        var services = context.HttpContext.RequestServices;
        var principal = services.GetRequiredService<IPrincipalAccessor>().Current;
        var ct = context.HttpContext.RequestAborted;

        if (string.IsNullOrWhiteSpace(input.RunId))
        {
            input.RunId = $"r_{Guid.NewGuid():N}";
        }
        else if (!ConversationService.ThreadIdPattern().IsMatch(input.RunId))
        {
            return Invalid("runId", "a run id is 1–64 letters, digits, '-' or '_'.");
        }

        var resume = input.Resume?.FirstOrDefault();
        var message = input.Messages?.OfType<AGUIUserMessage>().LastOrDefault()?.Content.ToString()?.Trim();
        var rejoin = resume is null && !string.IsNullOrWhiteSpace(input.ParentRunId) && string.IsNullOrEmpty(message);
        if (resume is null && !rejoin && (string.IsNullOrWhiteSpace(message) || message.Length > MaxMessageChars))
        {
            return Invalid("messages", $"a user message is required (max {MaxMessageChars} characters).");
        }

        var conversationId = await services.GetRequiredService<ConversationService>().ResolveOrClaimAsync(principal, input.ThreadId, ct);
        if (conversationId is null)
        {
            return Results.NotFound();
        }
        input.ThreadId = conversationId;

        // A run of someone else's thread is not found, exactly as that thread is not; nor is a run nobody keeps any more.
        if (rejoin && await services.GetRequiredService<RunRejoin>().FindAsync(principal, conversationId, input.ParentRunId!, ct) is null)
        {
            return Results.NotFound();
        }

        context.HttpContext.Items[RunKey] = new ChatRun(principal, conversationId, input.RunId, resume is not null || rejoin);
        return await next(context);
    }

    private static IResult Invalid(string field, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });
}

/// <summary>A run the filter let through, for whatever watches its events.</summary>
/// <param name="RecordsNoTurn">An answer to a question or a rejoin: the run records no turn of its own.</param>
public sealed record ChatRun(Maf.Lab.Domain.Tenancy.Principal Principal, string ConversationId, string RunId, bool RecordsNoTurn);
