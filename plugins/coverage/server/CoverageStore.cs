using System.IO.Compression;
using System.Text.Json;
using Maf.Lab.TestGen.Coverage;
using Microsoft.EntityFrameworkCore;

namespace Maf.Lab.Plugins.Coverage;

/// <summary>What a snapshot is: the commit it was measured at and whether it is the file's real coverage yet.</summary>
public static class SnapshotKind
{
    public const string Official = "official";
    public const string Candidate = "candidate";
}

/// <summary>One file's totals as of one snapshot, without its lines: a row of the tree.</summary>
public sealed record FileTotals(
    string Path, string SnapshotId, string CommitSha, DateTime MeasuredAt,
    int LinesTotal, int LinesCovered, int BranchesTotal, int BranchesCovered)
{
    public double LinePct => FileCoverage.Pct(LinesCovered, LinesTotal);
}

/// <summary>One file's coverage in one snapshot, with its lines.</summary>
public sealed record FileSnapshot(FileTotals Totals, string Kind, bool Dirty, IReadOnlyList<LineCoverage> Lines);

/// <summary>
/// Coverage snapshots in SQLite. A file's coverage is the newest official snapshot that contains it; a candidate
/// (from a run awaiting acceptance) is kept beside it and becomes official only when promoted.
/// </summary>
public sealed class CoverageStore(IDbContextFactory<DbContext> dbFactory, TimeProvider? time = null)
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    public async Task<string> IngestAsync(NormalisedCoverage coverage, string commitSha, bool dirty, string toolchain, string kind,
        string? runId, CancellationToken ct)
    {
        var id = $"cs_{Guid.NewGuid():N}";
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.Set<CoverageSnapshotRow>().Add(new CoverageSnapshotRow
        {
            Id = id,
            CommitSha = commitSha,
            Dirty = dirty,
            Toolchain = toolchain,
            Kind = kind,
            RunId = runId,
            CreatedAt = _time.GetUtcNow().UtcDateTime,
        });
        foreach (var file in coverage.Files)
        {
            db.Set<CoverageFileRow>().Add(new CoverageFileRow
            {
                SnapshotId = id,
                Path = file.Path,
                LinesTotal = file.LinesTotal,
                LinesCovered = file.LinesCovered,
                BranchesTotal = file.BranchesTotal,
                BranchesCovered = file.BranchesCovered,
                LinesBlob = Pack(file.Lines),
            });
        }
        await db.SaveChangesAsync(ct);
        return id;
    }

    /// <summary>Every file's current (official) totals: per file, the newest official snapshot that has it.</summary>
    public async Task<IReadOnlyList<FileTotals>> LatestOfficialAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var rows = await (
            from f in db.Set<CoverageFileRow>()
            join sn in db.Set<CoverageSnapshotRow>() on f.SnapshotId equals sn.Id
            where sn.Kind == SnapshotKind.Official
            where !(from f2 in db.Set<CoverageFileRow>()
                    join s2 in db.Set<CoverageSnapshotRow>() on f2.SnapshotId equals s2.Id
                    where f2.Path == f.Path && s2.Kind == SnapshotKind.Official
                        && (s2.CreatedAt > sn.CreatedAt || (s2.CreatedAt == sn.CreatedAt && string.Compare(s2.Id, sn.Id) > 0))
                    select s2.Id).Any()
            select new FileTotals(f.Path, sn.Id, sn.CommitSha, sn.CreatedAt, f.LinesTotal, f.LinesCovered, f.BranchesTotal, f.BranchesCovered))
            .ToListAsync(ct);
        return rows.OrderBy(r => r.Path, StringComparer.Ordinal).ToList();
    }

    /// <summary>The candidate totals of the given runs, by path.</summary>
    public async Task<IReadOnlyList<FileTotals>> CandidatesAsync(IReadOnlyCollection<string> runIds, CancellationToken ct)
    {
        if (runIds.Count == 0)
        {
            return [];
        }
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await (
            from f in db.Set<CoverageFileRow>()
            join sn in db.Set<CoverageSnapshotRow>() on f.SnapshotId equals sn.Id
            where sn.Kind == SnapshotKind.Candidate && sn.RunId != null && runIds.Contains(sn.RunId)
            select new FileTotals(f.Path, sn.Id, sn.CommitSha, sn.CreatedAt, f.LinesTotal, f.LinesCovered, f.BranchesTotal, f.BranchesCovered))
            .ToListAsync(ct);
    }

    /// <summary>
    /// Each run's candidate totals for its own file only (<paramref name="runPaths"/>: run id → the file it is for). A
    /// candidate snapshot measures the whole project, so the other files in it are not what the run is judged by.
    /// </summary>
    public async Task<IReadOnlyList<FileTotals>> CandidateTargetsAsync(IReadOnlyDictionary<string, string> runPaths, CancellationToken ct)
    {
        if (runPaths.Count == 0)
        {
            return [];
        }
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var ids = runPaths.Keys.ToList();
        var paths = runPaths.Values.Distinct().ToList();
        var rows = await (
            from f in db.Set<CoverageFileRow>()
            join sn in db.Set<CoverageSnapshotRow>() on f.SnapshotId equals sn.Id
            where sn.Kind == SnapshotKind.Candidate && sn.RunId != null && ids.Contains(sn.RunId) && paths.Contains(f.Path)
            select new { sn.RunId, Totals = new FileTotals(f.Path, sn.Id, sn.CommitSha, sn.CreatedAt, f.LinesTotal, f.LinesCovered, f.BranchesTotal, f.BranchesCovered) })
            .ToListAsync(ct);
        // A run re-verified after a restart may hold more than one candidate snapshot: its newest is the one that counts.
        return rows.Where(r => runPaths[r.RunId!] == r.Totals.Path)
            .GroupBy(r => r.Totals.Path)
            .Select(g => g.OrderByDescending(r => r.Totals.MeasuredAt).First().Totals)
            .ToList();
    }

    /// <summary>The file as of its newest official snapshot, or null when no snapshot has it.</summary>
    public async Task<FileSnapshot?> CurrentAsync(string path, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var id = await db.Set<CoverageFileRow>().Where(f => f.Path == path)
            .Join(db.Set<CoverageSnapshotRow>().Where(s => s.Kind == SnapshotKind.Official), f => f.SnapshotId, s => s.Id, (f, s) => s)
            .OrderByDescending(s => s.CreatedAt).ThenByDescending(s => s.Id)
            .Select(s => s.Id).FirstOrDefaultAsync(ct);
        return id is null ? null : await SnapshotOfAsync(db, id, path, ct);
    }

    /// <summary>The file as a run's candidate measured it, or null.</summary>
    public async Task<FileSnapshot?> CandidateAsync(string runId, string path, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var id = await db.Set<CoverageSnapshotRow>().Where(s => s.RunId == runId && s.Kind == SnapshotKind.Candidate)
            .Select(s => s.Id).FirstOrDefaultAsync(ct);
        return id is null ? null : await SnapshotOfAsync(db, id, path, ct);
    }

    /// <summary>Every snapshot that has the file, newest first.</summary>
    public async Task<IReadOnlyList<(FileTotals Totals, string Kind)>> HistoryAsync(string path, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var rows = await db.Set<CoverageFileRow>().Where(f => f.Path == path)
            .Join(db.Set<CoverageSnapshotRow>(), f => f.SnapshotId, s => s.Id, (f, s) => new { f, s })
            .OrderByDescending(x => x.s.CreatedAt).ThenByDescending(x => x.s.Id)
            .Select(x => new
            {
                Totals = new FileTotals(x.f.Path, x.s.Id, x.s.CommitSha, x.s.CreatedAt, x.f.LinesTotal, x.f.LinesCovered,
                    x.f.BranchesTotal, x.f.BranchesCovered),
                x.s.Kind,
            })
            .ToListAsync(ct);
        return rows.Select(r => (r.Totals, r.Kind)).ToList();
    }

    /// <summary>Whether any snapshot has this path: the only paths whose source may be served.</summary>
    public async Task<bool> KnowsAsync(string path, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Set<CoverageFileRow>().AnyAsync(f => f.Path == path, ct);
    }

    /// <summary>Whether anything has been ingested at all: the screen's "No coverage report yet".</summary>
    public async Task<bool> AnyAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Set<CoverageSnapshotRow>().AnyAsync(s => s.Kind == SnapshotKind.Official, ct);
    }

    /// <summary>
    /// A run was accepted: its candidate becomes official, measured at the merge commit and dated now, so it is the
    /// file's newest official snapshot.
    /// </summary>
    public async Task<bool> PromoteAsync(string runId, string mergeCommit, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var snapshot = await db.Set<CoverageSnapshotRow>().SingleOrDefaultAsync(s => s.RunId == runId && s.Kind == SnapshotKind.Candidate, ct);
        if (snapshot is null)
        {
            return false;
        }
        snapshot.Kind = SnapshotKind.Official;
        snapshot.CommitSha = mergeCommit;
        snapshot.CreatedAt = _time.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        return true;
    }

    private static async Task<FileSnapshot?> SnapshotOfAsync(DbContext db, string snapshotId, string path, CancellationToken ct)
    {
        var row = await db.Set<CoverageFileRow>().Where(f => f.SnapshotId == snapshotId && f.Path == path)
            .Join(db.Set<CoverageSnapshotRow>(), f => f.SnapshotId, s => s.Id, (f, s) => new { f, s })
            .SingleOrDefaultAsync(ct);
        if (row is null)
        {
            return null;
        }
        var totals = new FileTotals(row.f.Path, row.s.Id, row.s.CommitSha, row.s.CreatedAt, row.f.LinesTotal, row.f.LinesCovered,
            row.f.BranchesTotal, row.f.BranchesCovered);
        return new FileSnapshot(totals, row.s.Kind, row.s.Dirty, Unpack(row.f.LinesBlob));
    }

    // Lines as [[line, hits, branchesCovered, branchesTotal], …], gzipped: a large file is thousands of lines, and one
    // blob per file keeps an ingest to one row per file.
    internal static byte[] Pack(IReadOnlyList<LineCoverage> lines)
    {
        using var buffer = new MemoryStream();
        using (var gzip = new GZipStream(buffer, CompressionLevel.Fastest))
        {
            JsonSerializer.Serialize(gzip, lines.Select(l => new[] { l.Line, l.Hits, l.BranchesCovered, l.BranchesTotal }));
        }
        return buffer.ToArray();
    }

    internal static IReadOnlyList<LineCoverage> Unpack(byte[] blob)
    {
        using var gzip = new GZipStream(new MemoryStream(blob), CompressionMode.Decompress);
        var rows = JsonSerializer.Deserialize<int[][]>(gzip) ?? [];
        return rows.Select(r => new LineCoverage(r[0], r[1], r[2], r[3])).ToList();
    }
}
