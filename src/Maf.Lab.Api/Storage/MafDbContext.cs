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
