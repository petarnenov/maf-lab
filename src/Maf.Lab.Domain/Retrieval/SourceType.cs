namespace Maf.Lab.Domain.Retrieval;

/// <summary>Wire names of the three corpus source types.</summary>
public static class SourceType
{
    public const string Docs = "docs";
    public const string Procedures = "procedures";
    public const string Code = "code";

    public static readonly IReadOnlyList<string> All = [Docs, Procedures, Code];

    public static bool IsKnown(string? value) => value is Docs or Procedures or Code;
}
