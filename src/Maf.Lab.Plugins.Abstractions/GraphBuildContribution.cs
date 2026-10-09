using Maf.Lab.Indexing.Graph;

namespace Maf.Lab.Plugins.Abstractions;

/// <summary>A domain's pure graph construction; persistence and progress aggregation remain the host's.</summary>
public interface IGraphBuildContribution
{
    string Plugin { get; }
    string Source { get; }
    GraphBuild Build(string repositoryRoot, IReadOnlyList<CodeFile> files, IProgress<string>? progress, CancellationToken ct);
}
