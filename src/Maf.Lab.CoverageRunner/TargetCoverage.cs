using Maf.Lab.TestGen.Coverage;

namespace Maf.Lab.CoverageRunner;

/// <summary>One file's line coverage out of a run's report, and the line ranges still uncovered.</summary>
public static class TargetCoverage
{
    public static (double? Pct, IReadOnlyList<int[]> Uncovered) Of(string cobertura, string measuredRoot, IReadOnlySet<string> repoFiles,
        string target)
    {
        using var stream = File.OpenRead(cobertura);
        var normalised = CoveragePaths.Normalise(CoberturaParser.Parse(stream), measuredRoot, repoFiles);
        var file = normalised.Files.FirstOrDefault(f => f.Path == target);
        return file is null ? (null, []) : (file.LinePct, Ranges(file.Lines));
    }

    /// <summary>Uncovered lines as ranges; lines with nothing executable between them belong to one range.</summary>
    public static IReadOnlyList<int[]> Ranges(IReadOnlyList<LineCoverage> lines)
    {
        var ranges = new List<int[]>();
        int[]? open = null;
        foreach (var line in lines.OrderBy(l => l.Line))
        {
            if (line.Hits == 0)
            {
                if (open is null)
                {
                    open = [line.Line, line.Line];
                    ranges.Add(open);
                }
                else
                {
                    open[1] = line.Line;
                }
            }
            else
            {
                open = null;
            }
        }
        return ranges;
    }
}
