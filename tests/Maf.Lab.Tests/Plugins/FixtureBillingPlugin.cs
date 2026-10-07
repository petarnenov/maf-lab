using Maf.Lab.Plugins.Abstractions;

namespace Maf.Lab.Tests.Plugins;

/// <summary>
/// The billing-shaped domain the core tests run on (extract-billing): the descriptor of the fake server the shared fakes
/// already speak (<c>FakeToolSource</c>, <c>FakeJev</c>'s routing stub, "run 4417"), installed through the plugin seam as
/// a test-owned plugin. Its plugin name is not "billing", so the real plugin's code never joins it whether that folder
/// is present or not; its domain id is. Sameness with the billing plugin's table is pinned by that plugin's own test,
/// not assumed here.
/// </summary>
public sealed class FixtureBillingPlugin : IMafPlugin, IContributesDomainBehaviour
{
    public const string PluginName = "fixture-billing";

    public string Name => PluginName;

    public IDomainBehaviour Behaviour { get; } = new StandInBillingBehaviour();

    /// <summary>The billing domain's [domain] table as of extract-billing, without its prompt (a file of the plugin's).</summary>
    public static DomainTable Domain { get; } = new()
    {
        Id = "billing",
        Order = 10,
        QuestionKey = "in_domain",
        Description = "Fee billing on a wealth-management platform: billing runs and their failure codes, fee schedules and fee tiers, "
            + "billable AUM and billing exclusions, invoices, fee adjustments and billing credits, billing periods and period close, "
            + "household fee aggregation, custodian fee debits, client fee disputes, terminations and refunds, and who may approve what.",
        SearchTool = "search_documents",
        GraphTools = ["trace_billing_relationships"],
        GuardContext = GuardContexts.Documents,
        ReadTools = new Dictionary<string, string>
        {
            ["get_billing_run_status"] = "Returns the current status of one billing run by its run id: status, billing period, account count and failure reason.",
            ["search_billing_runs"] = "Lists the firm's billing runs, optionally filtered by status (pending, running, completed, failed) and billing period.",
        },
        WriteTools = new Dictionary<string, string>
        {
            ["propose_fee_adjustment"] = "Proposes a change to one account's fee (a credit or an increase), for a person to confirm.",
        },
        ToolRequires = new Dictionary<string, string> { ["propose_fee_adjustment"] = "compliance" },
        ScopeSummary = new Dictionary<string, string>
        {
            ["en"] = "your firm's billing (fees, fee schedules, billing runs, fee adjustments)",
            ["bg"] = "с таксуването на Вашата фирма (такси, тарифи, билинг цикли, корекции на такси)",
        },
        Subject = "fee billing",
        Intent = new DomainIntent
        {
            Procedural = "what a named fee schedule, failure code or rule means or charges",
            Mixed = "one specific billing run identified by its run number, e.g. why run 4417 failed",
            Data = "billing runs: a status, which runs failed, a list of runs",
        },
    };

    public static PluginManifest Manifest() => new()
    {
        Name = PluginName,
        Kind = PluginKinds.Mcp,
        Scope = PluginScopes.Tenant,
        Environments = ["dev", "qa"],
        Description = "A test fixture: the billing-shaped domain of the shared fakes",
        Progress = "None — a fixture",
        Stopping = "None — a fixture",
        Domain = Domain,
    };
}

/// <summary>
/// The stand-in domain's behaviour: a fixed, deliberately simple test double, the fixture's rule rather than billing's.
/// A run-status read binds the one all-digit word after "run"; a run search binds nothing; the write is never bound; a
/// mixed question naming one run gets its status alongside; no summaries.
/// </summary>
public sealed class StandInBillingBehaviour : IDomainBehaviour
{
    public const string StatusTool = "get_billing_run_status";
    public const string SearchRunsTool = "search_billing_runs";

    public string Domain => FixtureBillingPlugin.Domain.Id;

    public (IReadOnlyDictionary<string, object?>? Arguments, string? Reason) BindRead(string tool, string question,
        IReadOnlyDictionary<string, DecisionAnswer> answers, string? focus, double minConfidence) => tool switch
        {
            StatusTool => RunIds(question) is [var run]
                ? (new Dictionary<string, object?> { ["runId"] = run }, null)
                : (null, $"{tool} needs one run id"),
            SearchRunsTool => (new Dictionary<string, object?>(), null),
            _ => (null, $"{tool} has no binding"),
        };

    public IReadOnlyList<DomainRoute> Alongside(string intent, string question, double confidence) =>
        intent == "Mixed" && RunIds(question) is [var run]
            ? [new DomainRoute(StatusTool, new Dictionary<string, object?> { ["runId"] = run }, confidence)]
            : [];

    /// <summary>The distinct all-digit words that follow the word "run".</summary>
    public static IReadOnlyList<string> RunIds(string question)
    {
        var words = question.Split([' ', '\t', '\n', ',', '.', '?', '!', ';', ':'], StringSplitOptions.RemoveEmptyEntries);
        return [.. words.Skip(1).Where((w, i) => words[i].Equals("run", StringComparison.OrdinalIgnoreCase) && w.All(char.IsAsciiDigit))
            .Distinct(StringComparer.Ordinal)];
    }
}
