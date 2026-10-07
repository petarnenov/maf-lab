namespace Maf.Lab.Plugins.Abstractions;

/// <summary>
/// The audit record as a screen reads it (extract-compliance-plugin): whether the chain is intact, a page of the caller's
/// tenant's actions, and an export package a recipient can check. A Port the core implements: the records, the chain's
/// rule and the export's own record stay the core's. The tenant is the caller's (the request's principal), never a
/// parameter, and what comes back are DTOs.
/// </summary>
public interface IAuditTrail
{
    /// <summary>Walks the whole chain (it is global) and reports where it breaks, if it does.</summary>
    Task<AuditChainReport> VerifyAsync(CancellationToken ct);

    /// <summary>A page of the caller's tenant's actions, newest first. Reading is not recorded: browsing must not grow the log.</summary>
    Task<ActionPage> PageAsync(AuditFilter filter, CancellationToken ct);

    /// <summary>
    /// The caller's tenant's conversations, turns and actions in a range, with a digest over them and the chain's head as
    /// of before this export; the export itself is recorded before the package is returned.
    /// </summary>
    Task<ExportPackage> ExportAsync(ExportRange range, CancellationToken ct);
}

/// <param name="Before">The cursor of the previous page (a row id), for the next, older one.</param>
public sealed record AuditFilter(DateTimeOffset? From, DateTimeOffset? To, string? UserId, string? Kind, int Limit, long? Before);

/// <param name="UserId">Whose records, when only one person's are asked for.</param>
public sealed record ExportRange(DateTimeOffset From, DateTimeOffset To, string? UserId);

/// <param name="Intact">False when a row's content or a row's absence breaks a link.</param>
/// <param name="Checked">How many chained rows were walked.</param>
/// <param name="Unchained">Rows written before chaining began; reported, never rewritten.</param>
/// <param name="Head">Digest of the most recent chained row.</param>
public sealed record AuditChainReport(
    bool Intact,
    int Checked,
    int Unchained,
    DateTimeOffset? From,
    DateTimeOffset? To,
    string? Head,
    long? FirstBrokenId = null,
    string? Reason = null);

public sealed record ExportedConversation(string ConversationId, string UserId, DateTimeOffset CreatedAt,
    DateTimeOffset LastActivityAt, string? Title, DateTimeOffset? DeletedAt);

public sealed record ExportedTurn(string TurnId, string ConversationId, string UserId, DateTimeOffset CreatedAt,
    string Question, string Answer, string Intent, bool ForcedRetrieval, string ToolCallsJson, string SourcesJson, string SignalsJson);

public sealed record ExportedAction(long Id, DateTimeOffset At, string PrincipalId, string? Kind, string Action,
    string Arguments, string Outcome, long DurationMs, string? ConversationId, string? TurnId, string? Hash);

/// <summary>Exactly what the digest covers.</summary>
public sealed record ExportContent(IReadOnlyList<ExportedConversation> Conversations, IReadOnlyList<ExportedTurn> Turns,
    IReadOnlyList<ExportedAction> Actions);

/// <param name="Sha256">Digest over the content sections, so the package can be checked by someone who did not produce it.</param>
/// <param name="AuditChainHead">The record's head at the moment the package was drawn.</param>
public sealed record ExportManifest(string TenantId, string? SubjectUserId, DateTimeOffset From, DateTimeOffset To,
    DateTimeOffset GeneratedAt, string By, IReadOnlyDictionary<string, int> Counts, string Sha256, string? AuditChainHead);

/// <param name="NextCursor">Pass as `before` for the next, older page; null when there are no more.</param>
public sealed record ActionPage(IReadOnlyList<ExportedAction> Actions, long? NextCursor);

public sealed record ExportPackage(ExportManifest Manifest, IReadOnlyList<ExportedConversation> Conversations,
    IReadOnlyList<ExportedTurn> Turns, IReadOnlyList<ExportedAction> Actions);
