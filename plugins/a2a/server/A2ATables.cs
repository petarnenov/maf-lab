using Microsoft.EntityFrameworkCore;

namespace Maf.Lab.Plugins.A2A;

/// <summary>
/// The a2a plugin's three tables (extract-a2a batch 2), contributed to the core's store through
/// <see cref="Maf.Lab.Plugins.Abstractions.IContributesModel"/> under the names they had in the core, so an existing
/// database keeps its tasks, webhooks and deliveries.
/// </summary>
internal static class A2ATables
{
    public static void Configure(ModelBuilder model)
    {
        var task = model.Entity<A2ATaskRow>();
        task.ToTable("A2ATasks");
        task.HasKey(x => x.Id);
        // By property name: the same indexes, without a compiler-generated type whose constructor takes a tenant (no
        // plugin method takes one).
        task.HasIndex(nameof(A2ATaskRow.ContextId), nameof(A2ATaskRow.UpdatedAt));
        task.HasIndex(nameof(A2ATaskRow.PartnerId), nameof(A2ATaskRow.UpdatedAt));
        task.HasIndex(nameof(A2ATaskRow.TenantId), nameof(A2ATaskRow.UpdatedAt));
        var config = model.Entity<A2APushConfigRow>();
        config.ToTable("A2APushConfigs");
        config.HasKey(x => x.Id);
        config.HasIndex(x => x.TaskId);
        var delivery = model.Entity<A2APushDeliveryRow>();
        delivery.ToTable("A2APushDeliveries");
        delivery.HasIndex(x => new { x.TaskId, x.At });
    }
}

/// <summary>An A2A task, whole, so any replica can answer for it. The SDK's own store is per-process.</summary>
public sealed class A2ATaskRow
{
    public required string Id { get; set; }
    public required string ContextId { get; set; }
    public string? PartnerId { get; set; }

    /// <summary>The firm the partner was entitled to act for when the task was created.</summary>
    public string? TenantId { get; set; }
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
