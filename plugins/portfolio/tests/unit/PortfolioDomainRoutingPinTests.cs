using System.Text.Json.Nodes;
using Maf.Lab.Api.Agent;
using Maf.Lab.Api.Agent.Decisions;
using Maf.Lab.Plugins.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Maf.Lab.Tests;

/// <summary>
/// Pins today's domain routing before the domains become data (introduce-plugins task 1.1): the domain list, each
/// domain's search and graph tools, the tool → domain fallback, the data and code routers' closed question sets, the
/// domain questions and the data cards. A later change that builds these from plugin manifests must leave every pin as
/// it is for billing, portfolio and codebase — but one: introduce-plugins task 4.6 removes the billing fallback on purpose,
/// so an unknown tool belongs to no domain. The codebase domain's pins moved verbatim with it into its plugin (task 5.2,
/// CodeDomainRoutingPinTests), billing's with it (extract-billing, BillingDomainRoutingPinTests), and these, the portfolio
/// domain's, verbatim with it into its plugin (extract-portfolio): run in the view they had, portfolio alone.
/// </summary>
public class PortfolioDomainRoutingPinTests : IDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // The view these pins were written in: portfolio (from this plugin's manifest) as the only domain.
    private readonly IDisposable _domains =
        DomainCatalogue.Use(DomainCatalogue.Of([PortfolioPluginSupport.Descriptor], [new Maf.Lab.Plugins.Portfolio.PortfolioBehaviour()]));

    public void Dispose() => _domains.Dispose();

    [Fact]
    public void The_built_in_domain_in_trace_order()
    {
        Assert.Equal(["portfolio"], Domains.All);
    }

    [Fact]
    public void Each_domain_forces_its_own_search()
    {
        Assert.Equal(new Dictionary<string, string>
        {
            ["portfolio"] = "search_portfolio_documents",
        }, Domains.SearchTool.ToDictionary());
    }

    [Fact]
    public void Graph_tools_belong_to_the_domain_whose_server_offers_them()
    {
        Assert.Empty(Domains.GraphTool);
    }

    [Theory]
    [InlineData("search_portfolio_documents", "portfolio")]
    [InlineData("get_household_portfolio", "portfolio")]
    [InlineData("get_aum_history", "portfolio")]
    [InlineData("list_my_accounts", "portfolio")]
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
        Assert.Equal(["get_household_portfolio", "get_aum_history", "list_my_accounts"], DataToolRouter.ReadTools);
        Assert.Equal(new Dictionary<string, string>
        {
            ["get_household_portfolio"] = "portfolio",
            ["get_aum_history"] = "portfolio",
            ["list_my_accounts"] = "portfolio",
        }, DataToolRouter.ToolDomain.ToDictionary());
        Assert.Null(DataToolRouter.WriteTool);
        Assert.Equal(["tool_get_aum_history", "tool_get_household_portfolio", "tool_list_my_accounts"],
            DataToolRouter.Questions().Select(q => q.Key).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Each_domain_has_its_own_domain_question()
    {
        Assert.Equal(new Dictionary<string, string>
        {
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
            "intent", "in_portfolio", "tool_get_aum_history", "tool_get_household_portfolio", "tool_list_my_accounts",
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
