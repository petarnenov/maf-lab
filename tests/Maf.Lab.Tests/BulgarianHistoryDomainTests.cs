using System.Net.Http.Headers;
using System.Text.Json;
using Maf.Lab.Api.Agent;
using Maf.Lab.Api.Agent.Jev;
using Maf.Lab.Domain.BulgarianHistory;
using Maf.Lab.Domain.Code;
using Maf.Lab.Domain.Tracing;
using Maf.Lab.Domain.Configuration;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Indexing.Corpus;
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
/// The Bulgarian history domain (add-bulgarian-history-domain): its MCP server, its shared-only corpus layout, Jev's question
/// for it, the search it forces and the one server a history question loads.
/// </summary>
public sealed class BulgarianHistoryDomainTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ---- the Bulgarian history MCP server -------------------------------------------------------------------------------

    [Fact]
    public async Task The_server_lists_one_read_only_search_tool_without_tenant_inputs()
    {
        await using var factory = HistoryServer();
        await using var client = await ClientAsync(factory, "firm-a");

        var tools = await client.ListToolsAsync(cancellationToken: Ct);

        var tool = Assert.Single(tools);
        Assert.Equal(BulgarianHistoryTools.Search, tool.Name);
        Assert.True(tool.ProtocolTool.Annotations!.ReadOnlyHint);
        Assert.False(tool.ProtocolTool.Annotations.DestructiveHint);
        var schema = tool.ProtocolTool.InputSchema.GetRawText();
        foreach (var word in new[] { "tenant", "firm", "user", "advisor" })
        {
            Assert.DoesNotContain(word, schema, StringComparison.OrdinalIgnoreCase);
        }
        Assert.NotNull(tool.ProtocolTool.OutputSchema);
        // The tool points across the boundary to the domains that own what it does not.
        foreach (var other in new[] { "get_aum_history", "search_billing_runs", "search_codebase" })
        {
            Assert.Contains(other, tool.Description);
        }
    }

    [Fact]
    public async Task An_empty_query_is_a_tool_error()
    {
        await using var factory = HistoryServer();
        await using var client = await ClientAsync(factory, "firm-a");

        var result = await client.CallToolAsync(BulgarianHistoryTools.Search, new Dictionary<string, object?> { ["query"] = "  " }, cancellationToken: Ct);

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task The_server_rejects_a_caller_without_a_token()
    {
        await using var factory = HistoryServer();
        using var http = factory.CreateClient();

        var response = await http.PostAsync("/mcp", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"), Ct);

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static WebApplicationFactory<Maf.Lab.BulgarianHistory.Program> HistoryServer() =>
        new WebApplicationFactory<Maf.Lab.BulgarianHistory.Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Development");
            // No store is reachable on port 1: the bootstrap keeps retrying in the background and no test here searches.
            b.UseSetting("Qdrant:GrpcPort", "1");
            b.UseSetting(JevCredential.EnvironmentVariable, FakeJev.TestKey);
            b.ConfigureLogging(l => l.SetMinimumLevel(LogLevel.Warning));
        });

    private static async Task<McpClient> ClientAsync(WebApplicationFactory<Maf.Lab.BulgarianHistory.Program> factory, string firm)
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

    // ---- the corpus layout ----------------------------------------------------------------------------------------------

    [Fact]
    public void The_corpus_is_ten_documents_all_shared_and_none_rejected()
    {
        var snapshot = CorpusLoader.Load(Path.Combine(CorpusLoaderTests.RepoRoot(), "data-bulgarian-history"));

        Assert.Equal(10, snapshot.Documents.Count);
        Assert.All(snapshot.Documents, d => Assert.Equal(TenantId.Shared, d.Tenant));
        Assert.Equal(new HashSet<TenantId> { TenantId.Shared }, snapshot.LayoutTenants.ToHashSet());
        Assert.Empty(snapshot.Rejected);
    }

    // ---- Jev's Bulgarian history question --------------------------------------------------------------------------------

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static JevIntentClassifier Classifier(FakeJev jev)
    {
        var loggers = LoggerFactory.Create(_ => { });
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [JevCredential.EnvironmentVariable] = FakeJev.TestKey }).Build();
        var credential = new JevCredential(configuration, loggers.CreateLogger<JevCredential>());
        var client = new HttpClient(new JevAuthHandler(credential) { InnerHandler = jev }) { BaseAddress = new Uri("https://jev.test/") };
        var o = Options.Create(new JevOptions());
        return new JevIntentClassifier(new JevClient(new HistoryTestClients(client), credential, o), o, loggers);
    }

    [Fact]
    public async Task The_history_question_rides_in_the_same_request_and_names_its_exclusions_as_situations()
    {
        var jev = new FakeJev { InDomain = 0.02, BulgarianHistory = _ => 0.9, Choose = _ => "other" };

        await Classifier(jev).ClassifyAsync("Who was Simeon I?", Ct);

        var request = Assert.Single(jev.Requests).Body;
        Assert.Contains("\"in_bulgarian_history\"", request);
        Assert.Contains("\"in_codebase\"", request);
        Assert.Contains("The history of Bulgaria from antiquity to the present day", request);
        foreach (var exclusion in new[] { "from one quarter to the next", "earlier conversations with this assistant", "ran before", "commits and changes" })
        {
            Assert.Contains(exclusion, request);
        }
        // A label containing "history" would pull the question toward the domain it is meant to keep out.
        foreach (var label in new[] { "AUM history", "chat history", "run history", "commit history" })
        {
            Assert.DoesNotContain(label, request, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task A_history_question_puts_Bulgarian_history_alone_in_scope()
    {
        var jev = new FakeJev { InDomain = 0.02, BulgarianHistory = _ => 0.93, Portfolio = _ => 0.0, Codebase = _ => 0.0 };

        var decision = await Classifier(jev).ClassifyAsync("Кога е било Априлското въстание и защо се проваля?", Ct);

        Assert.Equal([Domains.BulgarianHistory], decision.Domains!.InScope);
        Assert.False(decision.Domains.Crossing);
        Assert.False(decision.OutsideDomains);
        Assert.Null(decision.Reason);
    }

    [Fact]
    public async Task Below_the_scope_floor_but_above_the_gate_Bulgarian_history_alone_is_in_scope()
    {
        var jev = new FakeJev { InDomain = 0.05, BulgarianHistory = _ => 0.35, Portfolio = _ => 0.0, Codebase = _ => 0.0, Choose = _ => "procedural" };

        var decision = await Classifier(jev).ClassifyAsync("Какво стана при Освобождението?", Ct);

        Assert.Equal([Domains.BulgarianHistory], decision.Domains!.InScope);
        Assert.False(decision.OutsideDomains);
    }

    [Fact]
    public async Task An_AUM_history_question_is_the_portfolios_alone()
    {
        var jev = new FakeJev { InDomain = 0.1, Portfolio = _ => 0.9, BulgarianHistory = _ => 0.05 };

        var decision = await Classifier(jev).ClassifyAsync("What is the AUM history of A-1042 over the last four quarters?", Ct);

        Assert.Equal([Domains.Portfolio], decision.Domains!.InScope);
        Assert.False(decision.Domains.Crossing);
    }

    [Fact]
    public async Task Jev_down_gives_no_domain_verdict()
    {
        var jev = new FakeJev { Status = System.Net.HttpStatusCode.ServiceUnavailable, BulgarianHistory = _ => 0.93 };

        var decision = await Classifier(jev).ClassifyAsync("Кога е било Априлското въстание?", Ct);

        Assert.Null(decision.Domains);
        Assert.Equal(Intent.Other, decision.Intent);
        Assert.False(decision.OutsideDomains);
    }

    // ---- what a Bulgarian history question forces -------------------------------------------------------------------------

    private static ToolSet Offered(params string[] names) =>
        new([.. names.Select(n => (AITool)AIFunctionFactory.Create(() => "", n))], null);

    private static IntentDecision Decision(Intent intent, params (string Domain, double P)[] domains) =>
        new(intent, Domains: DomainVerdict.From(domains.ToDictionary(d => d.Domain, d => d.P), 0.5, 0.2));

    [Theory]
    [InlineData(Intent.Data)]
    [InlineData(Intent.Other)]
    [InlineData(Intent.Procedural)]
    public void A_history_question_searches_the_history_whatever_its_intent(Intent intent)
    {
        var tools = Offered(BulgarianHistoryTools.Search, "search_documents");
        var decision = Decision(intent, (Domains.BulgarianHistory, 0.9), (Domains.Billing, 0.1));

        var forced = IntentClassifier.ForcesRetrieval(intent)
            ? ChatTurnRunner.ForcedSearches(decision.Domains, tools)
            : ChatTurnRunner.SearchOnlyDomainSearch(decision, tools);

        Assert.Equal([BulgarianHistoryTools.Search], forced);
    }

    [Fact]
    public void Small_talk_and_an_unoffered_search_force_no_history_search()
    {
        Assert.Empty(ChatTurnRunner.SearchOnlyDomainSearch(Decision(Intent.ChitChat, (Domains.BulgarianHistory, 0.9)),
            Offered(BulgarianHistoryTools.Search, "search_documents")));
        Assert.Empty(ChatTurnRunner.SearchOnlyDomainSearch(Decision(Intent.Other, (Domains.BulgarianHistory, 0.9)),
            Offered("search_documents")));
    }

    [Fact]
    public void A_procedural_question_in_the_codebase_and_Bulgarian_history_forces_both_searches()
    {
        var tools = Offered(CodeTools.Search, BulgarianHistoryTools.Search, "search_documents");

        var forced = ChatTurnRunner.ForcedSearches(
            Decision(Intent.Procedural, (Domains.Codebase, 0.8), (Domains.BulgarianHistory, 0.7)).Domains, tools);

        Assert.Equal([CodeTools.Search, BulgarianHistoryTools.Search], forced);
    }

    [Fact]
    public async Task A_data_question_in_Bulgarian_history_alone_routes_no_read_tool_and_forces_the_search()
    {
        // Every read tool scores 0.95: the domain verdict alone keeps them out, since none belongs to the history.
        var decision = await Classifier(new FakeJev
            {
                Choose = _ => "data", InDomain = 0.02, BulgarianHistory = _ => 0.9,
                Tools = (tool, _) => tool == Maf.Lab.Domain.Billing.FeeAdjustmentTool.Name ? 0.02 : 0.95,
            })
            .ClassifyAsync("Кога е било Априлското въстание?", Ct);

        Assert.Equal(Intent.Data, decision.Intent);
        Assert.Equal([Domains.BulgarianHistory], decision.Domains!.InScope);
        Assert.Null(decision.Route);
        Assert.Equal("no read tool belongs to a domain in scope", decision.RouteReason);
        Assert.Equal([BulgarianHistoryTools.Search],
            ChatTurnRunner.SearchOnlyDomainSearch(decision, Offered(BulgarianHistoryTools.Search, "search_documents")));
    }

    private static List<TraceEvent> Trace(IEnumerable<SseEvent> events) =>
        ApiFactory.TracesOf(events).Select(t => t.Deserialize<TraceEvent>(Json)!).ToList();

    [Fact]
    public async Task A_history_question_is_answered_from_the_history_server_alone()
    {
        var tools = new FakeToolSource { WithPortfolio = true, WithCodebase = true, WithBulgarianHistory = true };
        using var api = new ApiFactory(ApiFactory.ProceduralModel("Априлското въстание избухва на 20 април 1876 г. (Априлско въстание)."), tools);
        api.Jev.InDomain = 0.02;
        api.Jev.BulgarianHistory = q => q.Contains("въстание") ? 0.92 : 0.0;
        api.Jev.Choose = _ => "other";

        var events = await ApiFactory.ChatAsync(api.ClientFor("alice", "firm-a", Role.FIRM_ADMIN), "Кога е било Априлското въстание?");

        // Only the history server's tools were loaded, and its search was forced.
        Assert.Equal([Domains.BulgarianHistory], tools.RequestedDomains.Single()!);
        Assert.Contains(BulgarianHistoryTools.Search, tools.Invocations);
        var offered = api.Chat.Requests[^1].Options!.Tools!.Select(t => t.Name).ToList();
        Assert.Equal([BulgarianHistoryTools.Search], offered);

        var domain = Trace(events).Single(t => t.Kind == TraceKinds.Domain);
        Assert.Equal([Domains.BulgarianHistory], domain.Data.GetProperty("loaded").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(ChatTurnRunner.LoadInScope, domain.Data.GetProperty("loadReason").GetString());
        Assert.Contains(BulgarianHistoryTools.Search, domain.Data.GetProperty("forcedSearches").EnumerateArray().Select(e => e.GetString()));
    }
}

file sealed class HistoryTestClients(HttpClient client) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => client;
}
