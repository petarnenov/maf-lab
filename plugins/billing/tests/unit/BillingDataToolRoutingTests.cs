using System.Text.Json;
using Maf.Lab.Api.Agent;
using Maf.Lab.Api.Agent.Decisions;
using Maf.Lab.Plugins.Abstractions;
using Maf.Lab.Plugins.Billing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Maf.Lab.Tests;

/// <summary>
/// Billing's own routing of read tools on data turns (moved verbatim from the core's DataToolRoutingTests): the run
/// ids in every script, the run-status question, the month and year of a period, and what the router leaves to the model.
/// </summary>
public class BillingDataToolRoutingTests : IDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly IntentOptions Routing = new() { RouteDataTools = true };

    // The view these tests were written in: billing from this plugin, portfolio beside it (the core tests' stand-in).
    private readonly IDisposable _domains = BillingPluginSupport.Use();

    public void Dispose() => _domains.Dispose();

    private static RoutingAnswer Answer(double status = 0.1, double runs = 0.1, double write = 0.02, string? runStatus = "none", double confidence = 1.0) =>
        Billing(new Dictionary<string, double>
        {
            ["get_billing_run_status"] = status,
            ["search_billing_runs"] = runs,
            ["propose_fee_adjustment"] = write,
        }, runStatus, confidence);

    private static RoutingAnswer Billing(Dictionary<string, double> tools, string? runStatus, double confidence) =>
        new(tools, runStatus is null ? new Dictionary<string, DecisionAnswer>()
            : new Dictionary<string, DecisionAnswer> { [BillingBehaviour.StatusQuestionId] = new(runStatus, confidence, null, null) });

    private static (DecisionIntentClassifier Classifier, FakeJev Jev) Classifier(FakeJev? jev = null, IntentOptions? options = null)
    {
        jev ??= new FakeJev();
        var loggers = LoggerFactory.Create(_ => { });
        var o = Options.Create(options ?? Routing);
        return (new DecisionIntentClassifier(new FakeDecisionEngine(jev), o, loggers), jev);
    }

    [Theory]
    [InlineData("Какъв е статусът на рън 4417?", "4417")]
    [InlineData("kakav e statusat na run 4418", "4418")]
    [InlineData("What state is run #6203 in?", "6203")]
    public void Run_ids_are_read_in_every_script(string question, string id)
    {
        var (route, _) = DataToolRouter.Route(question, Answer(status: 0.92), Routing);

        Assert.Equal(id, route!.Arguments["runId"]);
    }

    [Fact]
    public void Runs_by_status_route_to_search_billing_runs()
    {
        var (route, reason) = DataToolRouter.Route("Which billing runs failed?", Answer(status: 0.57, runs: 0.92, runStatus: "failed"), Routing);

        Assert.Null(reason);
        Assert.Equal("search_billing_runs", route!.Tool);
        Assert.Equal("failed", route.Arguments["status"]);
        Assert.False(route.Arguments.ContainsKey("periodFrom"));
    }

    [Theory]
    [InlineData("List our billing runs for June 2026.", "2026-06-01", "2026-06-30")]
    [InlineData("Покажи рънове за февруари 2026", "2026-02-01", "2026-02-28")]
    [InlineData("pokaji runove za dekemvri 2025", "2025-12-01", "2025-12-31")]
    public void A_month_and_year_become_the_period(string question, string from, string to)
    {
        var (route, reason) = DataToolRouter.Route(question, Answer(runs: 0.93), Routing);

        Assert.Null(reason);
        Assert.Equal(from, route!.Arguments["periodFrom"]);
        Assert.Equal(to, route.Arguments["periodTo"]);
        Assert.False(route.Arguments.ContainsKey("status"));
    }

    [Theory]
    [InlineData("which runs failed last month?")]
    [InlineData("Кои рънове се провалиха този месец?")]
    [InlineData("runs from 2026")]
    [InlineData("runs in Q2")]
    [InlineData("list runs for June")]
    [InlineData("runs between June 2026 and July 2026")]
    public void A_time_expression_the_router_cannot_parse_is_left_to_the_model(string question)
    {
        var (route, reason) = DataToolRouter.Route(question, Answer(runs: 0.9, runStatus: "failed"), Routing);

        Assert.Null(route);
        Assert.Contains("time expression", reason);
    }

    [Fact]
    public void The_run_id_must_agree_with_the_chosen_tool()
    {
        Assert.Contains("needs one run id", DataToolRouter.Route("status of run 4417 and run 4418", Answer(status: 0.9), Routing).Reason);
        Assert.Contains("needs one run id", DataToolRouter.Route("what is the status", Answer(status: 0.9), Routing).Reason);
        Assert.Contains("names a run", DataToolRouter.Route("list runs like run 4417", Answer(runs: 0.9), Routing).Reason);
    }

    [Fact]
    public void An_unsure_status_is_left_out_rather_than_guessed()
    {
        var (route, _) = DataToolRouter.Route("show me the latest runs", Answer(runs: 0.9, runStatus: "pending", confidence: 0.4), Routing);

        Assert.Equal("search_billing_runs", route!.Tool);
        Assert.Empty(route.Arguments);
    }

    [Fact]
    public async Task Routing_questions_travel_in_the_intent_request_and_leave_the_state_alone()
    {
        var (classifier, jev) = Classifier();

        var decision = await classifier.ClassifyAsync("status of run 9981", Ct);

        var body = JsonDocument.Parse(Assert.Single(jev.Requests).Body).RootElement;
        Assert.Equal(["user_question"], body.GetProperty("state").EnumerateObject().Select(p => p.Name));
        var questions = body.GetProperty("questions");
        // The prompt-screening battery (injection-defense) also rides in the intent request, between the domain and the
        // routing questions; the routing questions still travel here and the state stays the user's question alone.
        // The codebase domain's questions ride here too while the code plugin is in use (its own tests pin them).
        string[] expected = ["intent", "in_domain", "in_portfolio", .. GuardQuestions.PromptIds,
            "tool_get_billing_run_status", "tool_search_billing_runs", "tool_propose_fee_adjustment",
            "tool_get_household_portfolio", "tool_get_aum_history", "tool_list_my_accounts", "run_status"];
        Assert.Equal(expected, questions.EnumerateObject().Select(q => q.Name));
        var tool = questions.GetProperty("tool_get_billing_run_status").GetProperty("instructions");
        Assert.StartsWith("get_billing_run_status: ", tool.GetProperty("tool").GetString());
        Assert.Equal("To answer `user_question`, is it necessary to call `tool`?", tool.GetProperty("question").GetString());
        Assert.DoesNotContain("9981", questions.GetRawText());

        Assert.Equal(Intent.Data, decision.Intent);
        Assert.Equal("get_billing_run_status", decision.Route!.Tool);
        Assert.Equal("9981", decision.Route.Arguments["runId"]);
        Assert.Equal(0.93, decision.Routing!.Tools["get_billing_run_status"]);
    }
}
