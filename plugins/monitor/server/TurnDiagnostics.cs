using Microsoft.EntityFrameworkCore;

namespace Maf.Lab.Plugins.Monitor;

/// <summary>
/// One turn's full trace and the AG-UI frames of the run that produced it, as the monitor keeps them (introduce-plugins
/// decision 7): its own table, created only while the plugin is installed, deleted by its own retention.
/// </summary>
public sealed class TurnDiagnosticsRow
{
    public required string TurnId { get; set; }
    public required string ConversationId { get; set; }
    public required string UserId { get; set; }
    public required string TenantId { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>The turn's trace events, as written (docs/trace-events.md).</summary>
    public required string Json { get; set; }

    /// <summary>The run's AG-UI frames, added once its stream ended; null until then, and for a run that kept none.</summary>
    public string? AguiJson { get; set; }

    internal static void Configure(ModelBuilder model)
    {
        var e = model.Entity<TurnDiagnosticsRow>();
        e.ToTable("TurnDiagnostics");
        e.HasKey(x => x.TurnId);
        e.HasIndex(x => x.CreatedAt);
    }
}
