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

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<ConversationRow>().HasKey(x => x.Id);
        b.Entity<MessageRow>().HasIndex(x => new { x.ConversationId, x.Id });
        b.Entity<TurnRow>().HasKey(x => x.Id);
        b.Entity<TurnRow>().HasIndex(x => new { x.FirmId, x.CreatedAt });
        b.Entity<TurnRow>().HasIndex(x => new { x.ConversationId, x.CreatedAt });
        b.Entity<FeedbackRow>().HasKey(x => x.Id);
        b.Entity<FeedbackRow>().HasIndex(x => x.TurnId);
        b.Entity<LabelRow>().HasKey(x => x.Id);
        b.Entity<AuditRow>().HasIndex(x => new { x.FirmId, x.At });
    }
}

public sealed class ConversationRow
{
    public required string Id { get; set; }
    public required string UserId { get; set; }
    public required string FirmId { get; set; }
    public DateTime CreatedAt { get; set; }
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

public sealed class AuditRow
{
    public long Id { get; set; }
    public DateTime At { get; set; }
    public required string PrincipalId { get; set; }
    public required string FirmId { get; set; }
    public string? ConversationId { get; set; }
    public string? TurnId { get; set; }
    public required string ToolName { get; set; }
    /// <summary>Argument identifiers only (key=value), never free text.</summary>
    public required string Arguments { get; set; }
    public required string Outcome { get; set; }
    public long DurationMs { get; set; }
}
