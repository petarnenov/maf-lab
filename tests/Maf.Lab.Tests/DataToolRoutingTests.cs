using System.Text.Json;
using Maf.Lab.Api.Agent;
using Maf.Lab.Api.Agent.Jev;
using Maf.Lab.Api.Agent.Tracing;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Domain.Tracing;
using Maf.Lab.Retrieval.Jev;
using Maf.Lab.TestSupport;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Maf.Lab.Tests;

/// <summary>
/// Pre-routing of read tools on data turns: Jev names the tool in the intent request, code takes only the arguments
/// from the question, a write is never routed, and anything unclear leaves the turn to the model as before.
/// </summary>
public class DataToolRoutingTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly JevOptions Routing = new() { RouteDataTools = true };

    private static RoutingAnswer Answer(double status = 0.1, double runs = 0.1, double write = 0.02, string? runStatus = "none", double confidence = 1.0) =>
        new(new Dictionary<string, double>
        {
            ["get_billing_run_status"] = status,
            ["search_billing_runs"] = runs,
            ["propose_fee_adjustment"] = write,
        }, runStatus, confidence);

    [Fact]
    public void Status_of_one_run_routes_to_get_billing_run_status_with_its_id()
    {
        var (route, reason) = DataToolRouter.Route("status of run 4417", Answer(status: 0.93, runs: 0.74), Routing);

        Assert.Null(reason);
        Assert.Equal("get_billing_run_status", route!.Tool);
        Assert.Equal("4417", route.Arguments["runId"]);
        Assert.Equal(0.93, route.Probability);
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
    public void A_write_is_never_routed_and_vetoes_a_read()
    {
        var (route, reason) = DataToolRouter.Route("status of run 4417 and credit 50 to A-1042", Answer(status: 0.93, write: 0.68), Routing);

        Assert.Null(route);
        Assert.Equal("a write is indicated (0.68)", reason);
        Assert.DoesNotContain(DataToolRouter.WriteTool, DataToolRouter.ReadTools);
    }

    [Fact]
    public void A_read_tool_below_the_floor_is_not_routed()
    {
        var (route, reason) = DataToolRouter.Route("koi runove se provaliha", Answer(runs: 0.73, status: 0.6), Routing);

        Assert.Null(route);
        Assert.Equal("no read tool is clear (search_billing_runs 0.73)", reason);
    }

    [Fact]
    public void An_unsure_status_is_left_out_rather_than_guessed()
    {
        var (route, _) = DataToolRouter.Route("show me the latest runs", Answer(runs: 0.9, runStatus: "pending", confidence: 0.4), Routing);

        Assert.Equal("search_billing_runs", route!.Tool);
        Assert.Empty(route.Arguments);
    }

    [Fact]
    public void A_missing_tool_answer_means_no_routing_answer_at_all()
    {
        var answers = new Dictionary<string, JevAnswer>
        {
            ["tool_get_billing_run_status"] = new("noul", null, null, null, 0.93),
            ["tool_search_billing_runs"] = new("noul", null, null, null, 0.2),
            ["run_status"] = new("choice", "none", null, 1.0),
        };

        Assert.Null(DataToolRouter.Read(answers));

        answers["tool_propose_fee_adjustment"] = new("noul", null, null, null, 0.02);
        // Every tool the request asked about must be answered, the portfolio domain's included.
        Assert.Null(DataToolRouter.Read(answers));

        answers["tool_get_household_portfolio"] = new("noul", null, null, null, 0.05);
        answers["tool_get_aum_history"] = new("noul", null, null, null, 0.05);
        Assert.Null(DataToolRouter.Read(answers));

        answers["tool_list_my_accounts"] = new("noul", null, null, null, 0.05);
        Assert.Equal(0.93, DataToolRouter.Read(answers)!.Tools["get_billing_run_status"]);
    }

    // ── the classifier ───────────────────────────────────────────────────────────────────────────────────────────

    private static (JevIntentClassifier Classifier, FakeJev Jev) Classifier(FakeJev? jev = null, JevOptions? options = null)
    {
        jev ??= new FakeJev();
        var loggers = LoggerFactory.Create(_ => { });
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [JevCredential.EnvironmentVariable] = FakeJev.TestKey }).Build();
        var credential = new JevCredential(configuration, loggers.CreateLogger<JevCredential>());
        var client = new HttpClient(new JevAuthHandler(credential) { InnerHandler = jev }) { BaseAddress = new Uri("https://jev.test/") };
        var o = Options.Create(options ?? Routing);
        return (new JevIntentClassifier(new JevClient(new RoutingClientFactory(client), credential, o), o, loggers), jev);
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
        string[] expected = ["intent", "in_domain", "in_portfolio", .. JevGuardQuestions.PromptIds,
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

    [Fact]
    public async Task With_routing_off_the_request_carries_no_routing_questions()
    {
        var (classifier, jev) = Classifier(options: new JevOptions { RouteDataTools = false });

        var decision = await classifier.ClassifyAsync("status of run 4417", Ct);

        // Routing off: no tool_* or run_status questions. The intent, domain and prompt-screening questions
        // (injection-defense) still ride in the request, with one domain question per domain (add-portfolio-domain).
        string[] expected = [.. new[] { "intent", "in_domain", "in_portfolio" }.Concat(JevGuardQuestions.PromptIds).Order()];
        Assert.Equal(expected, Assert.Single(jev.Questions).Keys.Order());
        Assert.Null(decision.Route);
        Assert.Null(decision.Routing);
        Assert.Null(decision.RouteReason);
    }

    [Fact]
    public async Task Only_a_data_intent_is_routed()
    {
        var (classifier, _) = Classifier();

        var decision = await classifier.ClassifyAsync("why did run 4417 fail", Ct);

        Assert.Equal(Intent.Mixed, decision.Intent);
        Assert.Null(decision.Route);
        Assert.Equal("intent is Mixed, not Data", decision.RouteReason);
    }

    [Fact]
    public async Task An_unused_data_intent_is_not_routed()
    {
        var (classifier, _) = Classifier(new FakeJev { Confidence = 0.3 });

        var decision = await classifier.ClassifyAsync("status of run 4417", Ct);

        Assert.Equal(Intent.Other, decision.Intent);
        Assert.Null(decision.Route);
    }

    // ── the turn ─────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>A model that picks the status tool itself when it has to, and otherwise answers from what it was given.</summary>
    private static ScriptedChatClient DataModel() => new((messages, _, _) =>
        messages.SelectMany(m => m.Contents).OfType<FunctionResultContent>().Any()
            ? ScriptedChatClient.Text("Run 4417 failed: the fee schedule is missing.")
            : ScriptedChatClient.Call("get_billing_run_status", new() { ["runId"] = "4417" }));

    private static List<TraceEvent> Trace(IEnumerable<SseEvent> events) =>
        ApiFactory.TracesOf(events).Select(t => t.Deserialize<TraceEvent>(Json)!).ToList();

    [Fact]
    public async Task A_routed_status_question_calls_the_tool_before_any_model_call()
    {
        using var api = new ApiFactory(DataModel()) { ExtraSettings = new Dictionary<string, string?> { ["Jev:RouteDataTools"] = "true" } };
        var client = api.ClientFor("adam", "firm-a", Role.ADVISOR);

        var events = await ApiFactory.ChatAsync(client, "status of run 4417");

        Assert.Equal(["get_billing_run_status"], api.Tools.Invocations);
        // One model call, and it already holds the result: the tool-choosing call is gone.
        var request = Assert.Single(api.Chat.Requests);
        Assert.Contains(request.Messages.SelectMany(m => m.Contents).OfType<FunctionResultContent>(), _ => true);
        Assert.Contains("Run 4417 failed", ApiFactory.AnswerOf(events));

        var trace = Trace(events);
        var intent = trace.Single(t => t.Kind == TraceKinds.Intent);
        Assert.Contains("routing get_billing_run_status", intent.Title);
        var routing = intent.Data.GetProperty("routing");
        Assert.Equal("get_billing_run_status", routing.GetProperty("routedTool").GetString());
        Assert.Equal("4417", routing.GetProperty("arguments").GetProperty("runId").GetString());
        Assert.Equal(0.93, routing.GetProperty("tools").GetProperty("get_billing_run_status").GetDouble());
        Assert.False(intent.Data.GetProperty("forcedRetrieval").GetBoolean());
        var forced = trace.Single(t => t.Kind == TraceKinds.ToolForced);
        Assert.StartsWith("Routed get_billing_run_status", forced.Title);
        // Issued before the model was asked anything, and audited like any other call.
        Assert.True(trace.IndexOf(forced) < trace.FindIndex(t => t.Kind == TraceKinds.ModelRequest));
        Assert.Contains(trace, t => t.Kind == TraceKinds.Audit && t.Title.Contains("get_billing_run_status"));
    }

    [Fact]
    public async Task An_unrouted_data_question_is_left_to_the_model()
    {
        using var api = new ApiFactory(DataModel(), jev: new FakeJev { Choose = _ => "data" })
        {
            ExtraSettings = new Dictionary<string, string?> { ["Jev:RouteDataTools"] = "true" },
        };
        var client = api.ClientFor("adam", "firm-a", Role.ADVISOR);

        var events = await ApiFactory.ChatAsync(client, "status of run 4417 and run 4418");

        var trace = Trace(events);
        Assert.DoesNotContain(trace, t => t.Kind == TraceKinds.ToolForced);
        Assert.Equal(2, api.Chat.Requests.Count);
        Assert.Contains("needs one run id", trace.Single(t => t.Kind == TraceKinds.Intent).Data.GetProperty("routing").GetProperty("reason").GetString());
    }

    [Fact]
    public async Task A_write_is_never_issued_on_the_models_behalf()
    {
        var chat = new ScriptedChatClient((_, _, _) => ScriptedChatClient.Text("I can propose that adjustment if you confirm the amount."));
        using var api = new ApiFactory(chat, jev: new FakeJev { Choose = _ => "data" })
        {
            ExtraSettings = new Dictionary<string, string?> { ["Jev:RouteDataTools"] = "true" },
        };
        var client = api.ClientFor("adam", "firm-a", Role.ADVISOR);

        var events = await ApiFactory.ChatAsync(client, "reduce the fee on A-1043 by 50");

        var trace = Trace(events);
        Assert.DoesNotContain(trace, t => t.Kind == TraceKinds.ToolForced);
        Assert.Empty(api.Tools.Invocations);
        Assert.Equal("a write is indicated (0.80)",
            trace.Single(t => t.Kind == TraceKinds.Intent).Data.GetProperty("routing").GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Routing_off_leaves_data_turns_as_they_were()
    {
        using var api = new ApiFactory(DataModel()) { ExtraSettings = new Dictionary<string, string?> { ["Jev:RouteDataTools"] = "false" } };
        var client = api.ClientFor("adam", "firm-a", Role.ADVISOR);

        var events = await ApiFactory.ChatAsync(client, "status of run 4417");

        Assert.Equal(2, api.Chat.Requests.Count);
        Assert.Equal(["get_billing_run_status"], api.Tools.Invocations);
        Assert.Equal(JsonValueKind.Null, Trace(events).Single(t => t.Kind == TraceKinds.Intent).Data.GetProperty("routing").ValueKind);
    }
}

file sealed class RoutingClientFactory(HttpClient client) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => client;
}
