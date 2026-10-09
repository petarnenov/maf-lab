using Maf.Lab.TestGen;
using Maf.Lab.TestGen.Coverage;

namespace Maf.Lab.CoverageRunner;

/// <summary>One file's line coverage out of a run's report, the line ranges still uncovered, and its line hits.</summary>
public static class TargetCoverage
{
    public static (double? Pct, IReadOnlyList<int[]> Uncovered, LineHits? Lines) Of(string cobertura, string measuredRoot,
        IReadOnlySet<string> repoFiles, string target)
    {
        using var stream = File.OpenRead(cobertura);
        var normalised = CoveragePaths.Normalise(CoberturaParser.Parse(stream), measuredRoot, repoFiles);
        var file = normalised.Files.FirstOrDefault(f => f.Path == target);
        return file is null ? (null, [], null) : (file.LinePct, LineRanges.Uncovered(file.Lines), FocusedCoverage.Of(file.Lines));
    }
}
