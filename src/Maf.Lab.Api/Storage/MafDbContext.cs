using Microsoft.EntityFrameworkCore;

namespace Maf.Lab.Api.Storage;

/// <summary>
/// Message content lives here (conversations, turns) with its own retention — never in logs.
/// Audit rows hold identifiers only.
/// </summary>
public sealed class MafDbContext(DbContextOptions<MafDbContext> options) : DbContext(options)
{
    public DbSet<ConversationRow> Conversations => Set<ConversationRow>();
    public DbSet<MessageRow> Messages => Set<MessageRow>();
    public DbSet<TurnRow> Turns => Set<TurnRow>();
    public DbSet<FeedbackRow> Feedback => Set<FeedbackRow>();
    public DbSet<LabelRow> Labels => Set<LabelRow>();
    public DbSet<AuditRow> Audit => Set<AuditRow>();
    public DbSet<AdminJobRow> AdminJobs => Set<AdminJobRow>();
    public DbSet<TurnTraceRow> TurnTraces => Set<TurnTraceRow>();
    public DbSet<A2ATaskRow> A2ATasks => Set<A2ATaskRow>();
    public DbSet<A2APushConfigRow> A2APushConfigs => Set<A2APushConfigRow>();
    public DbSet<A2APushDeliveryRow> A2APushDeliveries => Set<A2APushDeliveryRow>();
    public DbSet<PendingAdjustmentRow> PendingAdjustments => Set<PendingAdjustmentRow>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<ConversationRow>().HasKey(x => x.Id);
        b.Entity<ConversationRow>().HasIndex(x => new { x.UserId, x.FirmId, x.DeletedAt, x.LastActivityAt });
        b.Entity<MessageRow>().HasIndex(x => new { x.ConversationId, x.Id });
        b.Entity<TurnRow>().HasKey(x => x.Id);
        b.Entity<TurnRow>().HasIndex(x => new { x.FirmId, x.CreatedAt });
        b.Entity<TurnRow>().HasIndex(x => new { x.ConversationId, x.CreatedAt });
        b.Entity<FeedbackRow>().HasKey(x => x.Id);
        b.Entity<FeedbackRow>().HasIndex(x => x.TurnId);
        b.Entity<LabelRow>().HasKey(x => x.Id);
        b.Entity<AuditRow>().HasIndex(x => new { x.FirmId, x.At });
        // An investigation starts from a person, not from a firm.
        b.Entity<AuditRow>().HasIndex(x => new { x.PrincipalId, x.At });
        b.Entity<AdminJobRow>().HasKey(x => x.Id);
        b.Entity<TurnTraceRow>().HasKey(x => x.TurnId);
        b.Entity<TurnTraceRow>().HasIndex(x => x.CreatedAt);
        // At most one running job per firm and kind, enforced by the database across replicas.
        b.Entity<AdminJobRow>().HasIndex(x => new { x.FirmId, x.Kind }).IsUnique().HasFilter("\"State\" = 'running'");
        b.Entity<A2ATaskRow>().HasKey(x => x.Id);
        b.Entity<A2ATaskRow>().HasIndex(x => new { x.ContextId, x.UpdatedAt });
        b.Entity<A2ATaskRow>().HasIndex(x => new { x.PartnerId, x.UpdatedAt });
        b.Entity<A2APushConfigRow>().HasKey(x => x.Id);
        b.Entity<A2APushConfigRow>().HasIndex(x => x.TaskId);
        b.Entity<A2APushDeliveryRow>().HasIndex(x => new { x.TaskId, x.At });
        b.Entity<PendingAdjustmentRow>().HasKey(x => x.Id);
        b.Entity<PendingAdjustmentRow>().HasIndex(x => new { x.FirmId, x.UserId, x.UpdatedAt });
        b.Entity<PendingAdjustmentRow>().HasIndex(x => new { x.ConversationId, x.UpdatedAt });
    }
}

public sealed class ConversationRow
{
    public required string Id { get; set; }
    public required string UserId { get; set; }
    public required string FirmId { get; set; }
    public DateTime CreatedAt { get; set; }
    /// <summary>User-set title; null = derived from the first question.</summary>
    public string? Title { get; set; }
    public DateTime LastActivityAt { get; set; }
    /// <summary>Soft delete: hidden from history and cannot be continued; turns stay for review and evals.</summary>
    public DateTime? DeletedAt { get; set; }
}

