using System.Text.Json.Serialization;

namespace Maf.Lab.Domain.Chat;

/// <summary>A source of an answer, as the sources panel shows it.</summary>
public sealed record SourceRef(string DocId, string SectionPath, string SourcePath, string Snippet);

/// <summary>What a person answers a waiting write with.</summary>
public sealed record ConfirmationDecision(string ConversationId, string AdjustmentId, bool Approve);

public sealed record ChatRequest(string? ConversationId, string Message);

public sealed record ConversationCreated(string ConversationId);
