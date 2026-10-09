using Maf.Lab.TestGen;
using Microsoft.Extensions.AI;

namespace Maf.Lab.TestAgent;

/// <summary>The run's token or cost cap was crossed during a model call.</summary>
public sealed class BudgetExceededException() : Exception("The run's budget is spent.");

/// <summary>
/// Counts every model call's usage against the run's caps, and stops the run as soon as a call crosses either. It
/// sits directly on the provider client, so every call the tool loop makes is counted.
/// </summary>
public sealed class BudgetedChatClient(IChatClient inner, RunUsage usage) : DelegatingChatClient(inner)
{
    public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        usage.ThrowIfSpent();
        var response = await base.GetResponseAsync(messages, options, cancellationToken);
        usage.Add(response.Usage);
        usage.ThrowIfSpent();
        return response;
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
        ChatOptions? options = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        usage.ThrowIfSpent();
        await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken))
        {
            foreach (var content in update.Contents.OfType<UsageContent>())
            {
                usage.Add(content.Details);
            }
            yield return update;
        }
        usage.ThrowIfSpent();
    }
}

/// <summary>What the run has used so far, and what it may use.</summary>
public sealed class RunUsage(TestGenBudget? caps, ModelPrice price)
{
    private readonly TestGenBudget budget = caps ?? TestGenBudget.Unlimited;
    private long _input;
    private long _output;

    public long InputTokens => Interlocked.Read(ref _input);
    public long OutputTokens => Interlocked.Read(ref _output);
    public long Tokens => InputTokens + OutputTokens;
    public double CostUsd => AttemptEstimate.Cost(InputTokens, OutputTokens, price.InputPerMTok, price.OutputPerMTok);

    public void Add(UsageDetails? details)
    {
        if (details is null)
        {
            return;
        }
        Interlocked.Add(ref _input, details.InputTokenCount ?? 0);
        Interlocked.Add(ref _output, details.OutputTokenCount ?? 0);
    }

    /// <summary>What a task used before a restart: a resumed task's caps count it.</summary>
    public void Restore(TestGenUsage used)
    {
        Interlocked.Add(ref _input, used.InputTokens);
        Interlocked.Add(ref _output, used.OutputTokens);
    }

    // A null cap compares false, so an unset cap never stops the run.
    public bool Spent => Tokens > budget.MaxTokens || CostUsd > budget.MaxCostUsd;

    public void ThrowIfSpent()
    {
        if (Spent)
        {
            throw new BudgetExceededException();
        }
    }

    /// <summary>Whether one more attempt of this size would cross a cap: checked before an attempt starts.</summary>
    public bool WouldExceed(long input, long output) =>
        Tokens + input + output > budget.MaxTokens
        || AttemptEstimate.Cost(InputTokens + input, OutputTokens + output, price.InputPerMTok, price.OutputPerMTok) > budget.MaxCostUsd;

    public TestGenUsage Snapshot() => new(InputTokens, OutputTokens, CostUsd);
}
