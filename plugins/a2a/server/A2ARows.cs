namespace Maf.Lab.Plugins.A2A;

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
