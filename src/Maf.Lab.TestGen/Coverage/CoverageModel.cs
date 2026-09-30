namespace Maf.Lab.TestGen.Coverage;

/// <summary>The two toolchains whose reports the lab ingests.</summary>
public static class Toolchains
{
    public const string Dotnet = "dotnet";
    public const string Vitest = "vitest";

    public static bool IsKnown(string? value) => value is Dotnet or Vitest;

    /// <summary>The toolchain that measures a repo-relative source file, or null for a file neither covers.</summary>
    public static string? For(string path) =>
        path.EndsWith(".cs", StringComparison.Ordinal) ? Dotnet
        : path.EndsWith(".ts", StringComparison.Ordinal) || path.EndsWith(".tsx", StringComparison.Ordinal) ? Vitest
        : null;
}

/// <summary>covered | uncovered | partial, as the file view shows a line.</summary>
public static class LineStatus
{
    public const string Covered = "covered";
    public const string Uncovered = "uncovered";
    public const string Partial = "partial";

    public static string Of(LineCoverage line) =>
        line.Hits == 0 ? Uncovered
        : line.BranchesTotal > 0 && line.BranchesCovered < line.BranchesTotal ? Partial
        : Covered;
}

/// <summary>One executable line: how often it ran and, where it branches, how many branches were taken.</summary>
public sealed record LineCoverage(int Line, int Hits, int BranchesCovered, int BranchesTotal);

/// <summary>One source file's lines, in line order, with the totals the tree and the summary show.</summary>
public sealed record FileCoverage(string Path, IReadOnlyList<LineCoverage> Lines)
{
    public int LinesTotal => Lines.Count;
    public int LinesCovered => Lines.Count(l => l.Hits > 0);
    public int BranchesTotal => Lines.Sum(l => l.BranchesTotal);
    public int BranchesCovered => Lines.Sum(l => l.BranchesCovered);
    public double LinePct => Pct(LinesCovered, LinesTotal);

    /// <summary>A percentage rounded to one decimal; a file with nothing executable counts as fully covered.</summary>
    public static double Pct(int covered, int total) => total == 0 ? 100 : Math.Round(100.0 * covered / total, 1);
}

/// <summary>A Cobertura report as written: the files under whatever names the toolchain gave them.</summary>
public sealed record RawCoverageReport(IReadOnlyList<string> Sources, IReadOnlyList<FileCoverage> Files);

/// <summary>A report ready to store: repo-relative files, and how many entries were dropped on the way.</summary>
public sealed record NormalisedCoverage(IReadOnlyList<FileCoverage> Files, int Dropped);
