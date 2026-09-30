using System.Text;
using Maf.Lab.TestGen.Coverage;

namespace Maf.Lab.Api.Coverage;

/// <summary>What one ingestion stored.</summary>
public sealed record IngestResult(string SnapshotId, int Files, int Dropped);

/// <summary>
/// Parse, name, store: the one path every report takes, whether it was uploaded, produced by a refresh or measured
/// by a run's verification.
/// </summary>
public sealed class CoverageIngestor(CoverageStore store, IRepository repository)
{
    public async Task<IngestResult> IngestAsync(Stream cobertura, string commit, bool dirty, string toolchain, string kind,
        string? runId, string? measuredRoot, CancellationToken ct)
    {
        var raw = CoberturaParser.Parse(cobertura);
        var files = await repository.FilesAsync(commit, ct);
        var normalised = CoveragePaths.Normalise(raw, measuredRoot, files);
        // A file only counts under the toolchain that measures it: a stray .ts in a .NET report is not coverage.
        var own = normalised with { Files = normalised.Files.Where(f => Toolchains.For(f.Path) == toolchain).ToList() };
        var id = await store.IngestAsync(own, commit, dirty, toolchain, kind, runId, ct);
        return new IngestResult(id, own.Files.Count, normalised.Dropped);
    }

    public Task<IngestResult> IngestAsync(string cobertura, string commit, bool dirty, string toolchain, string kind,
        string? runId, string? measuredRoot, CancellationToken ct) =>
        IngestAsync(new MemoryStream(Encoding.UTF8.GetBytes(cobertura)), commit, dirty, toolchain, kind, runId, measuredRoot, ct);
}
