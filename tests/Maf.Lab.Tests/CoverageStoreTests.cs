using Maf.Lab.Api.Coverage;

namespace Maf.Lab.Tests;

/// <summary>Snapshots keyed by commit; the latest official per file; candidates and their promotion (coverage-ingestion).</summary>
public sealed class CoverageStoreTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly MovableTime _time = new(new DateTimeOffset(2026, 9, 30, 10, 0, 0, TimeSpan.Zero));

    private static FileCoverage File(string path, int covered, int total) =>
        new(path, Enumerable.Range(1, total).Select(i => new LineCoverage(i, i <= covered ? 1 : 0, 0, 0)).ToList());

    private static NormalisedCoverage Of(params FileCoverage[] files) => new(files, 0);

    private async Task<CoverageStore> NewStoreAsync() => new(await CoverageStorageTests.NewDatabaseAsync(), _time);

    [Fact]
    public async Task Each_file_comes_from_the_newest_official_snapshot_that_has_it()
    {
        var store = await NewStoreAsync();
        await store.IngestAsync(Of(File("src/A.cs", 1, 10), File("src/B.cs", 5, 10)), "c1", false, Toolchains.Dotnet, SnapshotKind.Official, null, Ct);
        _time.SetUtcNow(_time.GetUtcNow().AddMinutes(1));
        await store.IngestAsync(Of(File("src/A.cs", 9, 10)), "c2", false, Toolchains.Dotnet, SnapshotKind.Official, null, Ct);

        var latest = await store.LatestOfficialAsync(Ct);

        Assert.Equal([("src/A.cs", 90.0, "c2"), ("src/B.cs", 50.0, "c1")], latest.Select(f => (f.Path, f.LinePct, f.CommitSha)));
    }

    [Fact]
    public async Task A_candidate_does_not_replace_official_until_promoted()
    {
        var store = await NewStoreAsync();
        await store.IngestAsync(Of(File("src/A.cs", 6, 10)), "c1", false, Toolchains.Dotnet, SnapshotKind.Official, null, Ct);
        _time.SetUtcNow(_time.GetUtcNow().AddMinutes(1));
        await store.IngestAsync(Of(File("src/A.cs", 9, 10)), "c1", false, Toolchains.Dotnet, SnapshotKind.Candidate, "r_1", Ct);

        Assert.Equal(60.0, (await store.LatestOfficialAsync(Ct)).Single().LinePct);
        Assert.Equal(60.0, (await store.CurrentAsync("src/A.cs", Ct))!.Totals.LinePct);
        Assert.Equal(90.0, (await store.CandidatesAsync(["r_1"], Ct)).Single().LinePct);

        _time.SetUtcNow(_time.GetUtcNow().AddMinutes(1));
        Assert.True(await store.PromoteAsync("r_1", "merge1", Ct));

        var current = (await store.CurrentAsync("src/A.cs", Ct))!;
        Assert.Equal((90.0, "merge1", SnapshotKind.Official), (current.Totals.LinePct, current.Totals.CommitSha, current.Kind));
        Assert.Empty(await store.CandidatesAsync(["r_1"], Ct));
    }

    [Fact]
    public async Task Lines_survive_the_round_trip()
    {
        var store = await NewStoreAsync();
        var lines = new List<LineCoverage> { new(3, 7, 1, 2), new(4, 0, 0, 0), new(9, 1, 2, 2) };
        await store.IngestAsync(Of(new FileCoverage("src/A.cs", lines)), "c1", true, Toolchains.Dotnet, SnapshotKind.Official, null, Ct);

        var snapshot = (await store.CurrentAsync("src/A.cs", Ct))!;

        Assert.Equal(lines, snapshot.Lines);
        Assert.True(snapshot.Dirty);
        Assert.Equal((2, 3, 3, 4), (snapshot.Totals.LinesCovered, snapshot.Totals.LinesTotal, snapshot.Totals.BranchesCovered, snapshot.Totals.BranchesTotal));
    }

    [Fact]
    public async Task History_is_newest_first_with_kind()
    {
        var store = await NewStoreAsync();
        await store.IngestAsync(Of(File("src/A.cs", 5, 10)), "c1", false, Toolchains.Dotnet, SnapshotKind.Official, null, Ct);
        _time.SetUtcNow(_time.GetUtcNow().AddMinutes(1));
        await store.IngestAsync(Of(File("src/A.cs", 8, 10)), "c1", false, Toolchains.Dotnet, SnapshotKind.Candidate, "r_1", Ct);

        var history = await store.HistoryAsync("src/A.cs", Ct);

        Assert.Equal([(80.0, SnapshotKind.Candidate), (50.0, SnapshotKind.Official)], history.Select(h => (h.Totals.LinePct, h.Kind)));
        Assert.True(await store.KnowsAsync("src/A.cs", Ct));
        Assert.False(await store.KnowsAsync("src/Z.cs", Ct));
    }
}
