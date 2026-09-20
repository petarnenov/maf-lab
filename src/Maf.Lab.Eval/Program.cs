using Maf.Lab.Api.Feedback;
using Maf.Lab.Domain.Evals;
using Maf.Lab.Eval.Hosting;
using Maf.Lab.Eval.Reports;
using Maf.Lab.Eval.Suites;
using Maf.Lab.Indexing;
using Maf.Lab.Retrieval.Configuration;
using Maf.Lab.Retrieval.Models;
using Maf.Lab.Retrieval.Search;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Maf.Lab.Eval;

/// <summary>
/// dotnet run --project src/Maf.Lab.Eval -- --suite selection|retrieval|generation|injection|all
///   [--rerank] [--contextual] [--limit N] [--import-feedback [--api-db "Data Source=..."]]
/// Run on demand, and always after changing prompts, tool descriptions, the model, the tool set or chunking.
/// Exit code 1 when any suite is below its configured thresholds.
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var flags = ParseFlags(args);
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "eval.json"), optional: true)
            .AddEnvironmentVariables()
            .Build();
        var options = configuration.GetSection(EvalOptions.Section).Get<EvalOptions>() ?? new EvalOptions();
        var root = DatasetWriter.ResolveRoot(string.IsNullOrWhiteSpace(options.Root) ? null : options.Root);
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
        var ct = cts.Token;

        if (flags.ContainsKey("import-feedback"))
        {
            var apiDb = flags.GetValueOrDefault("api-db") ?? $"Data Source={Path.Combine(Path.GetDirectoryName(root)!, "src", "Maf.Lab.Api", "maf-lab.db")}";
            var added = await FeedbackImporter.ImportAsync(apiDb, new DatasetWriter(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Evals:Root"] = root }).Build()), ct);
            Console.WriteLine($"Imported {added} labeled feedback row(s) into {root}.");
            if (!flags.ContainsKey("suite"))
            {
                return 0;
            }
        }

        var suite = flags.GetValueOrDefault("suite") ?? "all";
        var suites = suite == "all" ? new[] { "selection", "retrieval", "generation", "injection", "confirmation" } : suite.Split(',');
        var ctx = new SuiteContext(root, options, flags.TryGetValue("limit", out var l) ? int.Parse(l) : null, m => Console.WriteLine($"  {m}"),
            configuration["Retrieval:CorpusLanguage"] ?? "en");
        var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss");
        var allPassed = true;
        // The gate compares with what this repository has accepted; thresholds still answer "usable at all".
        var baseline = await BaselineStore.ReadAsync(root, ct);
        var accepting = flags.ContainsKey("accept-baseline");
        var accepted = baseline;

        await using var host = await EvalAgentHost.StartAsync(configuration, ct);
        var models = host.Services.GetRequiredService<IOptions<ModelOptions>>().Value;
        var retrieval = host.Services.GetRequiredService<IOptions<RetrievalOptions>>().Value;
        var qdrant = host.Services.GetRequiredService<IOptions<QdrantOptions>>().Value;

        foreach (var name in suites)
        {
            Console.WriteLine($"== {name}");
            var started = DateTimeOffset.UtcNow;
            var settings = new Dictionary<string, string>
            {
                ["chatModel"] = models.ChatModel,
                ["provider"] = models.Provider,
                ["denseVector"] = retrieval.DenseVector,
                ["embeddingModel"] = models.Embeddings.TryGetValue(retrieval.DenseVector, out var p) ? p.Model : "?",
                ["collection"] = qdrant.Collection,
                ["systemPrompt"] = configuration["Agent:SystemPrompt"] ?? "system.v1",
                ["retrievalMode"] = retrieval.Mode,
            };
            IReadOnlyList<EvalVariantResult> variants = name switch
            {
                "selection" => await new SelectionSuite(host).RunAsync(ctx, ct),
                "retrieval" => await RunRetrievalAsync(host, configuration, options, retrieval, ctx, flags, settings, ct),
                "generation" => await new GenerationSuite(host, new RubricJudge(host.Services.GetRequiredService<IChatClientFactory>())).RunAsync(ctx, ct),
                "injection" => await new InjectionSuite(host).RunAsync(ctx, ct),
                "confirmation" => await new ConfirmationSuite(host).RunAsync(ctx, ct),
                _ => throw new ArgumentException($"Unknown suite '{name}'."),
            };
            var comparisons = RegressionGate.Compare(baseline.Suites.GetValueOrDefault(name), variants, options.ToleranceFor(name));
            var regressed = RegressionGate.HasRegression(comparisons);
            var runId = $"{stamp}-{name}";
            var report = new EvalReport(runId, name, started, DateTimeOffset.UtcNow, settings, variants,
                variants.All(v => v.Passed) && !regressed, comparisons);
            var path = await ReportWriter.WriteAsync(root, report, ct);
            allPassed &= report.Passed;
            Console.WriteLine($"   {(report.Passed ? "PASSED" : "FAILED")} → {path}");
            foreach (var v in variants)
            {
                Console.WriteLine($"   {v.Name}: {string.Join(", ", v.Metrics.Select(m => $"{m.Key}={m.Value:0.###}"))}{(v.Thresholds.Count > 0 ? (v.Passed ? " ✓" : " ✗") : "")}");
            }
            foreach (var line in RegressionGate.Describe(comparisons, name))
            {
                Console.WriteLine($"   {line}");
            }
            if (accepting)
            {
                var next = BaselineStore.Accept(accepted, name, variants, runId, DateTimeOffset.UtcNow);
                if (next is null)
                {
                    Console.WriteLine($"   ✗ not accepted: {name} is below its thresholds");
                }
                else
                {
                    accepted = next;
                }
            }
        }
        if (accepting && !ReferenceEquals(accepted, baseline))
        {
            // Only ever here: a plain run must never move the baseline, or a re-run would launder a regression.
            var path = await BaselineStore.WriteAsync(root, accepted, ct);
            Console.WriteLine($"Baseline accepted → {path}");
        }
        return allPassed ? 0 : 1;
    }

    private static async Task<IReadOnlyList<EvalVariantResult>> RunRetrievalAsync(EvalAgentHost host, IConfiguration configuration, EvalOptions options,
        RetrievalOptions retrieval, SuiteContext ctx, Dictionary<string, string> flags, Dictionary<string, string> settings, CancellationToken ct)
    {
        var search = host.Services.GetRequiredService<DocumentSearchService>();
        var variants = RetrievalSuite.DefaultVariants(search, retrieval.DenseVector, flags.ContainsKey("rerank")).ToList();
        ServiceProvider? contextual = null;
        if (flags.ContainsKey("contextual"))
        {
            // Same corpus indexed with Indexing:ContextualRetrieval=true into a second collection:
            //   Qdrant__Collection=maf_chunks_ctx Qdrant__MetaCollection=maf_meta_ctx dotnet run --project src/Maf.Lab.Indexing -- index --contextual on
            var ctxConfig = new ConfigurationBuilder().AddConfiguration(configuration).AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Qdrant:Collection"] = options.ContextualCollection,
                ["Qdrant:MetaCollection"] = options.ContextualCollection + "_meta",
            }).Build();
            contextual = new ServiceCollection().AddLogging().AddMafIndexing(ctxConfig).BuildServiceProvider();
            variants.Add(new RetrievalVariant("hybrid+contextual", variants[0].Settings, contextual.GetRequiredService<DocumentSearchService>(), false));
            settings["contextualCollection"] = options.ContextualCollection;
        }
        try
        {
            return await new RetrievalSuite().RunAsync(ctx, variants, ct);
        }
        finally
        {
            if (contextual is not null)
            {
                await contextual.DisposeAsync();
            }
        }
    }

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
            flags[key] = i + 1 < args.Length && !args[i + 1].StartsWith("--") ? args[++i] : "true";
        }
        return flags;
    }
}
