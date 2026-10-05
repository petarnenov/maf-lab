using Maf.Lab.Api.Agent.Jev;
using Maf.Lab.Api.Feedback;
using Maf.Lab.Domain.Evals;
using Maf.Lab.Eval.Hosting;
using Maf.Lab.Eval.Judging;
using Maf.Lab.Eval.Reports;
using Maf.Lab.Eval.Suites;
using Maf.Lab.Indexing;
using Maf.Lab.Retrieval.Configuration;
using Maf.Lab.Retrieval.Jev;
using Maf.Lab.Retrieval.Models;
using Maf.Lab.Retrieval.Search;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Maf.Lab.Eval;

/// <summary>
/// dotnet run --project src/Maf.Lab.Eval -- --suite selection|retrieval|generation|injection|confirmation|intent|guardrail|domain|presentation|answer-check|code-route|generation-judge|all
///   [--rerank [--reranker llm,jev]] [--contextual] [--limit N] [--repeat N] [--import-feedback [--api-db "Data Source=..."]]
/// dotnet run --project src/Maf.Lab.Eval -- --suite graph-depth [--structural-only] [--limit N]   (a comparison: reported, never gated)
/// dotnet run --project src/Maf.Lab.Eval -- --suite retrieval-backends [--limit N]   (the Neo4j spike against Qdrant; a comparison)
/// dotnet run --project src/Maf.Lab.Eval -- --ask "question" [--firm firm-a] [--trace-json path]   (one turn, its trace printed)
/// Run on demand, and always after changing prompts, tool descriptions, the model, the tool set or chunking.
/// Exit code 1 when any suite is below its configured thresholds.
/// </summary>
public static class Program
{
    /// <summary>All the suites `all` runs. A comparison suite is not among them: it runs only when named.</summary>
    public static readonly string[] AllSuites =
        ["selection", "retrieval", "generation", "injection", "confirmation", "intent", "guardrail", "domain", "presentation", "answer-check",
            CodeRouteSuite.Name, "generation-judge"];

    /// <summary>
    /// Suites that compare settings side by side (add-graph-depth-eval): no thresholds, no baseline comparison, never
    /// accepted into the baseline. Several of their metrics are better lower, which the baseline gate cannot express.
    /// </summary>
    public static readonly IReadOnlySet<string> ComparisonSuites = new HashSet<string>(StringComparer.Ordinal) { GraphDepthSuite.Name, RetrievalBackendsSuite.Name };

    public static string[] SuitesOf(string suite) => suite == "all" ? AllSuites : suite.Split(',');

    /// <summary>The run against the accepted baseline; nothing for a comparison suite, which is never gated.</summary>
    public static IReadOnlyList<MetricComparison> CompareWithBaseline(EvalBaseline baseline, string suite, IReadOnlyList<EvalVariantResult> variants,
        EvalOptions options) =>
        ComparisonSuites.Contains(suite)
            ? []
            : RegressionGate.Compare(baseline.Suites.GetValueOrDefault(suite), variants, metric => options.ToleranceFor(suite, metric));

    /// <summary>The baseline with this run accepted, null when it is below its thresholds; unchanged for a comparison suite.</summary>
    public static EvalBaseline? AcceptInto(EvalBaseline accepted, string suite, IReadOnlyList<EvalVariantResult> variants, string runId,
        DateTimeOffset at) =>
        ComparisonSuites.Contains(suite) ? accepted : BaselineStore.Accept(accepted, suite, variants, runId, at);

    public static async Task<int> Main(string[] args)
    {
        // Ctrl+C and SIGTERM stop the run (stop-anything): the suite in hand ends at its next item, and what the suites
        // that finished wrote stays.
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
        using var sigterm = System.Runtime.InteropServices.PosixSignalRegistration.Create(
            System.Runtime.InteropServices.PosixSignal.SIGTERM, signal => { signal.Cancel = true; cts.Cancel(); });
        try
        {
            return await RunAsync(args, cts.Token);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            Console.Error.WriteLine(AfterCancel);
            return 130;
        }
    }

