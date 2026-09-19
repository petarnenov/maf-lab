namespace Maf.Lab.Domain.Feedback;

public static class FeedbackKind
{
    public const string WrongTool = "wrong_tool";
    public const string WrongDocument = "wrong_document";
    public const string WrongAnswer = "wrong_answer";

    public static bool IsKnown(string? value) => value is WrongTool or WrongDocument or WrongAnswer;
}

/// <summary>Production signals that put a turn into the review queue.</summary>
public static class TurnSignal
{
    public const string NegativeFeedback = "negative_feedback";
    public const string Rephrased = "rephrased";
    public const string NoToolOnHowWhy = "no_tool_on_how_why";
    public const string ZeroRetrievalResults = "zero_retrieval_results";
    public const string LongAnswerWithoutSources = "long_answer_without_sources";
}

public sealed record FeedbackRequest(string ConversationId, string TurnId, string Kind, string? Comment);

public sealed record FeedbackAccepted(string FeedbackId);

public sealed record ToolCallRecord(string ToolName, string ArgumentSummary, string Outcome, int SourceCount, IReadOnlyList<string> DocIds, IReadOnlyList<string> ChunkIds);

public sealed record ReviewQueueItem(
    string TurnId,
    string ConversationId,
    string UserId,
    string Question,
    string Answer,
    IReadOnlyList<string> Signals,
    IReadOnlyList<ToolCallRecord> ToolCalls,
    IReadOnlyList<string> FeedbackKinds,
    DateTimeOffset CreatedAt,
    bool Labeled);

/// <summary>
/// A reviewer's label. Dataset decides which fields matter:
/// selection → ExpectedTools; retrieval → RelevantChunkIds; generation → ReferenceAnswer + ExpectedDocIds.
/// </summary>
public sealed record LabelRequest(
    string Dataset,
    IReadOnlyList<string>? ExpectedTools,
    IReadOnlyList<string>? RelevantChunkIds,
    string? ReferenceAnswer,
    IReadOnlyList<string>? ExpectedDocIds);

public static class EvalDataset
{
    public const string Selection = "selection";
    public const string Retrieval = "retrieval";
    public const string Generation = "generation";

    public static bool IsLabelable(string? value) => value is Selection or Retrieval or Generation;
}
