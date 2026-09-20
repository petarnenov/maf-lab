namespace Maf.Lab.Api.A2A;

/// <summary>
/// What consulting another agent produced. Every way a remote agent can answer — including not answering — is a
/// value here, because a caller that must handle each case is better served by a type it cannot ignore than by an
/// exception it might not catch.
/// </summary>
public abstract record ConsultationResult
{
    /// <summary>The review reached a decision.</summary>
    public sealed record Verdict(string TaskId, string AdjustmentId, bool Approved, string Reason) : ConsultationResult;

    /// <summary>The reviewer wants something before it decides. The task stays open for the answer.</summary>
    public sealed record QuestionAsked(string TaskId, string Question) : ConsultationResult;

    /// <summary>The deadline passed. The review is still running; the task id is how it is collected later.</summary>
    public sealed record TimedOut(string TaskId) : ConsultationResult;

    /// <summary>The reviewer could not be reached at all — down, misconfigured, or not there.</summary>
    public sealed record Unreachable(string Reason) : ConsultationResult;

    /// <summary>The reviewer answered, with a failure. Its words are a diagnosis, never a verdict.</summary>
    public sealed record Failed(string TaskId, string Reason) : ConsultationResult;

    /// <summary>One word for the audit record: what happened, never what was said.</summary>
    public string Outcome => this switch
    {
        Verdict { Approved: true } => "approved",
        Verdict => "refused",
        QuestionAsked => "input-required",
        TimedOut => "timeout",
        Unreachable => "unreachable",
        _ => "failed",
    };

    /// <summary>The task this concerned, when there was one.</summary>
    public string? TaskIdOrNull => this switch
    {
        Verdict v => v.TaskId,
        QuestionAsked q => q.TaskId,
        TimedOut t => t.TaskId,
        Failed f => f.TaskId,
        _ => null,
    };
}

/// <summary>What the reviewer is asked about. Identifiers and an amount — no prose, because the caller is a program.</summary>
public sealed record FeeAdjustment(string AdjustmentId, string FirmId, string AccountId, decimal Amount, string Reason);