public sealed class MessageRow
{
    public long Id { get; set; }
    public required string ConversationId { get; set; }
    public required string Role { get; set; }
    public required string Text { get; set; }
    public int Tokens { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class TurnRow
{
    public required string Id { get; set; }
    public required string ConversationId { get; set; }
    public required string UserId { get; set; }
    public required string FirmId { get; set; }
    public required string Question { get; set; }
    public string Answer { get; set; } = "";
    public string Intent { get; set; } = "";
    public bool ForcedRetrieval { get; set; }
    public string ToolCallsJson { get; set; } = "[]";
    /// <summary>(docId, sectionPath) pairs returned by search_documents, for resolving chunk ids at review time.</summary>
    public string SourcesJson { get; set; } = "[]";
    public string SignalsJson { get; set; } = "[]";
    public bool Labeled { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class FeedbackRow
{
    public required string Id { get; set; }
    public required string TurnId { get; set; }
    public required string ConversationId { get; set; }
    public required string UserId { get; set; }
    public required string FirmId { get; set; }
    public required string Kind { get; set; }
    public string? Comment { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class LabelRow
{
    public required string Id { get; set; }
    public required string TurnId { get; set; }
    public required string FirmId { get; set; }
    public required string ReviewerId { get; set; }
    public required string Dataset { get; set; }
    public required string RowJson { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// One audited action. Tool calls, deletions and exports share this record so they form a single ordered history;
/// <see cref="Hash"/> links each row to the one before it, so a changed or missing row can be pointed at.
/// </summary>
public sealed class AuditRow
{
    public long Id { get; set; }
    public DateTime At { get; set; }
    public required string PrincipalId { get; set; }
    public required string FirmId { get; set; }
    public string? ConversationId { get; set; }
    public string? TurnId { get; set; }
    /// <summary>The action: a tool name for a tool call, otherwise the action name (e.g. conversation.delete).</summary>
    public required string ToolName { get; set; }
    /// <summary>Argument identifiers only (key=value), never free text.</summary>
    public required string Arguments { get; set; }
    public required string Outcome { get; set; }
    public long DurationMs { get; set; }
    /// <summary>tool | conversation.delete | compliance.export. Null on rows written before kinds existed.</summary>
    public string? Kind { get; set; }
    /// <summary>Digest over this row's stored fields and the previous row's digest. Null before chaining began.</summary>
    public string? Hash { get; set; }
    public string? PreviousHash { get; set; }
}

/// <summary>An admin job (index, migrate). Shared by all api replicas; the owner keeps HeartbeatAt fresh while it runs.</summary>
/// <summary>
/// A proposal that has been made but not yet resolved. It lives here rather than in a replica's memory
/// because the person who answers it may reach a different replica — or come back tomorrow.
/// The signed state is what actually executes; this row is how the flow finds it again.
/// </summary>
public sealed class PendingAdjustmentRow
{
    public required string Id { get; set; }
    public required string FirmId { get; set; }
    public required string UserId { get; set; }
    public required string ConversationId { get; set; }
    public required string TurnId { get; set; }
    public required string ToolName { get; set; }
    public required string State { get; set; }
    /// <summary>The summary as it was put to the person — identifiers and amounts, no free text.</summary>
    public required string Summary { get; set; }

    /// <summary>The sentence the person was asked, so a proposal read back later asks it the same way.</summary>
    public string? Question { get; set; }

    /// <summary>When it stops being answerable. Only the signer knows it, so it is kept here too.</summary>
    public DateTime? ExpiresAt { get; set; }
    public required string Status { get; set; }
    /// <summary>The compliance review this proposal is under, when there is one.</summary>
    public string? ReviewTaskId { get; set; }
    /// <summary>How many times the reviewer has asked for a justification. Never more than twice.</summary>
    public int Questions { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>What has become of a proposal.</summary>
public static class PendingAdjustmentStatus
{
    public const string AwaitingConfirmation = "awaiting_confirmation";
    public const string AwaitingJustification = "awaiting_justification";
    public const string Applied = "applied";
    public const string Declined = "declined";
    public const string Refused = "refused";
    public const string Failed = "failed";
}

public sealed class AdminJobRow
{
    public required string Id { get; set; }
    public required string FirmId { get; set; }
    public required string Kind { get; set; }
    public required string State { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public string? Summary { get; set; }
    public required string OwnerInstance { get; set; }
    public DateTime HeartbeatAt { get; set; }
}

/// <summary>Full behind-the-scenes trace of a turn (message content; retention: Tracing:RetentionDays).</summary>
public sealed class TurnTraceRow
{
    public required string TurnId { get; set; }
    public required string ConversationId { get; set; }
    public required string UserId { get; set; }
    public required string FirmId { get; set; }
    public DateTime CreatedAt { get; set; }
    public required string Json { get; set; }
}

/// <summary>An A2A task, whole, so any replica can answer for it. The SDK's own store is per-process.</summary>
public sealed class A2ATaskRow
{
    public required string Id { get; set; }
    public required string ContextId { get; set; }
    public string? PartnerId { get; set; }
    public required string State { get; set; }
    /// <summary>The task as the SDK serialises it, including its history and artifacts.</summary>
    public required string Json { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>A webhook a caller registered for one task. The token is the caller's own, echoed back to it.</summary>
public sealed class A2APushConfigRow
{
    public required string Id { get; set; }
    public required string TaskId { get; set; }
    public required string Url { get; set; }
    public string? Token { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>What happened when we tried to deliver one state change. A failure is visible, never silent.</summary>
public sealed class A2APushDeliveryRow
{
    public long Id { get; set; }
    public required string TaskId { get; set; }
    public required string State { get; set; }
    public required string Url { get; set; }
    public DateTime At { get; set; }
    public int Attempts { get; set; }
    public bool Delivered { get; set; }
    public string? Error { get; set; }
}
