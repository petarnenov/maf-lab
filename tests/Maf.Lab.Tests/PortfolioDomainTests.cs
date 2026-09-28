using System.Net.Http.Headers;
using System.Text.Json;
using Maf.Lab.Api.Agent;
using Maf.Lab.Api.Agent.Jev;
using Maf.Lab.Domain.Configuration;
using Maf.Lab.Domain.Portfolio;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Domain.Tracing;
using Maf.Lab.Portfolio.Store;
using Maf.Lab.Retrieval.Auth;
using Maf.Lab.Retrieval.Jev;
using Maf.Lab.TestSupport;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;

namespace Maf.Lab.Tests;

/// <summary>
/// The portfolio domain and the boundary between it and billing (add-portfolio-domain): the second server's store and
/// tools, Jev's verdict on which domains a question belongs to, the searches that verdict forces, and the trace that
/// shows where a turn crossed from one domain into the other.
/// </summary>
public class PortfolioDomainTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static readonly Principal FirmA = new("adam", TenantId.Firm("firm-a"), Role.ADVISOR, []);
    private static readonly Principal FirmB = new("bea", TenantId.Firm("firm-b"), Role.ADVISOR, []);

    private static PortfolioStore Store() => new(new ConfigurationBuilder().Build());

    // ---- the portfolio store ------------------------------------------------------------------------------------------

    [Fact]
    public void An_accounts_portfolio_carries_weights_drift_and_whether_it_is_outside_tolerance()
    {
        var portfolio = Store().Portfolio(FirmA, "account a-1042")!;

        Assert.Equal("A-1042", portfolio.AccountId);
        Assert.Equal("ACME-BALANCED-60-40", portfolio.ModelPortfolio);
        Assert.Equal(3_240_000m, portfolio.TotalMarketValue);
        Assert.InRange(portfolio.Holdings.Sum(h => h.ActualWeightPct), 99.5m, 100.5m);
        var us = portfolio.Holdings.Single(h => h.AssetClass == "US equity");
        Assert.Equal(46.9m, us.ActualWeightPct);
        Assert.Equal(6.9m, us.DriftPct);
        // 6.9 points against a 5-point band.
        Assert.True(portfolio.OutsideTolerance);
    }

    [Fact]
    public void Aum_history_is_oldest_first_with_each_quarters_change()
    {
        var history = Store().History(FirmA, "A-1042")!;

        Assert.Equal([2_620_000m, 2_780_000m, 2_910_000m, 3_240_000m], history.Valuations.Select(v => v.Aum));
        Assert.Null(history.Valuations[0].ChangePct);
        Assert.Equal(11.3m, history.Valuations[^1].ChangePct);
    }

    [Fact]
    public void Another_firms_account_is_the_same_answer_as_no_account()
    {
        var store = Store();

        Assert.Null(store.Portfolio(FirmB, "A-1042"));
        Assert.Null(store.History(FirmB, "A-1042"));
        Assert.Null(store.Portfolio(FirmA, "A-9999"));
        Assert.NotNull(store.Portfolio(FirmB, "B-200"));
    }

    [Fact]
    public void A_records_note_never_leaves_the_store()
    {
        var store = Store();
        var json = JsonSerializer.Serialize(store.Portfolio(FirmA, "A-1042"), Json)
            + JsonSerializer.Serialize(store.History(FirmA, "A-1042"), Json);

        Assert.DoesNotContain("note", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CANARY", json, StringComparison.Ordinal);
        Assert.DoesNotContain("instructions", json, StringComparison.OrdinalIgnoreCase);
    }

    // ---- the portfolio MCP server ---------------------------------------------------------------------------------------

    [Fact]
    public async Task The_portfolio_server_lists_three_read_only_tools_without_tenant_inputs()
    {
        await using var factory = PortfolioServer();
        await using var client = await ClientAsync(factory, "firm-a");

        var tools = await client.ListToolsAsync(cancellationToken: Ct);

        Assert.Equal([PortfolioTools.AumHistory, PortfolioTools.GetPortfolio, PortfolioTools.Search], tools.Select(t => t.Name).Order());
        foreach (var tool in tools.Select(t => t.ProtocolTool))
        {
            Assert.True(tool.Annotations!.ReadOnlyHint);
            Assert.False(tool.Annotations.DestructiveHint);
            Assert.DoesNotContain("tenant", tool.InputSchema.GetRawText(), StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("firm", tool.InputSchema.GetRawText(), StringComparison.OrdinalIgnoreCase);
            Assert.NotNull(tool.OutputSchema);
        }
        // Each tool points across the boundary to the domain that owns what it does not.
        Assert.Contains("search_documents", tools.Single(t => t.Name == PortfolioTools.Search).Description);
    }

    [Fact]
    public async Task The_portfolio_server_scopes_its_read_tools_to_the_callers_firm()
    {
        await using var factory = PortfolioServer();
        await using var own = await ClientAsync(factory, "firm-a");
        await using var other = await ClientAsync(factory, "firm-b");

        var mine = await own.CallToolAsync(PortfolioTools.AumHistory, new Dictionary<string, object?> { ["accountId"] = "A-1042" }, cancellationToken: Ct);
        var theirs = await other.CallToolAsync(PortfolioTools.AumHistory, new Dictionary<string, object?> { ["accountId"] = "A-1042" }, cancellationToken: Ct);
        var missing = await own.CallToolAsync(PortfolioTools.AumHistory, new Dictionary<string, object?> { ["accountId"] = "A-9999" }, cancellationToken: Ct);

        Assert.NotEqual(true, mine.IsError);
        Assert.Equal(4, mine.StructuredContent!.Value.GetProperty("valuations").GetArrayLength());
        Assert.True(theirs.IsError);
        // Indistinguishable from an account that does not exist.
        Assert.Equal(
            Text(missing).Replace("A9999", "?"),
            Text(theirs).Replace("A1042", "?"));
    }

    [Fact]
    public async Task The_portfolio_server_rejects_a_caller_without_a_token()
    {
        await using var factory = PortfolioServer();
        using var http = factory.CreateClient();

        var response = await http.PostAsync("/mcp", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"), Ct);

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static string Text(ModelContextProtocol.Protocol.CallToolResult result) =>
        string.Concat(result.Content.OfType<ModelContextProtocol.Protocol.TextContentBlock>().Select(t => t.Text));

    private static WebApplicationFactory<Maf.Lab.Portfolio.Program> PortfolioServer() =>
        new WebApplicationFactory<Maf.Lab.Portfolio.Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Development");
            // No store is reachable on port 1: the bootstrap keeps retrying in the background and no test here searches.
            b.UseSetting("Qdrant:GrpcPort", "1");
            b.UseSetting(JevCredential.EnvironmentVariable, FakeJev.TestKey);
            b.ConfigureLogging(l => l.SetMinimumLevel(LogLevel.Warning));
        });

    private static async Task<McpClient> ClientAsync(WebApplicationFactory<Maf.Lab.Portfolio.Program> factory, string firm)
    {
        var (token, _) = DevJwt.Issue(new AuthOptions(), "u-" + firm, TenantId.Firm(firm), Role.ADVISOR, []);
        var http = factory.CreateDefaultClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(http.BaseAddress!, "/mcp"),
            TransportMode = HttpTransportMode.StreamableHttp,
        }, http, NullLoggerFactory.Instance, ownsHttpClient: true);
        return await McpClient.CreateAsync(transport, cancellationToken: Ct);
    }

    // ---- Jev's domain verdict -------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(0.9, 0.1, new[] { "billing" })]
    [InlineData(0.1, 0.9, new[] { "portfolio" })]
    [InlineData(0.8, 0.9, new[] { "portfolio", "billing" })]
    // Neither reaches scope, but the gate passes: the most probable domain alone, as billing at 0.37 always behaved.
    [InlineData(0.37, 0.1, new[] { "billing" })]
    [InlineData(0.05, 0.02, new string[0])]
    public void Domains_in_scope_are_those_at_the_floor_or_else_the_most_probable_past_the_gate(double billing, double portfolio, string[] expected)
    {
        var verdict = DomainVerdict.From(new Dictionary<string, double> { ["billing"] = billing, ["portfolio"] = portfolio }, 0.5, 0.2);

        Assert.Equal(expected, verdict.InScope);
        Assert.Equal(expected.Length > 1, verdict.Crossing);
        Assert.Equal(Math.Max(billing, portfolio), verdict.Highest);
    }

    private static JevIntentClassifier Classifier(FakeJev jev)
    {
        var loggers = LoggerFactory.Create(_ => { });
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [JevCredential.EnvironmentVariable] = FakeJev.TestKey }).Build();
        var credential = new JevCredential(configuration, loggers.CreateLogger<JevCredential>());
        var client = new HttpClient(new JevAuthHandler(credential) { InnerHandler = jev }) { BaseAddress = new Uri("https://jev.test/") };
        return new JevIntentClassifier(new SingleClientFactory(client), credential, Options.Create(new JevOptions()), loggers);
    }

    [Fact]
    public async Task A_portfolio_procedure_is_in_the_portfolio_domain_alone_and_passes_the_gate()
    {
        var decision = await Classifier(new FakeJev { InDomain = 0.03 }).ClassifyAsync("How much drift triggers a rebalance?", Ct);

        Assert.Equal(Intent.Procedural, decision.Intent);
        Assert.Null(decision.Reason);
        Assert.Equal(["portfolio"], decision.Domains!.InScope);
        Assert.False(decision.Domains.Crossing);
        // Billing's own answer is still kept where earlier traces and statistics read it.
        Assert.Equal(0.03, decision.InDomain);
    }

    [Fact]
    public async Task A_fee_that_moved_with_the_aum_crosses_into_the_portfolio_domain()
    {
        var decision = await Classifier(new FakeJev { InDomain = 0.92 })
            .ClassifyAsync("Why did the fee on A-1042 go up this quarter — did its AUM cross a tier?", Ct);

        Assert.Equal(Intent.Procedural, decision.Intent);
        Assert.True(decision.Domains!.Crossing);
        Assert.Equal(["billing", "portfolio"], decision.Domains.InScope);
    }

    [Fact]
    public async Task A_question_in_neither_domain_forces_nothing_and_says_why()
    {
        var decision = await Classifier(new FakeJev { InDomain = 0.02 }).ClassifyAsync("What is the procedure for renewing a passport?", Ct);

        Assert.Equal(Intent.Other, decision.Intent);
        Assert.Equal("outside the domain (0.02)", decision.Reason);
        Assert.Empty(decision.Domains!.InScope);
    }

    [Fact]
    public async Task A_portfolio_data_question_is_not_routed_to_a_billing_tool()
    {
        var decision = await Classifier(new FakeJev { Choose = _ => "data", InDomain = 0.1, Tools = (_, _) => 0.95 })
            .ClassifyAsync("Show me the holdings of A-1042", Ct);

        Assert.Equal(Intent.Data, decision.Intent);
        Assert.Null(decision.Route);
        Assert.Contains("not in the billing domain", decision.RouteReason);
    }

    // ---- forcing across the boundary -------------------------------------------------------------------------------------

    [Fact]
    public async Task Searches_forced_in_two_domains_go_out_together_once()
    {
        var inner = new ScriptedChatClient((_, _, _) => ScriptedChatClient.Text("answer"));
        var forced = new List<string>();
        var client = new RequiredToolModeChatClient(inner, c => forced.Add(c.Name), null, ["search_documents", PortfolioTools.Search]);
        var options = new ChatOptions { ToolMode = ChatToolMode.RequireSpecific("search_documents") };
        List<ChatMessage> messages = [new(ChatRole.User, "why did the fee go up")];

        var first = await client.GetResponseAsync(messages, options, Ct);

        var calls = first.Messages.SelectMany(m => m.Contents).OfType<FunctionCallContent>().ToList();
        Assert.Equal(["search_documents", PortfolioTools.Search], calls.Select(c => c.Name));
        Assert.All(calls, c => Assert.Equal("why did the fee go up", c.Arguments!["query"]));
        Assert.Equal(["search_documents", PortfolioTools.Search], forced);

        // Once they are in the conversation, the model is asked.
        messages.AddRange(first.Messages);
        var second = await client.GetResponseAsync(messages, options, Ct);
        Assert.Equal("answer", second.Text);
    }

    [Fact]
    public async Task A_mixed_question_about_two_runs_leaves_the_status_calls_to_the_model()
    {
        var stubborn = new ScriptedChatClient((_, _, _) => ScriptedChatClient.Text("answer"));
        using var api = new ApiFactory(stubborn);

        await ApiFactory.ChatAsync(api.ClientFor("adam", "firm-a", Role.ADVISOR), "why did run 4417 and run 4418 fail");

        Assert.Equal(["search_documents"], api.Tools.Invocations);
    }

    // ---- the trace of a crossing -----------------------------------------------------------------------------------------

    private static List<TraceEvent> Trace(IEnumerable<SseEvent> events) =>
        ApiFactory.TracesOf(events).Select(t => t.Deserialize<TraceEvent>(Json)!).ToList();

    [Fact]
    public async Task A_crossing_turn_traces_the_verdict_the_forced_pair_the_boundary_and_the_path()
    {
        var tools = new FakeToolSource { WithPortfolio = true };
        using var api = new ApiFactory(ApiFactory.ProceduralModel("The AUM crossed $3M, per Quarter-End Valuation."), tools);

        var events = await ApiFactory.ChatAsync(api.ClientFor("adam", "firm-a", Role.ADVISOR),
            "Why did the fee on A-1042 go up — did its AUM cross a tier?");
        var trace = Trace(events);

        var domain = trace.Single(t => t.Kind == TraceKinds.Domain);
        Assert.True(domain.Data.GetProperty("crossing").GetBoolean());
        Assert.Contains("crosses billing ↔ portfolio", domain.Title);
        Assert.Equal(["search_documents", PortfolioTools.Search],
            domain.Data.GetProperty("forcedSearches").EnumerateArray().Select(e => e.GetString()));

        var forced = trace.Where(t => t.Kind == TraceKinds.ToolForced).ToList();
        Assert.Equal(["billing", "portfolio"], forced.Select(f => f.Data.GetProperty("domain").GetString()));
        Assert.Equal(["search_documents", PortfolioTools.Search], tools.Invocations);

        var calls = trace.Where(t => t.Kind == TraceKinds.ToolCall).ToList();
        Assert.Equal(["maf-lab-retrieval", "maf-lab-portfolio"], calls.Select(c => c.Data.GetProperty("server").GetString()));
        var boundary = trace.Single(t => t.Kind == TraceKinds.Boundary);
        Assert.Equal("billing", boundary.Data.GetProperty("from").GetString());
        Assert.Equal("portfolio", boundary.Data.GetProperty("to").GetString());
        // The crossing happens as the call is made: after the billing call and before the portfolio one.
        Assert.True(calls[0].Seq < boundary.Seq && boundary.Seq < calls[1].Seq);

        var end = trace.Single(t => t.Kind == TraceKinds.TurnEnd);
        Assert.Equal(["billing", "portfolio"], end.Data.GetProperty("domainPath").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(["billing", "portfolio"], end.Data.GetProperty("domainsPredicted").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(1, end.Data.GetProperty("crossings").GetInt32());
        Assert.Contains("across billing → portfolio", end.Title);
        // Both domains' documentation reaches the answer as sources.
        var sources = ApiFactory.SourcesOf(events)!.Value.GetRawText();
        Assert.Contains("shared/procedures/missing-fee-schedule.txt", sources);
        Assert.Contains("shared/docs/portfolio-quarter-end-valuation.md", sources);
    }

    [Fact]
    public async Task A_billing_turn_crosses_nothing()
    {
        var tools = new FakeToolSource { WithPortfolio = true };
        using var api = new ApiFactory(ApiFactory.ProceduralModel(), tools);

        var trace = Trace(await ApiFactory.ChatAsync(api.ClientFor("adam", "firm-a", Role.ADVISOR), "what is the procedure when a fee schedule is missing"));

        Assert.False(trace.Single(t => t.Kind == TraceKinds.Domain).Data.GetProperty("crossing").GetBoolean());
        Assert.DoesNotContain(trace, t => t.Kind == TraceKinds.Boundary);
        Assert.Equal(["search_documents"], tools.Invocations);
        Assert.Equal(["billing"], trace.Single(t => t.Kind == TraceKinds.TurnEnd).Data.GetProperty("domainPath").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public async Task With_the_portfolio_server_down_a_crossing_question_searches_billing_and_says_portfolio_was_not_offered()
    {
        var tools = new FakeToolSource { WithPortfolio = true, PortfolioUnavailable = true };
        using var api = new ApiFactory(ApiFactory.ProceduralModel(), tools);

        var trace = Trace(await ApiFactory.ChatAsync(api.ClientFor("adam", "firm-a", Role.ADVISOR),
            "Why did the fee on A-1042 go up — did its AUM cross a tier?"));

        var prompt = trace.Single(t => t.Kind == TraceKinds.Prompt);
        Assert.Equal(["billing"], prompt.Data.GetProperty("domains").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(["portfolio"], prompt.Data.GetProperty("unavailableDomains").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(["search_documents"], tools.Invocations);
        Assert.Null(trace.Single(t => t.Kind == TraceKinds.TurnEnd).Data.GetProperty("error").GetString());
    }
}

file sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => client;
}
