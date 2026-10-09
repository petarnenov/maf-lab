using Maf.Lab.Plugins.Abstractions;
using Maf.Lab.Api.DataLifecycle;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

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
    public DbSet<OperatorSessionRow> OperatorSessions => Set<OperatorSessionRow>();
    public DbSet<ContentAccessGrantRow> ContentAccessGrants => Set<ContentAccessGrantRow>();
    internal DbSet<LifecycleJobRow> LifecycleJobs => Set<LifecycleJobRow>();
    internal DbSet<LifecycleLegalHoldRow> LifecycleLegalHolds => Set<LifecycleLegalHoldRow>();
    public DbSet<AdminJobRow> AdminJobs => Set<AdminJobRow>();
    public DbSet<PluginEntitlementRow> PluginEntitlements => Set<PluginEntitlementRow>();
    public DbSet<PendingWriteRow> PendingWrites => Set<PendingWriteRow>();

    /// <summary>
    /// The installed in-process plugins that contribute tables (introduce-plugins decision 5): read from the application's
    /// services the context was built with, none when it was built without them (a tool, a test's bare context).
    /// </summary>
    internal IReadOnlyList<IContributesModel> PluginModels =>
        this.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()?.ApplicationServiceProvider
            ?.GetService<Plugins.LoadedPlugins>()?.Plugins.OfType<IContributesModel>().ToList() ?? [];

    protected override void OnModelCreating(ModelBuilder b)
    {
        // A plugin's tables, only while it is installed: DatabaseInitializer's create script then includes them.
        foreach (var plugin in PluginModels)
        {
            plugin.ConfigureModel(b);
        }
        b.Entity<PluginEntitlementRow>().HasKey(x => new { x.TenantId, x.Plugin });
        b.Entity<PluginEntitlementRow>().ToTable("PluginEntitlements", t =>
            t.HasCheckConstraint("CK_PluginEntitlements_EnabledRequiresAllowed", "\"Enabled\" = 0 OR \"Allowed\" = 1"));
        b.Entity<ConversationRow>().HasKey(x => x.Id);
        b.Entity<ConversationRow>().HasIndex(x => new { x.UserId, x.TenantId, x.DeletedAt, x.LastActivityAt });
        b.Entity<MessageRow>().HasIndex(x => new { x.ConversationId, x.Id });
        b.Entity<TurnRow>().HasKey(x => x.Id);
        b.Entity<TurnRow>().HasIndex(x => new { x.TenantId, x.CreatedAt });
        b.Entity<TurnRow>().HasIndex(x => new { x.ConversationId, x.CreatedAt });
        b.Entity<FeedbackRow>().HasKey(x => x.Id);
        b.Entity<FeedbackRow>().HasIndex(x => x.TurnId);
        b.Entity<LabelRow>().HasKey(x => x.Id);
        b.Entity<AuditRow>().HasIndex(x => new { x.TenantId, x.At });
        b.Entity<OperatorSessionRow>().HasKey(x => x.SessionKey);
        b.Entity<ContentAccessGrantRow>().HasKey(x => x.Id);
        b.Entity<ContentAccessGrantRow>().HasIndex(x => x.SessionKey).IsUnique().HasFilter("\"EndedAt\" IS NULL");
        b.Entity<ContentAccessGrantRow>().HasIndex(x => new { x.TenantId, x.Id });
        b.Entity<ContentAccessGrantRow>().ToTable("ContentAccessGrants", t =>
            t.HasCheckConstraint("CK_ContentAccessGrants_BoundedLifetime",
                "\"ExpiresAt\" > \"StartedAt\" AND julianday(\"ExpiresAt\") - julianday(\"StartedAt\") <= 0.041666667"));
        // An investigation starts from a person, not from a firm.
        b.Entity<AuditRow>().HasIndex(x => new { x.PrincipalId, x.At });
        b.Entity<LifecycleJobRow>().HasKey(x => x.Id);
        b.Entity<LifecycleJobRow>().Property(x => x.State).HasConversion<string>();
        b.Entity<LifecycleJobRow>().Property(x => x.Operation).HasConversion<string>();
        b.Entity<LifecycleJobRow>().HasIndex(x => x.TenantId).IsUnique()
            .HasFilter("\"State\" IN ('Queued', 'Running', 'Stopping')");
        b.Entity<LifecycleLegalHoldRow>().HasKey(x => x.Id);
        b.Entity<LifecycleLegalHoldRow>().HasIndex(x => new { x.TenantId, x.ReleasedAt });
        b.Entity<AdminJobRow>().HasKey(x => x.Id);
        // At most one running job per firm and kind, enforced by the database across replicas.
        b.Entity<AdminJobRow>().HasIndex(x => new { x.TenantId, x.Kind }).IsUnique().HasFilter("\"State\" = 'running'");
        b.Entity<PendingWriteRow>().HasKey(x => x.Id);
        b.Entity<PendingWriteRow>().HasIndex(x => new { x.TenantId, x.UserId, x.UpdatedAt });
        b.Entity<PendingWriteRow>().HasIndex(x => new { x.ConversationId, x.UpdatedAt });

    }
}

