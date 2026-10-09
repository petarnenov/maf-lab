using System.Text.Json;
using Maf.Lab.Api.Agent;
using Maf.Lab.Api.Agent.Decisions;
using Maf.Lab.Api.BuiltIn;
using Maf.Lab.Plugins.Code;
using Maf.Lab.Plugins.Abstractions;
using Maf.Lab.Domain.Code;
using Maf.Lab.Domain.Graph;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Domain.Tracing;
using Maf.Lab.TestSupport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Maf.Lab.Tests;

/// <summary>
/// Structural code questions routed to the code graph (route-structural-code-questions): Jev names what a codebase
/// question needs in the intent request, code takes the one symbol or file from the question, and anything unclear keeps
/// the codebase search the turn always forced.
/// </summary>
public class CodeToolRoutingTests : IDisposable
{
    // The three-domain view these tests were written in: billing and portfolio (the core tests' stand-ins), codebase from this plugin.
    private readonly IDisposable _domains = CodePluginSupport.Use();

    public void Dispose() => _domains.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly IntentOptions Options_ = new();

    private static DomainVerdict Codebase(double p = 0.9) =>
        DomainVerdict.From(new Dictionary<string, double> { [CodePlugin.DomainId] = p, [BuiltInDomains.Billing] = 0.05 }, 0.5, 0.2);

    private static DecisionAnswer Need(string choice, double confidence = 0.9) => new(choice, confidence, null, null);

    private static (ToolRoute? Route, string? Reason) Route(string question, DecisionAnswer? answer, Intent intent = Intent.Procedural,
        DomainVerdict? domains = null, IntentOptions? o = null) =>
        DecisionIntentClassifier.PrimaryRoute(new CodebaseBehaviour(), question, answer, intent, domains ?? Codebase(), o ?? Options_);