    /// <summary>What a stopped run says: what it kept, and what to run again.</summary>
    internal const string AfterCancel =
        "Cancelled. The reports of the suites that finished are kept; the suite in progress wrote nothing. Run it again for the rest.";

    private static async Task<int> RunAsync(string[] args, CancellationToken ct)
    {
        var flags = ParseFlags(args);
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "eval.json"), optional: true)
            .AddEnvironmentVariables()
            .Build();
        var options = configuration.GetSection(EvalOptions.Section).Get<EvalOptions>() ?? new EvalOptions();
        var root = DatasetWriter.ResolveRoot(string.IsNullOrWhiteSpace(options.Root) ? null : options.Root);

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

        if (flags.TryGetValue("ask", out var question))
        {
            await using var asking = await EvalAgentHost.StartAsync(configuration, ct);
            return await AskCommand.RunAsync(asking, flags.GetValueOrDefault("firm") ?? "firm-a", question, flags.GetValueOrDefault("trace-json"), ct);
        }

        var suite = flags.GetValueOrDefault("suite") ?? "all";
        var suites = SuitesOf(suite);
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
                ["systemPrompt"] = configuration["Agent:SystemPrompt"] ?? Maf.Lab.Api.Agent.SystemPrompt.DefaultVersion,
                ["retrievalMode"] = retrieval.Mode,
            };
            var repeat = flags.TryGetValue("repeat", out var r) ? int.Parse(r) : options.Repeat.GetValueOrDefault(name, 1);
            var runs = new List<IReadOnlyList<EvalVariantResult>>();
            var runIds = new List<string>();
            for (var run = 1; run <= Math.Max(1, repeat); run++)
            {
                var thisRun = repeat > 1 ? $"{stamp}-{name}-r{run}" : $"{stamp}-{name}";
                if (repeat > 1)
                {
                    Console.WriteLine($"   run {run}/{repeat}");
                }
                var one = await RunSuiteAsync(name, thisRun);
                runs.Add(one);
                if (repeat > 1)
                {
                    // Each run keeps its own report, ungated: the mean is what is compared and accepted.
                    runIds.Add(thisRun);
                    var single = new EvalReport(thisRun, name, started, DateTimeOffset.UtcNow, settings, one, one.All(v => v.Passed), []);
                    Console.WriteLine($"   run {run}/{repeat} → {await ReportWriter.WriteAsync(root, single, ct)}");
                }
            }
            if (repeat > 1)
            {
                settings["repeat"] = repeat.ToString(System.Globalization.CultureInfo.InvariantCulture);
                settings["runs"] = string.Join(",", runIds);
            }
            var variants = RepeatedRuns.Mean(runs);

            async Task<IReadOnlyList<EvalVariantResult>> RunSuiteAsync(string name, string runId) => name switch
            {
                "selection" => await new SelectionSuite(host).RunAsync(ctx, ct),
                "retrieval" => await RunRetrievalAsync(host, configuration, options, retrieval, ctx, flags, settings, ct),
                "generation" => await RunGenerationAsync(host, options, root, runId, ctx, settings, ct),
                "injection" => await new InjectionSuite(host).RunAsync(ctx, ct),
                "confirmation" => await new ConfirmationSuite(host).RunAsync(ctx, ct),
                "intent" => await new IntentSuite(host).RunAsync(ctx, ct),
                "guardrail" => await new GuardrailSuite(host.Services).RunAsync(ctx, ct),
                "answer-check" => await new AnswerCheckSuite(host.Services).RunAsync(ctx, ct),
                "generation-judge" => await RunGenerationJudgeAsync(host, options, root, runId, ctx, settings, ct),
                "domain" => await new DomainSuite(host).RunAsync(ctx, ct),
                "presentation" => await new PresentationSuite(host, host.Services.GetRequiredService<IChatClientFactory>()).RunAsync(ctx, ct),
                CodeRouteSuite.Name => await new CodeRouteSuite(host).RunAsync(ctx, ct),
                RetrievalBackendsSuite.Name => await RunRetrievalBackendsAsync(host, configuration, ctx, settings, ct),
                GraphDepthSuite.Name => await RunGraphDepthAsync(host, configuration, options, ctx, flags, settings, ct),
                _ => throw new ArgumentException($"Unknown suite '{name}'."),
            };
            var comparisons = CompareWithBaseline(baseline, name, variants, options);
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
            if (ComparisonSuites.Contains(name))
            {
                Console.WriteLine($"   {name} is a comparison: reported side by side, not gated and never part of the baseline");
            }
            if (accepting)
            {
                var next = AcceptInto(accepted, name, variants, runId, DateTimeOffset.UtcNow);
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

    /// <summary>
    /// The generation suite graded by Jev (adopt-meai-evaluation): refused without the key, each case kept in the result
    /// store, the run's HTML report written beside its JSON and Markdown ones.
    /// </summary>
    private static async Task<IReadOnlyList<EvalVariantResult>> RunGenerationAsync(EvalAgentHost host, EvalOptions options, string root,
        string runId, SuiteContext ctx, Dictionary<string, string> settings, CancellationToken ct)
    {
        var sp = host.Services;
        var grader = new JevGrader(sp.GetRequiredService<JevClient>(), options.Judge, sp.GetRequiredService<ILogger<JevGrader>>());
        GenerationSuite.RequireKey(grader);
        var suite = new GenerationSuite(host, GradeReport.Configure(root, runId, new JevGenerationEvaluator(grader)));
        var variants = await suite.RunAsync(ctx, ct);
        settings["judgeModel"] = grader.Model;
        AddTokens(settings, suite.InputTokens);
        Console.WriteLine($"   report → {await GradeReport.WriteHtmlAsync(root, runId, GenerationSuite.ScenarioPrefix, ct)}");
        return variants;
    }

    /// <summary>Input tokens the grade was charged for, summed over a repeated suite's runs.</summary>
    private static void AddTokens(Dictionary<string, string> settings, int tokens) =>
        settings["judgeInputTokens"] = ((settings.TryGetValue("judgeInputTokens", out var t) ? int.Parse(t, System.Globalization.CultureInfo.InvariantCulture) : 0) + tokens)
            .ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The generation grade measured on labelled answers, without the agent; refused without the key.</summary>
    private static async Task<IReadOnlyList<EvalVariantResult>> RunGenerationJudgeAsync(EvalAgentHost host, EvalOptions options, string root,
        string runId, SuiteContext ctx, Dictionary<string, string> settings, CancellationToken ct)
    {
        var sp = host.Services;
        var grader = new JevGrader(sp.GetRequiredService<JevClient>(), options.Judge, sp.GetRequiredService<ILogger<JevGrader>>());
        GenerationSuite.RequireKey(grader);
        var suite = new GenerationJudgeSuite(sp.GetRequiredService<JevAnswerCheck>(),
            GradeReport.Configure(root, runId, new JevGenerationEvaluator(grader)));
        var variants = await suite.RunAsync(ctx, ct);
        settings["judgeModel"] = grader.Model;
        AddTokens(settings, suite.InputTokens);
        Console.WriteLine($"   report → {await GradeReport.WriteHtmlAsync(root, runId, GenerationJudgeSuite.ScenarioPrefix, ct)}");
        return variants;
    }

    private static async Task<IReadOnlyList<EvalVariantResult>> RunRetrievalBackendsAsync(EvalAgentHost host, IConfiguration configuration,
        SuiteContext ctx, Dictionary<string, string> settings, CancellationToken ct)
    {
        // The portfolio collection's services, as the retrieval suite builds them; both domains' copies are checked first.
        var portfolioConfig = new ConfigurationBuilder().AddConfiguration(configuration).AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Qdrant:Collection"] = Maf.Lab.Domain.Portfolio.PortfolioCollections.Chunks,
            ["Qdrant:MetaCollection"] = Maf.Lab.Domain.Portfolio.PortfolioCollections.Meta,
        }).Build();
        await using var portfolio = new ServiceCollection().AddLogging().AddMafIndexing(portfolioConfig).BuildServiceProvider();
        settings["backends"] = "qdrant,neo4j";
        settings["neo4jSearch"] = "eval-only GraphChunkSearch over chunks copied by make neo4j-chunks";
        return await new RetrievalBackendsSuite().RunAsync(ctx,
            [new BackendDomain("billing", host.Services), new BackendDomain("portfolio", portfolio)], ct);
    }

    private static async Task<IReadOnlyList<EvalVariantResult>> RunGraphDepthAsync(EvalAgentHost host, IConfiguration configuration, EvalOptions options, SuiteContext ctx,
        Dictionary<string, string> flags, Dictionary<string, string> settings, CancellationToken ct)
    {
        var structuralOnly = flags.ContainsKey("structural-only");
        // Every variant runs on a codebase server the suite starts itself, pinned to its depth; the stack's is unpinned.
        settings["codeServer"] = "in-process, pinned per variant";
        settings["depths"] = string.Join(",", GraphDepthSuite.Variants.Select(v => v.Depth));
        settings["layers"] = structuralOnly ? "structural" : "structural,end-to-end";
        var grader = structuralOnly ? null
            : new JevGrader(host.Services.GetRequiredService<JevClient>(), options.Judge, host.Services.GetRequiredService<ILogger<JevGrader>>());
        if (grader is not null)
        {
            settings["judgeModel"] = grader.Model;
        }
        return await new GraphDepthSuite(configuration, grader).RunAsync(ctx, structuralOnly, ct);
    }

    private static async Task<IReadOnlyList<EvalVariantResult>> RunRetrievalAsync(EvalAgentHost host, IConfiguration configuration, EvalOptions options,
        RetrievalOptions retrieval, SuiteContext ctx, Dictionary<string, string> flags, Dictionary<string, string> settings, CancellationToken ct)
    {
        var search = host.Services.GetRequiredService<DocumentSearchService>();
        var variants = RetrievalSuite.DefaultVariants(search, retrieval.DenseVector, flags.ContainsKey("rerank"),
            retrieval.DenseFloorFor(host.Services.GetRequiredService<IOptions<ModelOptions>>().Value, retrieval.DenseVector), retrieval.SparseFloor,
            retrieval.RelevanceGateEnabled, flags.GetValueOrDefault("reranker")?.Split(','),
            retrieval.RerankEnabled ? retrieval.Reranker : null).ToList();
        settings["reranker"] = retrieval.RerankEnabled ? retrieval.Reranker : "none";
        settings["relevanceGate"] = retrieval.RelevanceGateEnabled.ToString();
        settings["relevanceFloor"] = retrieval.RelevanceFloor.ToString(System.Globalization.CultureInfo.InvariantCulture);
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
        // The portfolio domain's cases, searched in its own collection exactly as production searches it.
        var portfolioConfig = new ConfigurationBuilder().AddConfiguration(configuration).AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Qdrant:Collection"] = Maf.Lab.Domain.Portfolio.PortfolioCollections.Chunks,
            ["Qdrant:MetaCollection"] = Maf.Lab.Domain.Portfolio.PortfolioCollections.Meta,
        }).Build();
        var portfolio = new ServiceCollection().AddLogging().AddMafIndexing(portfolioConfig).BuildServiceProvider();
        variants.Add(new RetrievalVariant("portfolio-hybrid", variants[0].Settings, portfolio.GetRequiredService<DocumentSearchService>(), true, "portfolio"));
        settings["portfolioCollection"] = Maf.Lab.Domain.Portfolio.PortfolioCollections.Chunks;
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
            await portfolio.DisposeAsync();
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