public sealed class ConversationRow
{
    public required string Id { get; set; }
    public required string UserId { get; set; }
    public required string TenantId { get; set; }
    public DateTime CreatedAt { get; set; }
    /// <summary>User-set title; null = derived from the first question.</summary>
    public string? Title { get; set; }
    public DateTime LastActivityAt { get; set; }
    /// <summary>Soft delete: hidden from history and cannot be continued; turns stay for review and evals.</summary>
    public DateTime? DeletedAt { get; set; }
    /// <summary>The account in focus (add-focus-state): set by a single-account portfolio read or by the user's choice.</summary>
    public string? FocusAccountId { get; set; }
    /// <summary>
    /// The domains of the conversation's last turn that had any in scope, comma-separated (add-codebase-domain): the tools a
    /// follow-up that names no domain is offered.
    /// </summary>
    public string? Domains { get; set; }
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
    public required string TenantId { get; set; }
    public required string Question { get; set; }
    public string Answer { get; set; } = "";
    public string Intent { get; set; } = "";
    public bool ForcedRetrieval { get; set; }
    public string ToolCallsJson { get; set; } = "[]";
    /// <summary>(docId, sectionPath) pairs returned by search_documents, for resolving chunk ids at review time.</summary>
    public string SourcesJson { get; set; } = "[]";
    /// <summary>The data cards the turn showed, as sent (add-activity-cards): numbers and names only.</summary>
    public string ActivitiesJson { get; set; } = "[]";
    public string SignalsJson { get; set; } = "[]";
    /// <summary>
    /// The turn's core record (introduce-plugins decision 7): its trace events of the kinds <see cref="Agent.Tracing.TurnRecord"/>
    /// keeps, in the trace's own shape (docs/trace-events.md). Read by the answer check's previous read and the statistics.
    /// </summary>
    public string RecordJson { get; set; } = "[]";
    /// <summary>What the model reasoned before it answered, as its own field (none for a model that does not reason).</summary>
    public string Reasoning { get; set; } = "";
    /// <summary>How long the reasoning took, from its first chunk to its last; null without reasoning.</summary>
    public long? ReasoningMs { get; set; }
    public bool Labeled { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class FeedbackRow
{
    public required string Id { get; set; }
    public required string TurnId { get; set; }
    public required string ConversationId { get; set; }
    public required string UserId { get; set; }
    public required string TenantId { get; set; }
    public required string Kind { get; set; }
    public string? Comment { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class LabelRow
{
    public required string Id { get; set; }
    public required string TurnId { get; set; }
    public required string TenantId { get; set; }
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
    public required string TenantId { get; set; }
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
/// A write proposed by any tool and not yet resolved (generalize-write-confirmation). It lives here rather than in a
/// replica's memory because the person who answers it may reach a different replica — or come back tomorrow. The
/// signed state is what actually executes; this row is how the core finds it again. The summary is the tool's own
/// and the flow's data is the flow's own: the core reads neither.
/// </summary>
public sealed class PendingWriteRow
{
    public required string Id { get; set; }
    public required string TenantId { get; set; }
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
    /// <summary>What the tool's flow keeps about the proposal (JSON), e.g. the review it is under. Never free text.</summary>
    public string? FlowJson { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>What has become of a proposal.</summary>
public static class PendingWriteStatus
{
    public const string AwaitingConfirmation = "awaiting_confirmation";
    /// <summary>The model was told to ask the person something first; their answer comes back as the tool's next call.</summary>
    public const string AwaitingInput = "awaiting_input";
    public const string Applied = "applied";
    public const string Declined = "declined";
    public const string Refused = "refused";
    public const string Failed = "failed";
    /// <summary>Past its expiry when an answer or a rejoin found it.</summary>
    public const string Expired = "expired";
}

public sealed class AdminJobRow
{
    public required string Id { get; set; }
    public required string TenantId { get; set; }
    public required string Kind { get; set; }
    public required string State { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public string? Summary { get; set; }
    public required string OwnerInstance { get; set; }
    public DateTime HeartbeatAt { get; set; }
}

/// <summary>Full behind-the-scenes trace of a turn (message content; retention: Tracing:RetentionDays).</summary>
/// <summary>Keys EF's model cache by the set of plugins that contribute tables, as well as by the context type.</summary>
public sealed class PluginModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime) => context is MafDbContext maf
        ? (context.GetType(), string.Join(",", maf.PluginModels.Select(p => ((IMafPlugin)p).Name).Order(StringComparer.Ordinal)), designTime)
        : (object)(context.GetType(), designTime);
}

/// <summary>
/// The one store as a plugin reaches it (introduce-plugins decision 5): EF's own <see cref="IDbContextFactory{TContext}"/>
/// over <see cref="DbContext"/>, so a plugin reads and writes its tables with <c>Set&lt;T&gt;()</c> and never names the
/// core's context type.
/// </summary>
public sealed class PluginDbContextFactory(IDbContextFactory<MafDbContext> inner) : IDbContextFactory<DbContext>
{
    public DbContext CreateDbContext() => inner.CreateDbContext();

    public async Task<DbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
        await inner.CreateDbContextAsync(cancellationToken);
}

/// <summary>Core tenant entitlements survive disabling or uninstalling a contributor.</summary>
public sealed class PluginEntitlementRow
{
    public required string TenantId { get; set; }
    public required string Plugin { get; set; }
    public bool Allowed { get; set; }
    public bool Enabled { get; set; }
    public DateTime ChangedAt { get; set; }
    public required string ChangedBy { get; set; }
}
