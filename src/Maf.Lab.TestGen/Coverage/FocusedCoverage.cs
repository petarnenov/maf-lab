namespace Maf.Lab.TestGen.Coverage;

/// <summary>
/// The target's coverage after a run of the related tests only. Production code does not change during a test run,
/// and the tests that run skipped are as they were at the baseline, so they still cover what the baseline saw them
/// cover: a line counts as covered when the baseline or this run covered it.
/// </summary>
public static class FocusedCoverage
{
    /// <summary>
    /// <paramref name="result"/> with its target coverage combined with <paramref name="baseline"/>'s, when it ran the
    /// related tests only and was measured; otherwise <paramref name="result"/> as it is.
    /// </summary>
    public static RunnerResult Apply(RunnerResult result, LineHits? baseline)
    {
        if (!result.Focused || !result.Measured || baseline is null)
        {
            return result;
        }
        var merged = Merge(baseline, result.TargetLines);
        var lines = Lines(merged);
        return result with
        {
            TargetPct = FileCoverage.Pct(merged.Covered.Count, merged.Covered.Count + merged.Uncovered.Count),
            Uncovered = LineRanges.Uncovered(lines),
            TargetLines = merged,
        };
    }

    /// <summary>
    /// The baseline's executable lines, each covered when either run covered it. A focused run that did not report
    /// the file (none of its tests loaded it) adds nothing.
    /// </summary>
    public static LineHits Merge(LineHits baseline, LineHits? focused)
    {
        var covered = new HashSet<int>(baseline.Covered);
        var uncoveredBefore = new HashSet<int>(baseline.Uncovered);
        foreach (var line in focused?.Covered ?? [])
        {
            if (uncoveredBefore.Contains(line))
            {
                covered.Add(line);
            }
        }
        return new LineHits(covered.Order().ToList(), baseline.Uncovered.Where(l => !covered.Contains(l)).Order().ToList());
    }

    /// <summary>The lines with one hit for covered and none for uncovered, in order.</summary>
    public static IReadOnlyList<LineCoverage> Lines(LineHits hits) =>
        hits.Covered.Select(l => new LineCoverage(l, 1, 0, 0))
            .Concat(hits.Uncovered.Select(l => new LineCoverage(l, 0, 0, 0)))
            .OrderBy(l => l.Line).ToList();

    /// <summary>A file's lines as line hits; a line listed more than once is covered when any entry ran.</summary>
    public static LineHits Of(IReadOnlyList<LineCoverage> lines)
    {
        var byLine = lines.GroupBy(l => l.Line).Select(g => (Line: g.Key, Ran: g.Any(l => l.Hits > 0))).OrderBy(l => l.Line).ToList();
        return new(byLine.Where(l => l.Ran).Select(l => l.Line).ToList(), byLine.Where(l => !l.Ran).Select(l => l.Line).ToList());
    }
}

/// <summary>Uncovered lines as ranges.</summary>
public static class LineRanges
{
    /// <summary>Uncovered lines as ranges; lines with nothing executable between them belong to one range.</summary>
    public static IReadOnlyList<int[]> Uncovered(IReadOnlyList<LineCoverage> lines)
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
