using System.Text.Json.Nodes;
using Maf.Lab.Api.Agent;
using Maf.Lab.Api.Agent.Decisions;
using Maf.Lab.Plugins.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Maf.Lab.Tests;

/// <summary>
/// The billing domain's part of the routing pins (introduce-plugins task 1.1), moved verbatim with the domain into its
/// plugin (extract-billing), as the codebase domain's were: run in the view the lab had when they were written —
/// billing from this folder's manifest, portfolio beside it (the core tests' stand-in since extract-portfolio) — every assertion is the one the core pin made.
/// </summary>
public class BillingDomainRoutingPinTests : IDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // The view these pins were written in: billing (from this plugin's manifest) and portfolio (the stand-in).
    private readonly IDisposable _domains = BillingPluginSupport.Use();

    public void Dispose() => _domains.Dispose();

    [Fact]
    public void The_built_in_domains_in_trace_order()
    {
        Assert.Equal(["billing", "portfolio"], Domains.All);
    }

    [Fact]
    public void Each_domain_forces_its_own_search()
    {
        Assert.Equal(new Dictionary<string, string>
        {
            ["billing"] = "search_documents",
            ["portfolio"] = "search_portfolio_documents",
        }, Domains.SearchTool.ToDictionary());
    }

    [Fact]
    public void Graph_tools_belong_to_the_domain_whose_server_offers_them()
    {
        Assert.Equal(new Dictionary<string, string>
        {
            ["trace_billing_relationships"] = "billing",
        }, Domains.GraphTool.ToDictionary());
    }

    [Theory]
    [InlineData("search_documents", "billing")]
    [InlineData("search_portfolio_documents", "portfolio")]
    [InlineData("get_billing_run_status", "billing")]
    [InlineData("search_billing_runs", "billing")]
    [InlineData("get_household_portfolio", "portfolio")]
    [InlineData("get_aum_history", "portfolio")]
    [InlineData("list_my_accounts", "portfolio")]
    [InlineData("trace_billing_relationships", "billing")]
    // An unknown tool used to fall back to billing; introduce-plugins task 4.6 removed that fallback on purpose: it belongs
    // to no domain.
    [InlineData("some_unknown_tool", null)]
    public void A_stored_tool_is_attributed_to_its_domain(string tool, string? domain)
    {
        Assert.Equal(domain, Domains.OfTool(tool));
    }

    [Fact]
    public void The_data_router_asks_about_a_closed_set_of_read_tools_and_one_veto()
    {
        Assert.Equal(["get_billing_run_status", "search_billing_runs", "get_household_portfolio", "get_aum_history", "list_my_accounts"],
            DataToolRouter.ReadTools);
        Assert.Equal(new Dictionary<string, string>
        {
            ["get_billing_run_status"] = "billing",
            ["search_billing_runs"] = "billing",
            ["get_household_portfolio"] = "portfolio",
            ["get_aum_history"] = "portfolio",
            ["list_my_accounts"] = "portfolio",
        }, DataToolRouter.ToolDomain.ToDictionary());
        Assert.Equal("propose_fee_adjustment", DataToolRouter.WriteTool);
        Assert.Equal(
            ["run_status", "tool_get_aum_history", "tool_get_billing_run_status", "tool_get_household_portfolio", "tool_list_my_accounts",
                "tool_propose_fee_adjustment", "tool_search_billing_runs"],
            DataToolRouter.Questions().Select(q => q.Key).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Each_domain_has_its_own_domain_question()
    {
        Assert.Equal(new Dictionary<string, string>
        {
            ["billing"] = "in_domain",
            ["portfolio"] = "in_portfolio",
        }, DecisionIntentClassifier.DomainQuestionIds.ToDictionary());
    }

    [Fact]
    public void Only_the_portfolio_read_tools_travel_as_cards()
    {
        Assert.Equal(new Dictionary<string, string>
        {
            ["get_household_portfolio"] = "maf-lab/holdings",
            ["get_aum_history"] = "maf-lab/aum-history",
            ["list_my_accounts"] = "maf-lab/accounts",
        }, DataCards.Tools.ToDictionary(t => t.Key, t => t.Value));
    }

    [Fact]
    public async Task One_intent_request_holds_every_domain_and_routing_question()
    {
        var jev = new FakeJev { Choose = _ => "other" };
        await Classifier(jev).ClassifyAsync("How do I issue a billing credit?", Ct);

        var body = Assert.Single(jev.Requests).Body;
        var asked = JsonNode.Parse(body)!["questions"]!.AsObject().Select(q => q.Key).ToHashSet(StringComparer.Ordinal);
        string[] routing =
        [
            "intent", "in_domain", "in_portfolio", "run_status",
            "tool_get_aum_history", "tool_get_billing_run_status", "tool_get_household_portfolio", "tool_list_my_accounts",
            "tool_propose_fee_adjustment", "tool_search_billing_runs",
        ];
        Assert.Superset(routing.ToHashSet(StringComparer.Ordinal), asked);
        // Besides routing, only the prompt-screening battery rides in the same request.
        Assert.Equal(asked.Except(routing).Order(StringComparer.Ordinal), GuardQuestions.PromptIds.Order(StringComparer.Ordinal));
    }

    private static DecisionIntentClassifier Classifier(FakeJev jev)
    {
        var loggers = LoggerFactory.Create(_ => { });
        var o = Options.Create(new IntentOptions());
        return new DecisionIntentClassifier(new FakeDecisionEngine(jev), o, loggers);
    }
}
