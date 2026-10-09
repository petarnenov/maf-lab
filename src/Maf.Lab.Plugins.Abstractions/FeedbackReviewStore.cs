using System.Text.Json.Nodes;
using Maf.Lab.Domain.Feedback;

namespace Maf.Lab.Plugins.Abstractions;

/// <summary>Purpose-built review data, exposed only for the validated administrator's tenant.</summary>
public sealed record ReviewStoredTurn(string Id, string ConversationId, string UserId, string TenantId,
    string Question, string Answer, string SignalsJson, string ToolCallsJson, string SourcesJson,
    DateTimeOffset CreatedAt, bool Labeled, IReadOnlyList<string> FeedbackKinds);

public sealed record ReviewSource(string DocId, string SectionPath);

/// <summary>Core turn/label persistence. Every access derives the tenant from the current principal.</summary>
public interface IFeedbackReviewStore
{
    Task<IReadOnlyList<ReviewStoredTurn>> FlaggedAsync(CancellationToken ct);
    Task<ReviewStoredTurn?> FindAsync(string turnId, CancellationToken ct);
    Task<bool> LabelAsync(string turnId, string dataset, JsonObject row, CancellationToken ct);
    Task<IReadOnlyList<string>?> ResolveChunkIdsAsync(string tool, IReadOnlyList<ReviewSource> sources, CancellationToken ct);
    string? DomainOf(string tool);
}
