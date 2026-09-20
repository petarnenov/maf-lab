using Maf.Lab.A2A;
using A2A;
using Maf.Lab.Api.Agent;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Retrieval.Auth;
using Maf.Lab.Domain.Configuration;
using Maf.Lab.Retrieval.Configuration;
using Maf.Lab.Retrieval.Models;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Options;
using MessageRole = A2A.Role;
using UserRole = Maf.Lab.Domain.Tenancy.Role;

namespace Maf.Lab.Api.A2A;

/// <summary>
/// Anything that is not a billing run is answered by the assistant itself — the same agent, the same system
/// prompt and the same MCP tools the chat UI talks to. A partner therefore gets the real answer, sourced the real
/// way, rather than a second implementation that would drift from it.
///
/// The tenant still comes from the partner's registration and nowhere else: the agent runs under a token minted
/// for that firm, so the MCP server scopes every query itself, exactly as it does for a user.
///
/// The Agent Framework ships a bridge for this (<c>Microsoft.Agents.AI.Hosting.A2A</c>), but in
/// 1.22.0-preview its handler is internal and reachable only through <c>MapA2AJsonRpc</c>, which takes an agent
/// instead of a handler — and a handler is what the task lifecycle needs (see DECISIONS.md). The agent is run
/// directly instead, through the same <see cref="AIAgent"/> abstraction that bridge would have used.
/// </summary>
public sealed class AssistantBridge(
    IChatClientFactory models,
    IToolSource tools,
    SystemPrompt prompt,
    IOptions<AuthOptions> auth,
    ILoggerFactory loggers)
{
    /// <summary>A partner asks; the assistant answers with a message, as the protocol allows for work that is quick.</summary>
    public async Task AnswerAsync(TenantId firm, string question, AgentEventQueue queue, CancellationToken ct)
    {
        // Not a user's token and not a user's entitlements: a read-only principal for the firm this partner may see.
        var (token, _) = DevJwt.Issue(auth.Value, $"a2a:{firm.Value}", firm, UserRole.READ_ONLY, []);
        await using var toolSet = await tools.GetToolsAsync(token, null, ct);

        var chatOptions = models.BaseChatOptions();
        chatOptions.Instructions = prompt.Text;
        chatOptions.Tools = [.. toolSet.Tools];

        var agent = new ChatClientAgent(
            models.CreateChatClient(),
            new ChatClientAgentOptions { Name = "maf-lab-assistant", ChatOptions = chatOptions },
            loggers);

        var session = await agent.CreateSessionAsync(ct);
        var response = await agent.RunAsync(question, session, cancellationToken: ct);

        await queue.EnqueueMessageAsync(
            new Message
            {
                MessageId = Guid.NewGuid().ToString("N"),
                Role = MessageRole.Agent,
                Parts = [new Part { Text = response.Text }],
            }, ct);
        queue.Complete();
    }
}
