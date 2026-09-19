using System.Text.Json;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Indexing.Corpus;
using Maf.Lab.Indexing.Pipeline;
using Maf.Lab.Retrieval.Store;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Maf.Lab.Indexing;

/// <summary>
/// dotnet run --project src/Maf.Lab.Indexing [-- command] [--tenants firm-a,shared] [--force] [--contextual on|off]
/// Commands: index (default) | drift | status | migrate --to dense_v2 [--batch 64] | chunks [--doc text] [--match text]
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
                    var summary = await services.GetRequiredService<IndexingPipeline>().RunAsync(
                        new IndexRequest { Tenants = tenants, Force = flags.ContainsKey("force") }, cts.Token);
                    Console.WriteLine(JsonSerializer.Serialize(summary, Pretty));
                    return summary.Rejected.Count > 0 ? 0 : 0;
                }
                case "chunks":
                {
                    // Lists chunk ids from the chunkers (no store needed) — for authoring eval datasets.
                    var options = services.GetRequiredService<IOptions<IndexingOptions>>().Value;
                    var match = flags.GetValueOrDefault("match");
                    foreach (var doc in CorpusLoader.Load(options.ResolveCorpusRoot(), tenants).Documents)
                    {
                        if (flags.TryGetValue("doc", out var docFilter) && !doc.DocId.Contains(docFilter, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                        foreach (var chunk in Chunking.ChunkBuilder.Build(doc, options.MaxChunkChars))
                        {
                            if (match is null || chunk.Text.Contains(match, StringComparison.OrdinalIgnoreCase) || chunk.SectionPath.Contains(match, StringComparison.OrdinalIgnoreCase))
                            {
                                Console.WriteLine($"{chunk.ChunkId}\t{chunk.SectionPath}");
                            }
                        }
                    }
                    return 0;
                }
                case "drift":
                {
                    var report = await services.GetRequiredService<DriftService>().ComputeAsync(tenants, cts.Token);
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
                    Console.Error.WriteLine($"Unknown command '{command}'. Use index | drift | status | migrate.");
                    return 2;
            }
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Cancelled.");
            return 130;
        }
    }

    private static HashSet<TenantId> AllTenants(IServiceProvider services)
    {
        var root = services.GetRequiredService<IOptions<IndexingOptions>>().Value.ResolveCorpusRoot();
        return CorpusLoader.Load(root).LayoutTenants.ToHashSet();
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
