using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace Maf.Lab.Api.Agent;

/// <summary>
/// Makes <c>ChatToolMode.RequireSpecific("search_documents")</c> effective on providers that ignore tool_choice
/// (Ollama, including Ollama Cloud): while the required tool has not been called in this request, the call is issued
/// on the model's behalf with the user's question as the query. FunctionInvokingChatClient (above this client)
/// executes it through MCP and clears the required mode for the next iteration, so the model then answers — or calls
/// further tools — with the retrieved snippets in context.
/// </summary>
public sealed class RequiredToolModeChatClient(IChatClient inner, Action<FunctionCallContent>? onForced = null) : DelegatingChatClient(inner)
{
    public const string EmulatedTool = "search_documents";

    public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var list = messages as IList<ChatMessage> ?? messages.ToList();
        if (ForcedCall(list, options) is { } call)
        {
            onForced?.Invoke(call);
            return new ChatResponse(new ChatMessage(ChatRole.Assistant, [call])) { FinishReason = ChatFinishReason.ToolCalls };
        }
        return await base.GetResponseAsync(list, options, cancellationToken);
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var list = messages as IList<ChatMessage> ?? messages.ToList();
        if (ForcedCall(list, options) is { } call)
        {
            onForced?.Invoke(call);
            yield return new ChatResponseUpdate(ChatRole.Assistant, [call]) { FinishReason = ChatFinishReason.ToolCalls };
            yield break;
        }
        await foreach (var update in base.GetStreamingResponseAsync(list, options, cancellationToken))
        {
            yield return update;
        }
    }

    private static FunctionCallContent? ForcedCall(IList<ChatMessage> messages, ChatOptions? options)
    {
        if (options?.ToolMode is not RequiredChatToolMode { RequiredFunctionName: EmulatedTool }
            || messages.SelectMany(m => m.Contents).OfType<FunctionCallContent>().Any(c => c.Name == EmulatedTool))
        {
            return null;
        }
        var question = messages.LastOrDefault(m => m.Role == ChatRole.User)?.Text?.Trim();
        return string.IsNullOrEmpty(question)
            ? null
            : new FunctionCallContent($"forced_{Guid.NewGuid():N}"[..20], EmulatedTool, new Dictionary<string, object?> { ["query"] = question });
    }
}
