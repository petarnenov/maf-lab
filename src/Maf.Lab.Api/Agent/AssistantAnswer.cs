using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Retrieval.Auth;
using Maf.Lab.Domain.Configuration;
using Maf.Lab.Retrieval.Configuration;
using Maf.Lab.Retrieval.Models;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace Maf.Lab.Api.Agent;

/// <summary>
/// A question relayed from outside the chat (an A2A partner's, extract-a2a) is answered by the assistant itself — the
/// same agent, the same system prompt and the same MCP tools the chat UI talks to. A partner therefore gets the real answer, sourced the real
/// way, rather than a second implementation that would drift from it.
///
/// The tenant comes from the principal the relaying plugin built (a partner's registration) and nowhere else: the agent
/// runs under a token minted for it, so the MCP server scopes every query itself, exactly as it does for a user.
///
/// The Agent Framework ships a bridge for this (<c>Microsoft.Agents.AI.Hosting.A2A</c>), but in
/// 1.22.0-preview its handler is internal and reachable only through <c>MapA2AJsonRpc</c>, which takes an agent
/// instead of a handler — and a handler is what the task lifecycle needs (see DECISIONS.md). The agent is run
/// directly instead, through the same <see cref="AIAgent"/> abstraction that bridge would have used.
/// </summary>
public sealed class AssistantAnswer(
    IChatClientFactory models,
    IToolSource tools,
    SystemPrompt prompt,
    Guardrail guardrail,
    IOptions<AuthOptions> auth,
    ILoggerFactory loggers) : Maf.Lab.Plugins.Abstractions.IAssistantAnswer
{
    /// <summary>The assistant's answer for the principal; the relaying protocol frames it.</summary>
    public async Task<string> AnswerAsync(Principal principal, string question, CancellationToken ct)
    {
        // A partner is screened like a user: a question that tries to steer the assistant gets the fixed refusal and
        // never reaches the model. Unscreened (Jev down) fails open, as a user's prompt does.
        var screen = await guardrail.ScreenPromptAsync(question, ct);
        guardrail.Trace(null, Guardrail.CheckPartner, null, null, screen.Decision, screen.Threshold,
            [new ScreenedItem(0, screen.Decision, screen.Scores)], 0, screen.Reason);
        if (screen.Blocked)
        {
            return Guardrail.Refusal(question);
        }

        // Not a user's token and not a user's entitlements: the principal the relaying plugin built, typically read-only
        // for the one firm a partner may see.
        var (token, _) = DevJwt.Issue(auth.Value, principal.UserId, principal.TenantId, principal.Role);
        await using var toolSet = await tools.GetToolsAsync(token, null, ct);

        var chatOptions = models.BaseChatOptions();
        chatOptions.Instructions = prompt.Text;
        chatOptions.Tools = [.. toolSet.Tools];

        var agent = new ChatClientAgent(
                new CitationMarkerChatClient(models.CreateChatClient()),
                new ChatClientAgentOptions { Name = "maf-lab-assistant", ChatOptions = chatOptions },
                loggers)
            .AsBuilder()
            .Use(ScreenToolResultAsync)
            .Build();

        var session = await agent.CreateSessionAsync(ct);
        var response = await agent.RunAsync(question, session, cancellationToken: ct);
        return response.Text;
    }

    /// <summary>
    /// What a tool returns on this path is judged and framed exactly as in a chat turn: flagged items are withheld, and
    /// the rest reaches the model as data, never as instructions.
    /// </summary>
    private async ValueTask<object?> ScreenToolResultAsync(AIAgent agent, FunctionInvocationContext context,
        Func<FunctionInvocationContext, CancellationToken, ValueTask<object?>> next, CancellationToken ct)
    {
        var name = context.Function.Name;
        var result = await next(context, ct);
        var (payload, structured, isError) = ToolDataEnvelope.Unpack(result);
        if (await guardrail.ScreenToolResultAsync(name, payload, structured, isError, ct) is { } screened)
        {
            guardrail.Trace(null, Guardrail.CheckToolResult, name, context.CallContent?.CallId, screened.Decision, screened.Threshold,
                screened.Items, screened.Withheld, null, screened.Requests, screened.ElapsedMs);
            payload = screened.Payload;
        }
        return ToolDataEnvelope.Wrap(name, payload);
    }
}
