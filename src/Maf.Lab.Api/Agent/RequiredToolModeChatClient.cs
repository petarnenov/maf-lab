using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace Maf.Lab.Api.Agent;

/// <summary>
/// Makes <c>ChatToolMode.RequireSpecific("search_documents")</c> effective on providers that ignore tool_choice
/// (Ollama, including Ollama Cloud): while the required tool has not been called in this request, the call is issued
/// on the model's behalf with the user's question as the query. FunctionInvokingChatClient (above this client)
/// executes it through MCP and clears the required mode for the next iteration, so the model then answers — or calls
/// further tools — with the retrieved snippets in context.
/// A data turn Jev routed to a read tool (<paramref name="route"/>) is issued the same way, with the route's arguments:
/// the model's tool-choosing call is skipped and its first call is the answer.
/// A question in scope for several domains forces each domain's search (<paramref name="forcedSearches"/>): they are
/// issued together, as parallel calls of one assistant message, so the turn reads both sides of the boundary before the
/// model says a word (add-portfolio-domain).
/// </summary>
/// <paramref name="alongside"/> are read calls issued with those searches, arguments taken from the question — the run a
/// mixed question names, whose state the answer needs as much as the documentation.
public sealed class RequiredToolModeChatClient(IChatClient inner, Action<FunctionCallContent>? onForced = null, Jev.ToolRoute? route = null,
    IReadOnlyList<string>? forcedSearches = null, IReadOnlyList<Jev.ToolRoute>? alongside = null)
    : DelegatingChatClient(inner)
{
    public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var list = messages as IList<ChatMessage> ?? messages.ToList();
        if (ForcedCalls(list, options) is { Count: > 0 } calls)
        {
            calls.ForEach(c => onForced?.Invoke(c));
            return new ChatResponse(new ChatMessage(ChatRole.Assistant, [.. calls])) { FinishReason = ChatFinishReason.ToolCalls };
        }
        return await base.GetResponseAsync(list, options, cancellationToken);
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var list = messages as IList<ChatMessage> ?? messages.ToList();
        if (ForcedCalls(list, options) is { Count: > 0 } calls)
        {
            calls.ForEach(c => onForced?.Invoke(c));
            yield return new ChatResponseUpdate(ChatRole.Assistant, [.. calls]) { FinishReason = ChatFinishReason.ToolCalls };
            yield break;
        }
        await foreach (var update in base.GetStreamingResponseAsync(list, options, cancellationToken))
        {
            yield return update;
        }
    }

    private List<FunctionCallContent>? ForcedCalls(IList<ChatMessage> messages, ChatOptions? options)
    {
        if (options?.ToolMode is not RequiredChatToolMode { RequiredFunctionName: { } required })
        {
            return null;
        }
        var called = messages.SelectMany(m => m.Contents).OfType<FunctionCallContent>().Select(c => c.Name).ToHashSet(StringComparer.Ordinal);
        if (!Domains.IsSearch(required))
        {
            return !called.Contains(required) && route is { } r && r.Tool == required
                ? [new FunctionCallContent($"routed_{Guid.NewGuid():N}"[..20], r.Tool, r.Arguments.ToDictionary(a => a.Key, a => a.Value))]
                : null;
        }
        // The searches of every domain in scope go out together, once: any of them already called means they were issued.
        var searches = forcedSearches is { Count: > 0 } f && f.Contains(required) ? f : [required];
        if (searches.Any(called.Contains))
        {
            return null;
        }
        var question = messages.LastOrDefault(m => m.Role == ChatRole.User)?.Text?.Trim();
        return string.IsNullOrEmpty(question)
            ? null
            : [
                .. searches.Select(tool => new FunctionCallContent($"forced_{Guid.NewGuid():N}"[..20], tool, new Dictionary<string, object?> { ["query"] = question })),
                .. (alongside ?? []).Where(r => !called.Contains(r.Tool))
                    .Select(r => new FunctionCallContent($"forced_{Guid.NewGuid():N}"[..20], r.Tool, r.Arguments.ToDictionary(a => a.Key, a => a.Value))),
            ];
    }
}
