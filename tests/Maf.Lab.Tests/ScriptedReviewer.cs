using System.Collections.Concurrent;
using Maf.Lab.Plugins.Abstractions;

namespace Maf.Lab.Tests;

/// <summary>
/// A test double at the reviewer-consultation port (extract-compliance-plugin): a write flow is tested against the
/// reviewer's possible answers — approved, refused, a question, a timeout, unreachable, failed — without a reviewer
/// agent, A2A or any plugin's code. A host takes it by registering it as the port (the last registration wins).
/// </summary>
public sealed class ScriptedReviewer : IReviewerConsultation
{
    public const string TaskId = "review-1";

    /// <summary>A review whose amount is above this (either way) is refused, as the reviewer agent's policy does.</summary>
    public decimal RefuseAbove { get; init; } = 1_000m;

    /// <summary>The first review asks for a justification; an answer on its task then decides.</summary>
    public bool AskFirst { get; init; }

    /// <summary>Every review reports the deadline passed.</summary>
    public bool Late { get; init; }

    /// <summary>A fixed answer to every review and every answer, when a test needs one result exactly.</summary>
    public Func<ReviewRequest, ConsultationResult>? Answer { get; init; }

    /// <summary>What was asked, in order: the request, and for an answer the task and the justification.</summary>
    public ConcurrentQueue<(ReviewRequest Request, string? TaskId, string? Justification)> Calls { get; } = new();

    public Task<ConsultationResult> ReviewAsync(ReviewRequest request, CancellationToken ct)
    {
        Calls.Enqueue((request, null, null));
        return Task.FromResult(Answer?.Invoke(request)
            ?? (Late ? new ConsultationResult.TimedOut(TaskId)
                : AskFirst ? new ConsultationResult.QuestionAsked(TaskId, "Why is this adjustment needed?")
                : Decide(request)));
    }

    public Task<ConsultationResult> AnswerAsync(ReviewRequest request, string taskId, string justification, CancellationToken ct)
    {
        Calls.Enqueue((request, taskId, justification));
        return Task.FromResult(Answer?.Invoke(request) ?? Decide(request));
    }

    private ConsultationResult Decide(ReviewRequest request) => Math.Abs(request.Amount) > RefuseAbove
        ? new ConsultationResult.Verdict(TaskId, request.AdjustmentId, false, "Above the reviewer's limit.")
        : new ConsultationResult.Verdict(TaskId, request.AdjustmentId, true, "Within policy.");
}
