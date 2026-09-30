using System.Runtime.CompilerServices;
using Maf.Lab.TestGen;
using Microsoft.Extensions.AI;

namespace Maf.Lab.TestAgent;

/// <summary>
/// Sits under the tool loop and counts its model calls in the current attempt. On the call where only
/// <see cref="Instructions.NudgeAtRoundsLeft"/> tool rounds remain, it adds one fixed message telling the model to
/// write now, so an attempt is not spent reading until the round cap cuts it off. The message holds no source or
/// model text, and nothing about it is logged.
/// </summary>
public sealed class RoundNudgeChatClient(IChatClient inner, int roundCap) : DelegatingChatClient(inner)
{
    private int _calls;

    /// <summary>A new attempt: its rounds are counted from zero.</summary>
    public void BeginAttempt() => Interlocked.Exchange(ref _calls, 0);

    public override Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        base.GetResponseAsync(WithNudge(messages), options, cancellationToken);

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
        ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var update in base.GetStreamingResponseAsync(WithNudge(messages), options, cancellationToken))
        {
            yield return update;
        }
    }

    private IEnumerable<ChatMessage> WithNudge(IEnumerable<ChatMessage> messages)
    {
        // Rounds already used are the calls before this one.
        var left = roundCap - (Interlocked.Increment(ref _calls) - 1);
        return left == Instructions.NudgeAtRoundsLeft
            ? [.. messages, new ChatMessage(ChatRole.User, Instructions.Nudge(left))]
            : messages;
    }
}
