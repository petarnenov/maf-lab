namespace Maf.Lab.ComplianceAgent;

/// <summary>
/// How the reviewer behaves. Every number here exists so that a test can make the reviewer instant and certain,
/// while the stack keeps it slow and occasionally awkward — which is the point of having it at all.
/// </summary>
public sealed class ReviewOptions
{
    public const string Section = "Review";

    /// <summary>Shortest simulated review, in milliseconds.</summary>
    public int MinDurationMs { get; set; } = 20_000;

    /// <summary>Longest simulated review, in milliseconds.</summary>
    public int MaxDurationMs { get; set; } = 60_000;

    /// <summary>How often a review asks for the advisor's justification instead of answering, from 0 to 1.</summary>
    public double AskForJustificationRate { get; set; } = 0.2;

    /// <summary>An adjustment larger than this is refused, and the reason says so.</summary>
    public decimal RefuseAboveAmount { get; set; } = 1_000m;
}
