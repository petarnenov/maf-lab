using Maf.Lab.Indexing.Corpus;
using Maf.Lab.Indexing.Pipeline;
using Maf.Lab.Retrieval.Billing;
using Maf.Lab.Retrieval.Configuration;
using Maf.Lab.Retrieval.Graph;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Maf.Lab.Indexing.Graph;

/// <summary>
/// Builds the requested subgraphs and writes them through the one maintenance path: nodes, then edges, then removal of
/// what an older run left. Progress is reported per written item (stage "billing" or "code"), so the bar moves while
/// a large batch is written.
/// </summary>
public sealed class GraphBuildService(TenantScopedGraphMaintenance graph, IOptions<IndexingOptions> indexing, IConfiguration configuration)
{
    private const int WriteBatch = 500;

    /// <summary>The folders whose C# is part of the code graph.</summary>
    public static readonly IReadOnlyList<string> CodeFolders = ["src", "tests", "tools", "plugins"];

    public async Task<IReadOnlyList<GraphBuildSummary>> RunAsync(IReadOnlyCollection<string> sources, IProgress<IndexProgress>? progress, CancellationToken ct)
    {
        await graph.EnsureSchemaAsync(ct);
        var runId = $"{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..23];
        var summaries = new List<GraphBuildSummary>();
        var done = 0;
        var total = 0;
        foreach (var source in GraphSources.All.Where(sources.Contains))
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(new IndexProgress($"{source}: reading sources", done, total == 0 ? null : total));
            var filesRead = 0;
            var build = source == GraphSources.Billing
                ? BuildBilling()
                : BuildCode(new SyncProgress<IndexProgress>(p => { filesRead = p.Done - done; progress?.Report(p); }), done, total);
            // Files read count as work done, so the bar never moves back when writing starts.
            done += filesRead;
            total += filesRead + build.Nodes.Count + build.Edges.Count;
            progress?.Report(new IndexProgress($"{source}: writing", done, total));

            var nodes = GraphWriteCounts.None;
            foreach (var batch in build.Nodes.Chunk(WriteBatch))
            {
                nodes = nodes.Add(await graph.WriteNodesAsync(source, runId, batch, ct));
                done += batch.Length;
                progress?.Report(new IndexProgress($"{source}: writing", done, total));
            }
            var edges = GraphWriteCounts.None;
            foreach (var batch in build.Edges.Chunk(WriteBatch))
            {
                edges = edges.Add(await graph.WriteEdgesAsync(source, runId, batch, ct));
                done += batch.Length;
                progress?.Report(new IndexProgress($"{source}: writing", done, total));
            }
            progress?.Report(new IndexProgress($"{source}: removing stale", done, total));
            var removed = await graph.RemoveStaleAsync(source, runId, ct);
            var counts = await graph.CountAsync(source, ct);
            summaries.Add(new GraphBuildSummary(source, nodes.Written, nodes.Unchanged, edges.Written, edges.Unchanged,
                removed.Nodes, removed.Edges, build.Rejected.Count + nodes.Rejected + edges.Rejected, build.UnresolvedCalls,
                counts.Nodes, counts.Edges));
        }
        return summaries;
    }

    public GraphBuild BuildBilling()
    {
        var accounts = File.ReadAllText(SeedPaths.Resolve(configuration, "Billing:AccountsSeedPath", "billing-accounts.json"));
        var households = File.ReadAllText(SeedPaths.Resolve(configuration, "Portfolio:SeedPath", "portfolio-households.json"));
        var runs = File.ReadAllText(SeedPaths.Resolve(configuration, "Billing:SeedPath", "billing-runs.json"));
        var corpus = BillingCorpus().LoadCorpus();
        var build = BillingGraphBuilder.Build(accounts, households, runs, corpus.Documents);
        return build with { Rejected = [.. build.Rejected, .. corpus.Rejected.Select(r => $"{r.Path}: {r.Reason}")] };
    }

    public GraphBuild BuildCode(IProgress<IndexProgress>? progress = null, int done = 0, int total = 0)
    {
        var root = RepositoryRoot();
        var projects = CodeGraphBuilder.FindProjects(root, CodeFolders);
        var options = indexing.Value;
        var files = RepositoryCorpusLoader.Load(root, [.. CodeFolders.Select(f => f + "/")], [], options.RepositoryMaxFileBytes)
            .Documents
            .Where(d => d.RelativePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            .Select(d => new CodeFile(d.RelativePath, d.Content))
            .ToList();
        var read = 0;
        var fileProgress = progress is null
            ? null
            : new SyncProgress<string>(path => progress.Report(new IndexProgress("code: reading sources", done + ++read, total + files.Count, path)));
        return CodeGraphBuilder.Build(projects, files, fileProgress);
    }

    /// <summary>The billing corpus: the indexer's configured corpus root, in the tenant layout.</summary>
    private IndexingOptions BillingCorpus() => new()
    {
        CorpusRoot = configuration["Graph:CorpusRoot"] ?? (indexing.Value.RepositoryLayout ? "" : indexing.Value.CorpusRoot),
        Layout = CorpusLayouts.Tenants,
    };

    /// <summary>Graph:RepositoryRoot if set; otherwise the repository containing the working directory.</summary>
    private string RepositoryRoot() =>
        configuration["Graph:RepositoryRoot"] is { Length: > 0 } configured
            ? Path.GetFullPath(configured)
            : new IndexingOptions { Layout = CorpusLayouts.Repository }.ResolveCorpusRoot();

    /// <summary>Reports on the calling thread, so the bar sees each file in order (Progress&lt;T&gt; would post).</summary>
    private sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