    [Fact]
    public void The_options_bind_from_configuration_with_routing_on_and_a_floor_of_0_55()
    {
        Assert.True(Options_.RouteCodeTools);
        Assert.Equal(0.55, Options_.MinCodeRouteConfidence);

        var bound = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jev:RouteCodeTools"] = "false",
            ["Jev:MinCodeRouteConfidence"] = "0.75",
        }).Build().GetSection(IntentOptions.Section).Get<IntentOptions>()!;
        Assert.False(bound.RouteCodeTools);
        Assert.Equal(0.75, bound.MinCodeRouteConfidence);
    }

    [Theory]
    [InlineData("Who calls TenantScopedSearch.QueryAsync?", "callers")]
    [InlineData("What does ChatTurnRunner.RunAsync end up calling?", "callees")]
    [InlineData("Кой вика `TenantScopedSearch.QueryAsync`?", "callers")]
    [InlineData("koi vika TenantScopedSearch.QueryAsync", "callers")]
    public void One_symbol_and_a_direction_route_to_trace_code_symbol(string question, string direction)
    {
        var (route, reason) = Route(question, Need(direction));

        Assert.Null(reason);
        Assert.Equal(GraphTools.TraceCodeSymbol, route!.Tool);
        Assert.Equal(direction == "callers" ? "TenantScopedSearch.QueryAsync" : "ChatTurnRunner.RunAsync", route.Arguments["symbol"]);
        Assert.Equal(direction, route.Arguments["direction"]);
        Assert.Equal(0.9, route.Probability);
    }

    [Theory]
    [InlineData("What tests cover src/Maf.Lab.Retrieval/Store/TenantScopedSearch.cs?")]
    [InlineData("Кои тестове покриват src/Maf.Lab.Retrieval/Store/TenantScopedSearch.cs?")]
    public void One_file_routes_to_change_impact_and_its_segments_are_not_symbols(string question)
    {
        var (route, reason) = Route(question, Need("impact"));

        Assert.Null(reason);
        Assert.Equal(GraphTools.ChangeImpact, route!.Tool);
        Assert.Equal("src/Maf.Lab.Retrieval/Store/TenantScopedSearch.cs", route.Arguments["path"]);
        Assert.Empty(CodeToolRouter.Symbols(question));
    }

    [Fact]
    public void A_qualified_symbol_keeps_its_type_and_member() =>
        Assert.Equal(["TenantScopedSearch.QueryAsync"], CodeToolRouter.Symbols("who calls Maf.Lab.Retrieval.Store.TenantScopedSearch.QueryAsync"));

    [Fact]
    public void A_file_name_is_not_a_symbol() =>
        Assert.Empty(CodeToolRouter.Symbols("what calls the code in Program.cs?"));

    [Theory]
    [InlineData("Does TenantScopedSearch.QueryAsync call TenantFilter.For?", "callees", "2 symbols in the question")]
    [InlineData("Who calls the tenant filter?", "callers", "no Type.Member symbol in the question")]
    [InlineData("Which tests cover the tenant search?", "impact", "no file path in the question")]
    [InlineData("Compare src/A/X.cs and src/A/Y.cs impact", "impact", "2 file paths in the question")]
    [InlineData("How does TenantScopedSearch.QueryAsync work?", "text", "needs text, not the graph")]
    [InlineData("What is a fee schedule?", "none", "needs none, not the graph")]
    public void Anything_the_router_cannot_pin_down_is_not_routed(string question, string need, string reason)
    {
        var (route, why) = Route(question, Need(need));

        Assert.Null(route);
        Assert.Equal(reason, why);
    }

    [Theory]
    [InlineData("Which tests cover web/src/chat/ChatPage.tsx?", "impact", "no file path in the question")]
    [InlineData("Какво вика ChatPage във web/src/chat/ChatPage.tsx?", "callees", "no Type.Member symbol in the question")]
    [InlineData("Who renders the CodeSnippetsPanel component?", "callers", "no Type.Member symbol in the question")]
    public void The_web_code_is_not_in_the_graph_so_its_questions_keep_the_search(string question, string need, string reason)
    {
        // The code graph holds the C# backend only (Roslyn): a web path or component is never a route's argument.
        var (route, why) = Route(question, Need(need));

        Assert.Null(route);
        Assert.Equal(reason, why);
    }

    [Fact]
    public void A_confidence_below_the_floor_is_not_routed()
    {
        var (route, reason) = Route("Who calls TenantScopedSearch.QueryAsync?", Need("callers", 0.5));

        Assert.Null(route);
        Assert.Equal("low confidence (0.50)", reason);
    }

    [Fact]
    public void Small_talk_another_primary_domain_or_no_answer_route_nothing()
    {
        const string question = "Who calls TenantScopedSearch.QueryAsync?";
        var billing = DomainVerdict.From(new Dictionary<string, double> { [BuiltInDomains.Billing] = 0.9, [CodePlugin.DomainId] = 0.6 }, 0.5, 0.2);

        Assert.Equal("small talk", Route(question, Need("callers"), Intent.ChitChat).Reason);
        Assert.Equal("the codebase is not the primary domain", Route(question, Need("callers"), domains: billing).Reason);
        Assert.Equal("no code-route answer", Route(question, null).Reason);
    }

    // ---- in the intent request ------------------------------------------------------------------------------------------

    private static DecisionIntentClassifier Classifier(FakeJev jev, IntentOptions o)
    {
        var loggers = LoggerFactory.Create(_ => { });
        var options = Options.Create(o);
        return new DecisionIntentClassifier(new FakeDecisionEngine(jev), options, loggers);
    }

    [Fact]
    public async Task The_code_route_question_rides_in_the_one_intent_request_and_its_route_reaches_the_decision()
    {
        var jev = new FakeJev { InDomain = 0.02, Codebase = _ => 0.93, Choose = _ => "other", CodeNeed = _ => "callers", CodeNeedConfidence = 0.88 };

        var decision = await Classifier(jev, new IntentOptions()).ClassifyAsync("Who calls TenantScopedSearch.QueryAsync?", Ct);

        var request = Assert.Single(jev.Requests).Body;
        Assert.Contains("\"code_need\"", request);
        Assert.Contains("\"user_question\":\"Who calls TenantScopedSearch.QueryAsync?\"", request);
        Assert.Equal("callers", decision.CodeRouting!.Choice);
        Assert.Equal(GraphTools.TraceCodeSymbol, decision.CodeRoute!.Tool);
        Assert.Null(decision.CodeRouteReason);
    }

    [Fact]
    public async Task Switched_off_the_request_carries_no_code_route_question()
    {
        var jev = new FakeJev { InDomain = 0.02, Codebase = _ => 0.93, Choose = _ => "other", CodeNeed = _ => "callers" };

        var decision = await Classifier(jev, new IntentOptions { RouteCodeTools = false }).ClassifyAsync("Who calls TenantScopedSearch.QueryAsync?", Ct);

        Assert.DoesNotContain("code_need", Assert.Single(jev.Requests).Body);
        Assert.Null(decision.CodeRouting);
        Assert.Null(decision.CodeRoute);
    }
    // ---- in the turn ----------------------------------------------------------------------------------------------------

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static ApiFactory CodeApi(FakeToolSource tools, Func<string, string> need, string intent = "other", double billing = 0.02)
    {
        var api = new ApiFactory(ApiFactory.ProceduralModel("It is called from DocumentSearchService.RankCoreAsync."), tools) { InstalledPlugins = [CodePluginSupport.Manifest, .. StandInDomains.Installed] }.WithCode();
        api.Jev.InDomain = billing;
        api.Jev.Codebase = _ => 0.92;
        api.Jev.Choose = _ => intent;
        api.Jev.CodeNeed = need;
        return api;
    }

    private static List<TraceEvent> Trace(IEnumerable<SseEvent> events) =>
        ApiFactory.TracesOf(events).Select(t => t.Deserialize<TraceEvent>(Json)!).ToList();

    [Fact]
    public async Task A_routed_callers_question_starts_with_the_trace_and_never_forces_the_codebase_search()
    {
        var tools = new FakeToolSource { WithCodebase = true, WithCodeGraph = true };
        using var api = CodeApi(tools, _ => "callers");
        const string question = "Who calls TenantScopedSearch.QueryAsync? MARKER-Q";

        var events = await ApiFactory.ChatAsync(api.ClientFor("alice", "firm-a", Role.TENANT_ADMIN), question);

        Assert.Equal(GraphTools.TraceCodeSymbol, tools.Invocations[0]);
        Assert.DoesNotContain(CodeTools.Search, tools.Invocations);
        Assert.Equal("TenantScopedSearch.QueryAsync", tools.GraphArguments[0]["symbol"]);
        Assert.Equal("callers", tools.GraphArguments[0]["direction"]);

        var intent = Trace(events).Single(t => t.Kind == TraceKinds.Intent);
        Assert.Contains("routed trace_code_symbol (callers", intent.Title);
        var codeRouting = intent.Data.GetProperty("codeRouting");
        Assert.Equal("callers", codeRouting.GetProperty("choice").GetString());
        Assert.Equal(GraphTools.TraceCodeSymbol, codeRouting.GetProperty("routedTool").GetString());
        Assert.Equal("TenantScopedSearch.QueryAsync", codeRouting.GetProperty("arguments").GetProperty("symbol").GetString());
        var forced = Trace(events).Single(t => t.Kind == TraceKinds.ToolForced);
        Assert.StartsWith("Routed trace_code_symbol", forced.Title);
        Assert.Contains("Structural code question", forced.Data.GetProperty("reason").GetString());
        // No message content in logs.
        Assert.DoesNotContain(api.Logs.Messages, m => m.Contains("MARKER-Q"));
    }

    [Fact]
    public async Task A_routed_impact_question_calls_change_impact_with_its_file()
    {
        var tools = new FakeToolSource { WithCodebase = true, WithCodeGraph = true };
        using var api = CodeApi(tools, _ => "impact");

        await ApiFactory.ChatAsync(api.ClientFor("alice", "firm-a", Role.TENANT_ADMIN), "Кои тестове покриват src/Maf.Lab.Retrieval/Store/TenantScopedSearch.cs?");

        Assert.Equal(GraphTools.ChangeImpact, tools.Invocations[0]);
        Assert.Equal("src/Maf.Lab.Retrieval/Store/TenantScopedSearch.cs", tools.GraphArguments[0]["path"]);
        Assert.DoesNotContain(CodeTools.Search, tools.Invocations);
    }

    [Fact]
    public async Task A_procedural_question_in_billing_and_the_codebase_issues_the_billing_search_and_the_graph_call_together()
    {
        var tools = new FakeToolSource { WithCodebase = true, WithCodeGraph = true };
        using var api = CodeApi(tools, _ => "callers", intent: "procedural", billing: 0.8);

        var events = await ApiFactory.ChatAsync(api.ClientFor("alice", "firm-a", Role.TENANT_ADMIN), "Who calls TenantScopedSearch.QueryAsync?");

        Assert.Equal(["search_documents", GraphTools.TraceCodeSymbol], tools.Invocations.Take(2).Order(StringComparer.Ordinal));
        Assert.DoesNotContain(CodeTools.Search, tools.Invocations);
        Assert.Equal(2, Trace(events).Count(t => t.Kind == TraceKinds.ToolForced));
    }

    [Fact]
    public async Task A_text_question_still_forces_the_codebase_search_and_keeps_the_reason()
    {
        var tools = new FakeToolSource { WithCodebase = true, WithCodeGraph = true };
        using var api = CodeApi(tools, _ => "text");

        var events = await ApiFactory.ChatAsync(api.ClientFor("alice", "firm-a", Role.TENANT_ADMIN), "How does TenantScopedSearch.QueryAsync work?");

        Assert.Equal(CodeTools.Search, tools.Invocations[0]);
        var codeRouting = Trace(events).Single(t => t.Kind == TraceKinds.Intent).Data.GetProperty("codeRouting");
        Assert.Equal(JsonValueKind.Null, codeRouting.GetProperty("routedTool").ValueKind);
        Assert.Equal("needs text, not the graph", codeRouting.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task A_route_whose_tool_is_not_offered_falls_back_to_the_codebase_search()
    {
        var tools = new FakeToolSource { WithCodebase = true };
        using var api = CodeApi(tools, _ => "callers");

        var events = await ApiFactory.ChatAsync(api.ClientFor("alice", "firm-a", Role.TENANT_ADMIN), "Who calls TenantScopedSearch.QueryAsync?");

        Assert.Equal(CodeTools.Search, tools.Invocations[0]);
        Assert.Equal("trace_code_symbol is not offered",
            Trace(events).Single(t => t.Kind == TraceKinds.Intent).Data.GetProperty("codeRouting").GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Small_talk_routes_and_forces_nothing()
    {
        var tools = new FakeToolSource { WithCodebase = true, WithCodeGraph = true };
        using var api = CodeApi(tools, _ => "callers", intent: "chitchat");

        await ApiFactory.ChatAsync(api.ClientFor("alice", "firm-a", Role.TENANT_ADMIN), "thanks, TenantScopedSearch.QueryAsync is clear now");

        Assert.DoesNotContain(GraphTools.TraceCodeSymbol, tools.Invocations);
        Assert.DoesNotContain(CodeTools.Search, tools.Invocations);
    }
}
