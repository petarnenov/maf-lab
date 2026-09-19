using System.Text.Json.Nodes;
using Maf.Lab.Api.Agent.Tracing;
using Maf.Lab.Api.Storage;
using Maf.Lab.Domain.Tracing;
using Microsoft.Agents.AI;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;

namespace Maf.Lab.Api.Agent;

/// <summary>
/// Conversation memory persisted in SQLite, keyed by a server-issued conversation id. Only user and final
/// assistant text are kept (tool calls and tool data are not replayed), and the history sent to the model is
/// the newest messages that fit the token budget.
/// </summary>
public sealed class SqliteChatHistoryProvider(
    IDbContextFactory<MafDbContext> db, TokenCounter tokens, string conversationId, int tokenBudget, TimeProvider time, TurnTrace? trace = null) : ChatHistoryProvider
{
    protected override async ValueTask<IEnumerable<ChatMessage>> ProvideChatHistoryAsync(InvokingContext context, CancellationToken cancellationToken = default)
    {
        await using var ctx = await db.CreateDbContextAsync(cancellationToken);
        var recent = await ctx.Messages
            .Where(m => m.ConversationId == conversationId)
            .OrderByDescending(m => m.Id)
            .Take(50)
            .ToListAsync(cancellationToken);

        var window = new List<MessageRow>();
        var used = 0;
        foreach (var message in recent)
        {
            if (used + message.Tokens > tokenBudget)
            {
                break;
            }
            used += message.Tokens;
            window.Add(message);
        }
        window.Reverse();
        trace?.Add(TraceKinds.History, $"History window: {window.Count} message(s), {used}/{tokenBudget} tokens", new JsonObject
        {
            ["budgetTokens"] = tokenBudget,
            ["usedTokens"] = used,
            ["included"] = new JsonArray(window.Select(m => (JsonNode)new JsonObject { ["role"] = m.Role, ["text"] = m.Text, ["tokens"] = m.Tokens }).ToArray()),
            ["excludedCount"] = recent.Count - window.Count,
        });
        return window.Select(m => new ChatMessage(m.Role == "user" ? ChatRole.User : ChatRole.Assistant, m.Text)).ToList();
    }

    protected override async ValueTask StoreChatHistoryAsync(InvokedContext context, CancellationToken cancellationToken = default)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var rows = context.RequestMessages.Where(m => m.Role == ChatRole.User)
            .Concat((context.ResponseMessages ?? []).Where(m => m.Role == ChatRole.Assistant))
            .Select(m => m.Text?.Trim() ?? "")
            .Zip(context.RequestMessages.Where(m => m.Role == ChatRole.User).Select(_ => "user")
                .Concat((context.ResponseMessages ?? []).Where(m => m.Role == ChatRole.Assistant).Select(_ => "assistant")))
            .Where(x => x.First.Length > 0)
            .Select(x => new MessageRow { ConversationId = conversationId, Role = x.Second, Text = x.First, Tokens = tokens.Count(x.First), CreatedAt = now })
            .ToList();
        if (rows.Count == 0)
        {
            return;
        }
        trace?.Add(TraceKinds.Memory, $"Stored {rows.Count} message(s) in conversation memory", new JsonObject
        {
            ["stored"] = new JsonArray(rows.Select(r => (JsonNode)new JsonObject { ["role"] = r.Role, ["tokens"] = r.Tokens }).ToArray()),
        });
        await using var ctx = await db.CreateDbContextAsync(cancellationToken);
        ctx.Messages.AddRange(rows);
        await ctx.SaveChangesAsync(cancellationToken);
    }
}
