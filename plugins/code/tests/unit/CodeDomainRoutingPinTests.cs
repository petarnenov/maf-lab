using System.Text.Json.Nodes;
using Maf.Lab.Api.Agent;
using Maf.Lab.Api.Agent.Jev;
using Maf.Lab.Plugins.Code;
using Maf.Lab.Retrieval.Jev;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Maf.Lab.Tests;

/// <summary>
/// The codebase domain's part of the routing pins (introduce-plugins task 1.1), moved verbatim with the domain into its
/// plugin (task 5.2): run in the three-domain view the lab had when they were written — billing and portfolio built
/// in, codebase from this folder's manifest — every assertion is the one the core pin made.
/// </summary>
public class CodeDomainRoutingPinTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void The_three_domains_in_trace_order()
    {
        using var domains = CodePluginSupport.Use();
        Assert.Equal(["billing", "portfolio", "codebase"], Domains.All);
    }

    [Fact]
    public void Each_domain_forces_its_own_search()
    {
        using var domains = CodePluginSupport.Use();
        Assert.Equal(new Dictionary<string, string>
        {
            ["billing"] = "search_documents",
            ["portfolio"] = "search_portfolio_documents",
            ["codebase"] = "search_codebase",
        }, Domains.SearchTool.ToDictionary());
    }

    [Fact]
    public void Graph_tools_belong_to_the_domain_whose_server_offers_them()
    {
        using var domains = CodePluginSupport.Use();
        Assert.Equal(new Dictionary<string, string>
        {
            ["trace_billing_relationships"] = "billing",
            ["trace_code_symbol"] = "codebase",
            ["change_impact"] = "codebase",
        }, Domains.GraphTool.ToDictionary());
    }

    [Theory]
    [InlineData("search_documents", "billing")]
    [InlineData("search_portfolio_documents", "portfolio")]
    [InlineData("search_codebase", "codebase")]
    [InlineData("get_billing_run_status", "billing")]
    [InlineData("search_billing_runs", "billing")]
    [InlineData("get_household_portfolio", "portfolio")]
    [InlineData("get_aum_history", "portfolio")]
    [InlineData("list_my_accounts", "portfolio")]
    [InlineData("trace_billing_relationships", "billing")]
    [InlineData("trace_code_symbol", "codebase")]
    [InlineData("change_impact", "codebase")]
    // An unknown tool used to fall back to billing; introduce-plugins task 4.6 removed that fallback on purpose: it belongs
    // to no domain.
    [InlineData("some_unknown_tool", null)]
    public void A_stored_tool_is_attributed_to_its_domain(string tool, string? domain)
    {
        using var domains = CodePluginSupport.Use();
        Assert.Equal(domain, Domains.OfTool(tool));
    }

    [Fact]
    public void The_code_router_asks_one_closed_choice()
    {
        using var domains = CodePluginSupport.Use();
        Assert.Equal("code_need", CodeToolRouter.Question().Key);
        Assert.Equal(["callees", "callers", "impact", "none", "text"], CodeToolRouter.Criteria.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Each_domain_has_its_own_domain_question()
    {
        using var domains = CodePluginSupport.Use();
        Assert.Equal(new Dictionary<string, string>
        {
            ["billing"] = "in_domain",
            ["portfolio"] = "in_portfolio",
            ["codebase"] = "in_codebase",
        }, JevIntentClassifier.DomainQuestionIds.ToDictionary());
    }

    [Fact]
    public async Task One_intent_request_holds_every_domain_and_routing_question()
    {
        using var domains = CodePluginSupport.Use();
        var jev = new FakeJev { Choose = _ => "other" };
        await Classifier(jev).ClassifyAsync("How do I issue a billing credit?", Ct);

        var body = Assert.Single(jev.Requests).Body;
        var asked = JsonNode.Parse(body)!["questions"]!.AsObject().Select(q => q.Key).ToHashSet(StringComparer.Ordinal);
        string[] routing =
        [
            "intent", "in_domain", "in_portfolio", "in_codebase", "code_need", "run_status",
            "tool_get_aum_history", "tool_get_billing_run_status", "tool_get_household_portfolio", "tool_list_my_accounts",
            "tool_propose_fee_adjustment", "tool_search_billing_runs",
        ];
        Assert.Superset(routing.ToHashSet(StringComparer.Ordinal), asked);
        // Besides routing, only the prompt-screening battery rides in the same request.
        Assert.Equal(asked.Except(routing).Order(StringComparer.Ordinal), JevGuardQuestions.PromptIds.Order(StringComparer.Ordinal));
    }

    private static JevIntentClassifier Classifier(FakeJev jev)
    {
        var loggers = LoggerFactory.Create(_ => { });
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [JevCredential.EnvironmentVariable] = FakeJev.TestKey }).Build();
        var credential = new JevCredential(configuration, loggers.CreateLogger<JevCredential>());
        var client = new HttpClient(new JevAuthHandler(credential) { InnerHandler = jev }) { BaseAddress = new Uri("https://jev.test/") };
        var o = Options.Create(new JevOptions());
        return new JevIntentClassifier(new JevClient(new CodePinTestClients(client), credential, o), o, loggers);
    }
}

file sealed class CodePinTestClients(HttpClient client) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => client;
}
