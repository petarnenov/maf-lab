using Maf.Lab.Domain.Chat;

namespace Maf.Lab.Domain.History;

/// <summary>One entry of GET /api/conversations.</summary>
public sealed record ConversationSummary(string ConversationId, string Title, DateTimeOffset CreatedAt, DateTimeOffset LastActivityAt, int TurnCount);

/// <summary>GET /api/conversations response; pass <see cref="NextCursor"/> as <c>before</c> for the next page.</summary>
public sealed record ConversationPage(IReadOnlyList<ConversationSummary> Conversations, string? NextCursor);

/// <summary>GET /api/conversations/{id}.</summary>
public sealed record ConversationDetail(string ConversationId, string Title, DateTimeOffset CreatedAt, DateTimeOffset LastActivityAt, IReadOnlyList<HistoryTurn> Turns);

public sealed record HistoryTurn(
    string TurnId,
    string Question,
    string Answer,
    DateTimeOffset CreatedAt,
    IReadOnlyList<HistoryToolCall> ToolCalls,
    IReadOnlyList<SourceRef> Sources,
    IReadOnlyList<string> FeedbackKinds,
    bool TraceAvailable,
    IReadOnlyList<HistoryActivity>? Activities = null);

/// <summary>
/// A data card the turn showed (add-activity-cards): the AG-UI activity as it was sent, so a reopened conversation
/// shows it again. Turns stored before cards existed have none.
/// </summary>
public sealed record HistoryActivity(string MessageId, string ActivityType, System.Text.Json.JsonElement Content);

/// <summary>CallId and ResultSummary are null for turns stored before they were persisted.</summary>
public sealed record HistoryToolCall(string? CallId, string ToolName, string ArgumentSummary, string Outcome, string? ResultSummary, int SourceCount);

public sealed record RenameConversationRequest(string Title);
