using System.Text.Json;
using Maf.Lab.Domain.Admin;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Hosting.Cli;
using Maf.Lab.Indexing.Corpus;
using Maf.Lab.Indexing.Pipeline;
using Maf.Lab.Retrieval.Store;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Maf.Lab.Indexing;

/// <summary>
/// dotnet run --project src/Maf.Lab.Indexing [-- command] [--tenants firm-a,shared] [--force] [--contextual on|off]
/// Commands: index (default) | drift | status | migrate --to <vector> [--batch 64] | rebuild --yes | chunks [--doc text] [--match text] [--tokens]
///           | graph [--only billing|code]
/// </summary>
public static class Program
{
    private static readonly JsonSerializerOptions Pretty = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static async Task<int> Main(string[] args)
    {
        var command = args.FirstOrDefault(a => !a.StartsWith("--")) ?? "index";
        var flags = ParseFlags(args);

        var builder = Host.CreateApplicationBuilder([]);
        builder.Configuration.AddJsonFile("indexing.json", optional: true).AddEnvironmentVariables();
        if (flags.TryGetValue("contextual", out var ctx))
        {
            builder.Configuration["Indexing:ContextualRetrieval"] = (ctx is "on" or "true").ToString();
        }
        builder.Services.AddMafIndexing(builder.Configuration);
        // The progress bar owns stderr's last line and ends in the run's outcome; informational logs would break into
        // it and repeat that outcome, so the console shows warnings and errors only.
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        // stdout carries only the JSON a command prints (drift, graph, index summaries): logs go to stderr with the bar.
        builder.Services.Configure<Microsoft.Extensions.Logging.Console.ConsoleLoggerOptions>(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
        using var host = builder.Build();
        var services = host.Services;
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        var tenants = flags.TryGetValue("tenants", out var t)
            ? t.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(ParseTenant).ToHashSet()
            : null;

        try
        {
            switch (command)
            {
                case "index":
                {
                    var summary = await IndexWithProgressAsync(services,
                        new IndexRequest { Tenants = tenants, Force = flags.ContainsKey("force") }, cts.Token);
                    Console.WriteLine(JsonSerializer.Serialize(summary, Pretty));
                    return summary.Rejected.Count > 0 ? 0 : 0;
                }
                case "chunks":
                {
                    // Lists chunk ids from the chunkers (no store needed) — for authoring eval datasets.
                    // --tokens adds each chunk's estimated token count and line span, and a summary against the ceiling.
                    var options = services.GetRequiredService<IOptions<IndexingOptions>>().Value;
                    var ceiling = IndexingPipeline.InputCeiling(services.GetRequiredService<IOptions<Retrieval.Configuration.ModelOptions>>().Value, options.ContextualRetrieval);
                    var withTokens = flags.ContainsKey("tokens");
                    var match = flags.GetValueOrDefault("match");
                    var estimates = new List<int>();
                    foreach (var doc in options.LoadCorpus(tenants).Documents)
                    {
                        if (flags.TryGetValue("doc", out var docFilter) && !doc.DocId.Contains(docFilter, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                        foreach (var chunk in Chunking.ChunkBuilder.Build(doc, options.ChunkBudget, ceiling))
                        {
                            if (match is null || chunk.Text.Contains(match, StringComparison.OrdinalIgnoreCase) || chunk.SectionPath.Contains(match, StringComparison.OrdinalIgnoreCase))
                            {
                                var tokens = Chunking.TokenEstimator.Estimate(chunk.SparseText);
                                estimates.Add(tokens);
                                Console.WriteLine(withTokens
                                    ? $"{chunk.ChunkId}\t{chunk.SectionPath}\t{tokens}\t{chunk.StartLine}-{chunk.EndLine}"
                                    : $"{chunk.ChunkId}\t{chunk.SectionPath}");
                            }
                        }
                    }
                    if (withTokens && estimates.Count > 0)
                    {
                        estimates.Sort();
                        Console.Error.WriteLine($"chunks={estimates.Count} estimated tokens: median={estimates[estimates.Count / 2]} " +
                            $"p95={estimates[(int)(estimates.Count * 0.95)]} max={estimates[^1]} ceiling={(ceiling?.ToString() ?? "none")} budget={options.ChunkBudget}");
                    }
                    return 0;
                }
                case "graph":
                {
                    var only = flags.GetValueOrDefault("only");
                    if (only is not null && !Retrieval.Graph.GraphSources.All.Contains(only))
                    {
                        Console.Error.WriteLine($"--only must be one of: {string.Join(", ", Retrieval.Graph.GraphSources.All)}.");
                        return 2;
                    }
                    var summaries = await GraphWithProgressAsync(services, only is null ? Retrieval.Graph.GraphSources.All : [only], cts.Token);
                    Console.WriteLine(JsonSerializer.Serialize(summaries, Pretty));
                    return 0;
                }
                case "drift":
                {
                    var report = await DriftWithProgressAsync(services, tenants, cts.Token);
                    Console.WriteLine(JsonSerializer.Serialize(report, Pretty));
                    return 0;
                }
                case "status":
                {
                    var store = services.GetRequiredService<TenantScopedMaintenance>();
                    await services.GetRequiredService<CollectionBootstrapper>().EnsureAsync(cts.Token);
                    foreach (var tenant in tenants ?? AllTenants(services))
                    {
                        var versions = await store.ModelVersionsAsync(tenant, cts.Token);
                        Console.WriteLine($"{tenant.Value}: {string.Join(", ", versions.Select(v => $"{v.ModelVersion}={v.Chunks}"))}");
                    }
                    return 0;
                }
                case "rebuild":
                {
                    // The one way to provision a new dense vector: Qdrant cannot add one to an existing collection.
                    var bootstrapper = services.GetRequiredService<CollectionBootstrapper>();
                    var current = await bootstrapper.DescribeChunkCollectionAsync(cts.Token);
                    Console.WriteLine(current is { } c
                        ? $"Rebuild discards collection '{c.Collection}' ({c.Points} points) and re-indexes the corpus with every configured dense vector."
                        : "No chunk collection yet; rebuild creates it and indexes the corpus.");
                    if (!flags.ContainsKey("yes"))
                    {
                        Console.Error.WriteLine("Nothing deleted. Re-run with --yes (make rebuild-index FORCE=1) to go ahead.");
                        return 1;
                    }
                    await bootstrapper.DeleteChunkCollectionAsync(cts.Token);
                    var summary = await IndexWithProgressAsync(services, new IndexRequest { Tenants = tenants, Force = true }, cts.Token);
                    Console.WriteLine(JsonSerializer.Serialize(summary, Pretty));
                    return 0;
                }
                case "migrate":
                {
                    var target = flags.GetValueOrDefault("to") ?? throw new ArgumentException("migrate requires --to <denseVectorName>");
                    var batch = uint.Parse(flags.GetValueOrDefault("batch") ?? "64");
                    var summary = await services.GetRequiredService<MigrationService>().RunAsync(
                        tenants ?? AllTenants(services), target, batch, cts.Token);
                    Console.WriteLine(JsonSerializer.Serialize(summary, Pretty));
                    return 0;
                }
                default:
                    Console.Error.WriteLine($"Unknown command '{command}'. Use index | drift | status | migrate | rebuild | graph.");
                    return 2;
            }
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Cancelled.");
            return 130;
        }
        catch (Exception ex) when (UnreachableService.Describe(ex,
            services.GetRequiredService<IOptions<Retrieval.Configuration.QdrantOptions>>().Value,
            services.GetRequiredService<IOptions<Retrieval.Configuration.ModelOptions>>().Value,
            services.GetRequiredService<IOptions<Retrieval.Graph.GraphOptions>>().Value) is { } unreachable)
        {
            Console.Error.WriteLine(unreachable);
            return 1;
        }
    }

    /// <summary>
    /// Runs the pipeline under a progress bar on stderr (stdout keeps the JSON summary), ending in one line that says
    /// whether it succeeded, failed or was cancelled. A failure names the exception type only, never its message.
    /// </summary>
    private static async Task<IndexRunSummary> IndexWithProgressAsync(IServiceProvider services, IndexRequest request, CancellationToken ct)
    {
        var collection = services.GetRequiredService<IOptions<Retrieval.Configuration.QdrantOptions>>().Value.Collection;
        using var bar = new ConsoleProgress($"index {collection}");
        try
        {
            var summary = await services.GetRequiredService<IndexingPipeline>().RunAsync(request with { Progress = new IndexProgressBar(bar) }, ct);
            bar.Succeed($"{summary.DocumentsIndexed} indexed, {summary.DocumentsUnchanged} unchanged, {summary.ChunksWritten} chunks written");
            return summary;
        }
        catch (OperationCanceledException)
        {
            bar.Cancel();
            throw;
        }
        catch (Exception ex)
        {
            bar.Fail(ex.GetType().Name);
            throw;
        }
    }

    /// <summary>
    /// Builds the graph under a progress bar on stderr (stdout keeps the JSON summary): the stage names the subgraph,
    /// the count is items read and written of the total. Ends in one line with what was written, unchanged and removed.
    /// </summary>
    private static async Task<IReadOnlyList<Graph.GraphBuildSummary>> GraphWithProgressAsync(IServiceProvider services, IReadOnlyCollection<string> sources, CancellationToken ct)
    {
        using var bar = new ConsoleProgress($"graph {string.Join("+", sources)}");
        try
        {
            var summaries = await services.GetRequiredService<Graph.GraphBuildService>().RunAsync(sources, new IndexProgressBar(bar), ct);
            bar.Succeed(string.Join("; ", summaries.Select(s =>
                $"{s.Source}: {s.NodesWritten + s.EdgesWritten} written, {s.NodesUnchanged + s.EdgesUnchanged} unchanged, " +
                $"{s.NodesRemoved + s.EdgesRemoved} removed ({s.NodesTotal} nodes, {s.EdgesTotal} edges)")));
            return summaries;
        }
        catch (OperationCanceledException)
        {
            bar.Cancel();
            throw;
        }
        catch (Exception ex)
        {
            bar.Fail(ex.GetType().Name);
            throw;
        }
    }

    /// <summary>
    /// Drift under a progress bar on stderr (stdout keeps the JSON report): reading the corpus, the tenants listed from
    /// the index, then the graph. Ends in one line with the index's and the graph's drift (add-graph-drift).
    /// </summary>
    private static async Task<Domain.Admin.DriftReport> DriftWithProgressAsync(IServiceProvider services, IReadOnlySet<TenantId>? tenants, CancellationToken ct)
    {
        using var bar = new ConsoleProgress("drift");
        try
        {
            var report = await services.GetRequiredService<DriftService>().ComputeAsync(tenants, ct, new IndexProgressBar(bar));
            bar.Succeed(DriftSummary(report));
            return report;
        }
        catch (OperationCanceledException)
        {
            bar.Cancel();
            throw;
        }
        catch (Exception ex)
        {
            bar.Fail(ex.GetType().Name);
            throw;
        }
    }

    /// <summary>"624 documents: index 0 stale, graph 0 out of sync" — or "graph unavailable" when the graph store could not be read.</summary>
    internal static string DriftSummary(Domain.Admin.DriftReport report) =>
        $"{report.TotalDocuments} documents: index {report.StaleDocuments} stale, " +
        (report.Graph is { Available: true } graph ? $"graph {graph.OutOfSync} out of sync" : "graph unavailable");

    private static HashSet<TenantId> AllTenants(IServiceProvider services)
    {
        return services.GetRequiredService<IOptions<IndexingOptions>>().Value.LoadCorpus().LayoutTenants.ToHashSet();
    }

    private static TenantId ParseTenant(string value) =>
        TenantId.TryParse(value.Trim(), out var tenant) ? tenant : throw new ArgumentException($"'{value}' is not a tenant id.");

    private static Dictionary<string, string> ParseFlags(string[] args)
    {
        var flags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--"))
            {
                continue;
            }
            var key = args[i][2..];
            var value = i + 1 < args.Length && !args[i + 1].StartsWith("--") ? args[++i] : "true";
            flags[key] = value;
        }
        return flags;
    }
}
