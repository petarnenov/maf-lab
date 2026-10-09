using Maf.Lab.Indexing.Graph;
using Maf.Lab.Plugins.Abstractions;

namespace Maf.Lab.Plugins.CodeGraph;

public sealed class CodeGraphContribution : IGraphBuildContribution
{
    public string Plugin => "code";
    public string Source => "code";
    public GraphBuild Build(string repositoryRoot, IReadOnlyList<CodeFile> files, IProgress<string>? progress, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var projects = CodeGraphBuilder.FindProjects(repositoryRoot, ["src", "tests", "tools", "plugins"]);
        return CodeGraphBuilder.Build(projects, files, progress);
    }
}
