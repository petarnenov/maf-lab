using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace Maf.Lab.TestSupport;

/// <summary>
/// IChatClient whose replies are decided by a script over the conversation so far. Records every request
/// (messages and options) so tests can assert tool mode, instructions and history.
/// </summary>
public sealed class ScriptedChatClient(Func<IReadOnlyList<ChatMessage>, ChatOptions?, int, ChatResponseUpdate[]> script) : IChatClient
{
    public List<(List<ChatMessage> Messages, ChatOptions? Options)> Requests { get; } = [];

    /// <summary>
    /// When set, a streamed answer stops after this many words of text and waits until its call is cancelled — a paid
    /// model still answering when the person stops the run (stop-anything).
    /// </summary>
    public int? HoldAfterWords { get; set; }

    /// <summary>Set once a call is being held.</summary>
    public TaskCompletionSource Holding { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Set once a held call's token fired: the caller cancelled the call rather than waiting for it.</summary>
    public TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>How many updates every call has yielded so far.</summary>
    public int Yielded => yielded;

    private int yielded;

    public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var updates = new List<ChatResponseUpdate>();
        await foreach (var u in GetStreamingResponseAsync(messages, options, cancellationToken))
        {
            updates.Add(u);
        }
        return updates.ToChatResponse();
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var list = messages.ToList();
        Requests.Add((list, options));
        var words = 0;
        foreach (var update in script(list, options, Requests.Count))
        {
            await Task.Yield();
            if (HoldAfterWords is { } hold && !string.IsNullOrEmpty(update.Text) && words++ == hold)
            {
                Holding.TrySetResult();
                try
                {
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    Cancelled.TrySetResult();
                    throw;
                }
            }
            Interlocked.Increment(ref yielded);
            yield return update;
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }

    public static ChatResponseUpdate[] Text(string text) =>
        text.Split(' ').Select((w, i) => new ChatResponseUpdate(ChatRole.Assistant, (i == 0 ? "" : " ") + w)).ToArray();

    /// <summary>What a reasoning model streams before it answers, one update per word.</summary>
    public static ChatResponseUpdate[] Thinking(string text) =>
        text.Split(' ').Select((w, i) => new ChatResponseUpdate(ChatRole.Assistant,
            (IList<AIContent>)[new TextReasoningContent((i == 0 ? "" : " ") + w)])).ToArray();

    public static ChatResponseUpdate[] Call(string tool, Dictionary<string, object?> args, string? callId = null) =>
        [new ChatResponseUpdate(ChatRole.Assistant, [new FunctionCallContent(callId ?? $"call_{tool}_{Guid.NewGuid():N}"[..20], tool, args)])];

    /// <summary>True once the conversation already contains a result for the given tool.</summary>
    public static bool HasResult(IReadOnlyList<ChatMessage> messages, string tool)
    {
        var callIds = messages.SelectMany(m => m.Contents).OfType<FunctionCallContent>().Where(c => c.Name == tool).Select(c => c.CallId).ToHashSet();
        return messages.SelectMany(m => m.Contents).OfType<FunctionResultContent>().Any(r => callIds.Contains(r.CallId));
    }
}
