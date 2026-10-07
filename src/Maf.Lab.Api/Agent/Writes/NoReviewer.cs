using Maf.Lab.Plugins.Abstractions;

namespace Maf.Lab.Api.Agent.Writes;

/// <summary>
/// The reviewer-consultation port when no installed plugin provides a reviewer (a Null Object, extract-compliance-plugin):
/// a write flow that needs one still resolves, and is told the reviewer cannot be reached. A tool that requires a reviewer
/// is not offered without one anyway (its <c>tool_requires</c>), so this answers only a flow that asks regardless.
/// </summary>
public sealed class NoReviewer : IReviewerConsultation
{
    public const string Reason = "no reviewer is installed";

    public Task<ConsultationResult> ReviewAsync(ReviewRequest request, CancellationToken ct) =>
        Task.FromResult<ConsultationResult>(new ConsultationResult.Unreachable(Reason));

    public Task<ConsultationResult> AnswerAsync(ReviewRequest request, string taskId, string justification, CancellationToken ct) =>
        ReviewAsync(request, ct);
}
