using System.Text.Json;
using Maf.Lab.Api.Agent;
using Maf.Lab.Api.Agent.Decisions;
using Maf.Lab.Api.Agent.Tracing;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Domain.Tracing;
using Maf.Lab.Plugins.Abstractions;
using Maf.Lab.TestSupport;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Maf.Lab.Tests;

/// <summary>
/// The codebase domain's part of the data-routing request pins (introduce-plugins 1.1: in_codebase and code_need ride in
/// the one intent request), moved verbatim with the domain into its plugin (5.2) and run in the three-domain view.
/// </summary>
public class CodeRoutingRequestTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly IntentOptions Routing = new() { RouteDataTools = true };

    private static (DecisionIntentClassifier Classifier, FakeJev Jev) Classifier(FakeJev? jev = null, IntentOptions? options = null)
    {
        jev ??= new FakeJev();
        var loggers = LoggerFactory.Create(_ => { });
        var o = Options.Create(options ?? Routing);
        return (new DecisionIntentClassifier(new FakeDecisionEngine(jev), o, loggers), jev);
    }

    [Fact]
    public async Task Routing_questions_travel_in_the_intent_request_and_leave_the_state_alone()
    {
        using var domains = CodePluginSupport.Use();
        var (classifier, jev) = Classifier();

        var decision = await classifier.ClassifyAsync("status of run 9981", Ct);

        var body = JsonDocument.Parse(Assert.Single(jev.Requests).Body).RootElement;
        Assert.Equal(["user_question"], body.GetProperty("state").EnumerateObject().Select(p => p.Name));
        var questions = body.GetProperty("questions");
        // The prompt-screening battery (injection-defense) also rides in the intent request, between the domain and the
        // routing questions; the routing questions still travel here and the state stays the user's question alone.
        string[] expected = ["intent", "in_domain", "in_portfolio", "in_codebase", .. GuardQuestions.PromptIds,
            "tool_get_billing_run_status", "tool_search_billing_runs", "tool_propose_fee_adjustment",
            "tool_get_household_portfolio", "tool_get_aum_history", "tool_list_my_accounts",
            // (Billing's own run_status question is the billing plugin's; the core's stand-in for billing asks none.)
            // What a codebase question needs (route-structural-code-questions) rides in the same request, after them.
            "code_need"];
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

    [Fact]
    public async Task With_routing_off_the_request_carries_no_routing_questions()
    {
        using var domains = CodePluginSupport.Use();
        var (classifier, jev) = Classifier(options: new IntentOptions { RouteDataTools = false });

        var decision = await classifier.ClassifyAsync("status of run 4417", Ct);

        // Routing off: no tool_* or run_status questions. The intent, domain and prompt-screening questions
        // (injection-defense) still ride in the request, with one domain question per domain (add-portfolio-domain).
        // Code routing is its own switch: data routing off leaves the code-route question in the request.
        string[] expected = [.. new[] { "intent", "in_domain", "in_portfolio", "in_codebase", "code_need" }.Concat(GuardQuestions.PromptIds).Order()];
        Assert.Equal(expected, Assert.Single(jev.Questions).Keys.Order());
        Assert.Null(decision.Route);
        Assert.Null(decision.Routing);
        Assert.Null(decision.RouteReason);
    }
}
