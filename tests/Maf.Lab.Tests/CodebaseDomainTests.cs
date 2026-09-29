using System.Text.Json;
using Maf.Lab.Api.Agent;
using Maf.Lab.Api.Agent.Jev;
using Maf.Lab.Domain.Chat;
using Maf.Lab.Domain.Code;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Domain.Tracing;
using Maf.Lab.Retrieval.Jev;
using Maf.Lab.TestSupport;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Maf.Lab.Tests;

/// <summary>
/// The codebase as the agent's third domain (add-codebase-domain): Jev's question for it, the search it forces, the tools a
/// turn loads by its conversation's domains, and code snippets as the answer's sources.
/// </summary>
public class CodebaseDomainTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static JevIntentClassifier Classifier(FakeJev jev)
    {
        var loggers = LoggerFactory.Create(_ => { });
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [JevCredential.EnvironmentVariable] = FakeJev.TestKey }).Build();
        var credential = new JevCredential(configuration, loggers.CreateLogger<JevCredential>());
        var client = new HttpClient(new JevAuthHandler(credential) { InnerHandler = jev }) { BaseAddress = new Uri("https://jev.test/") };
        var o = Options.Create(new JevOptions());
        return new JevIntentClassifier(new JevClient(new CodeTestClients(client), credential, o), o, loggers);
    }

    // ---- Jev's codebase question -------------------------------------------------------------------------------------

    [Fact]
    public async Task A_code_question_puts_the_codebase_in_scope_in_the_same_request()
    {
        var jev = new FakeJev { InDomain = 0.02, Codebase = _ => 0.93, Choose = _ => "other" };

        var decision = await Classifier(jev).ClassifyAsync("как в кода се прави идемпотентност на тул?", Ct);

        Assert.Equal([Domains.Codebase], decision.Domains!.InScope);
        Assert.False(decision.OutsideDomains);
        var request = Assert.Single(jev.Requests).Body;
        Assert.Contains("\"in_codebase\"", request);
        Assert.Contains("The maf-lab source code", request);
        Assert.Contains("why a run failed", request);
    }

    [Fact]
    public async Task General_programming_is_outside_every_domain()
    {
        var jev = new FakeJev { InDomain = 0.02, Codebase = _ => 0.04, Portfolio = _ => 0.01, Choose = _ => "procedural" };

        var decision = await Classifier(jev).ClassifyAsync("How do I reverse a linked list in Python?", Ct);

        Assert.Empty(decision.Domains!.InScope);
        Assert.True(decision.OutsideDomains);
    }

    [Fact]
    public async Task Below_the_scope_floor_but_above_the_gate_the_codebase_alone_is_in_scope()
    {
        var jev = new FakeJev { InDomain = 0.05, Codebase = _ => 0.35, Portfolio = _ => 0.0, Choose = _ => "procedural" };

        var decision = await Classifier(jev).ClassifyAsync("where does the retry live?", Ct);

        Assert.Equal([Domains.Codebase], decision.Domains!.InScope);
    }

    // ---- what a codebase question forces ------------------------------------------------------------------------------

    private static ToolSet Offered(params string[] names) =>
        new([.. names.Select(n => (AITool)AIFunctionFactory.Create(() => "", n))], null);

    private static IntentDecision Decision(Intent intent, params (string Domain, double P)[] domains) =>
        new(intent, Domains: DomainVerdict.From(domains.ToDictionary(d => d.Domain, d => d.P), 0.5, 0.2));

    [Theory]
    [InlineData(Intent.Data)]
    [InlineData(Intent.Other)]
    [InlineData(Intent.Procedural)]
    public void A_codebase_question_searches_the_codebase_whatever_its_intent(Intent intent)
    {
        var tools = Offered(CodeTools.Search, "search_documents");
        var decision = Decision(intent, (Domains.Codebase, 0.9), (Domains.Billing, 0.1));

        var forced = IntentClassifier.ForcesRetrieval(intent)
            ? ChatTurnRunner.ForcedSearches(decision.Domains, tools)
            : ChatTurnRunner.CodebaseSearch(decision, tools);

        Assert.Equal([CodeTools.Search], forced);
    }

    [Fact]
    public void Small_talk_and_other_domains_force_no_codebase_search()
    {
        var tools = Offered(CodeTools.Search, "search_documents");

        Assert.Empty(ChatTurnRunner.CodebaseSearch(Decision(Intent.ChitChat, (Domains.Codebase, 0.9)), tools));
        Assert.Empty(ChatTurnRunner.CodebaseSearch(Decision(Intent.Data, (Domains.Billing, 0.9), (Domains.Codebase, 0.6)), tools));
    }

    [Fact]
    public void A_procedural_question_in_billing_and_the_codebase_forces_both_searches()
    {
        var tools = Offered(CodeTools.Search, "search_documents");

        var forced = ChatTurnRunner.ForcedSearches(Decision(Intent.Procedural, (Domains.Billing, 0.8), (Domains.Codebase, 0.7)).Domains, tools);

        Assert.Equal(["search_documents", CodeTools.Search], forced);
    }

    // ---- which servers a turn loads ---------------------------------------------------------------------------------------

    [Fact]
    public void Domains_are_loaded_by_scope_then_by_conversation_then_all()
    {
        var inScope = ChatTurnRunner.SelectDomains(DomainVerdict.From(new Dictionary<string, double> { [Domains.Codebase] = 0.9 }, 0.5, 0.2), [Domains.Portfolio]);
        var followUp = ChatTurnRunner.SelectDomains(DomainVerdict.From(new Dictionary<string, double> { [Domains.Billing] = 0.05 }, 0.5, 0.2), [Domains.Portfolio]);
        var fresh = ChatTurnRunner.SelectDomains(DomainVerdict.From(new Dictionary<string, double> { [Domains.Billing] = 0.05 }, 0.5, 0.2), []);
        var noVerdict = ChatTurnRunner.SelectDomains(null, [Domains.Portfolio]);
        var routed = ChatTurnRunner.SelectDomains(DomainVerdict.From(new Dictionary<string, double> { [Domains.Billing] = 0.9 }, 0.5, 0.2), [],
            Maf.Lab.Domain.Portfolio.PortfolioTools.ListAccounts);

        Assert.Equal([Domains.Codebase], inScope.Domains!);
        Assert.Equal(ChatTurnRunner.LoadInScope, inScope.Reason);
        Assert.Equal([Domains.Portfolio], followUp.Domains!);
        Assert.Equal(ChatTurnRunner.LoadConversation, followUp.Reason);
        Assert.Equal((null, ChatTurnRunner.LoadAll), (fresh.Domains, fresh.Reason));
        Assert.Equal((null, ChatTurnRunner.LoadAll), (noVerdict.Domains, noVerdict.Reason));
        // A routed read tool's domain is loaded even when the verdict left it out.
        Assert.Equal([Domains.Billing, Domains.Portfolio], routed.Domains!.Order(StringComparer.Ordinal));
    }

    private static List<TraceEvent> Trace(IEnumerable<SseEvent> events) =>
        ApiFactory.TracesOf(events).Select(t => t.Deserialize<TraceEvent>(Json)!).ToList();

    private static ApiFactory CodeApi(FakeToolSource tools, string answer = "Idempotency rides in ConfirmedCall (src/Maf.Lab.Api/Agent/ToolSource.cs:17-27).")
    {
        var api = new ApiFactory(ApiFactory.ProceduralModel(answer), tools);
        api.Jev.InDomain = 0.02;
        api.Jev.Codebase = q => q.Contains("code", StringComparison.OrdinalIgnoreCase) || q.Contains("кода") ? 0.92 : 0.0;
        api.Jev.Choose = _ => "other";
        return api;
    }

    [Fact]
    public async Task A_code_question_is_answered_from_the_codebase_alone_with_code_sources()
    {
        var tools = new FakeToolSource { WithPortfolio = true, WithCodebase = true };
        using var api = CodeApi(tools);

        var events = await ApiFactory.ChatAsync(api.ClientFor("alice", "firm-a", Role.FIRM_ADMIN), "как в кода се прави идемпотентност на тул?");

        // Only the codebase server's tools were loaded, and its search was forced.
        Assert.Equal([Domains.Codebase], tools.RequestedDomains.Single()!);
        Assert.Equal([CodeTools.Search], tools.Invocations);
        var offered = api.Chat.Requests[^1].Options!.Tools!.Select(t => t.Name).ToList();
        Assert.Equal([CodeTools.Search], offered);
        Assert.Contains("ToolSource.cs:17-27", ApiFactory.AnswerOf(events));

        var domain = Trace(events).Single(t => t.Kind == TraceKinds.Domain);
        Assert.Equal([Domains.Codebase], domain.Data.GetProperty("loaded").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(ChatTurnRunner.LoadInScope, domain.Data.GetProperty("loadReason").GetString());

        // The guard screened the code snippet like any search excerpt: one item, checked.
        var guard = Trace(events).Single(t => t.Kind == TraceKinds.Guardrail && t.Title.StartsWith(CodeTools.Search, StringComparison.Ordinal));
        Assert.Single(guard.Data.GetProperty("items").EnumerateArray());
        Assert.DoesNotContain("unscreened", guard.Title);

        var source = ApiFactory.SourcesOf(events)!.Value.EnumerateArray().Single();
        Assert.Equal("code", source.GetProperty("kind").GetString());
        Assert.Equal(17, source.GetProperty("startLine").GetInt32());
        Assert.Equal("ConfirmedCall", source.GetProperty("symbol").GetString());
        Assert.Equal("src/Maf.Lab.Api/Agent/ToolSource.cs:17-27 › ConfirmedCall", source.GetProperty("sectionPath").GetString());
    }

    [Fact]
    public async Task A_follow_up_in_no_domain_keeps_the_conversations_tools_and_history_keeps_code_sources()
    {
        var tools = new FakeToolSource { WithPortfolio = true, WithCodebase = true };
        using var api = CodeApi(tools);
        var client = api.ClientFor("alice", "firm-a", Role.FIRM_ADMIN);
        var conversationId = ApiFactory.ThreadOf(await ApiFactory.ChatAsync(client, "how does the code confirm a tool call?"));

        var events = await ApiFactory.ChatAsync(client, "покажи още", conversationId);

        Assert.Equal([Domains.Codebase], tools.RequestedDomains[^1]!);
        var domain = Trace(events).Single(t => t.Kind == TraceKinds.Domain);
        Assert.Equal(ChatTurnRunner.LoadConversation, domain.Data.GetProperty("loadReason").GetString());
        Assert.Equal([Domains.Codebase], domain.Data.GetProperty("storedDomains").EnumerateArray().Select(e => e.GetString()));

        var history = await client.GetStringAsync($"/api/conversations/{conversationId}", Ct);
        Assert.Contains("\"startLine\":17", history);
        Assert.Contains("\"kind\":\"code\"", history);
    }

    [Fact]
    public async Task A_billing_question_does_not_load_the_codebase()
    {
        var tools = new FakeToolSource { WithPortfolio = true, WithCodebase = true };
        using var api = new ApiFactory(ApiFactory.ProceduralModel(), tools);

        await ApiFactory.ChatAsync(api.ClientFor("adam", "firm-a", Role.ADVISOR), "What is the procedure when a fee schedule is missing?");

        Assert.Equal([Domains.Billing], tools.RequestedDomains.Single()!);
        Assert.DoesNotContain(CodeTools.Search, api.Chat.Requests[^1].Options!.Tools!.Select(t => t.Name));
    }

    // ---- the domain eval's labels --------------------------------------------------------------------------------------

    [Fact]
    public void Domain_labels_name_every_domain_in_scope_and_keep_both_for_billing_and_portfolio()
    {
        DomainVerdict Verdict(params (string D, double P)[] ps) => DomainVerdict.From(ps.ToDictionary(p => p.D, p => p.P), 0.5, 0.2);

        Assert.Equal("codebase", Maf.Lab.Eval.Suites.DomainSuite.Label(Verdict((Domains.Codebase, 0.9))));
        Assert.Equal("both", Maf.Lab.Eval.Suites.DomainSuite.Label(Verdict((Domains.Portfolio, 0.9), (Domains.Billing, 0.8))));
        Assert.Equal("billing+codebase", Maf.Lab.Eval.Suites.DomainSuite.Label(Verdict((Domains.Codebase, 0.9), (Domains.Billing, 0.8))));
        Assert.True(Maf.Lab.Eval.Datasets.DatasetLoader.IsDomainExpectation("billing+codebase"));
        Assert.False(Maf.Lab.Eval.Datasets.DatasetLoader.IsDomainExpectation("billing+billing"));
        Assert.False(Maf.Lab.Eval.Datasets.DatasetLoader.IsDomainExpectation("weather"));

        var metrics = Maf.Lab.Eval.Suites.Metrics.Domain([("codebase", "codebase", "en", "design"), ("billing+codebase", "codebase", "en", "design")]);
        Assert.Equal(1.0, metrics["codebaseRecall"]);
        Assert.Equal(0.0, metrics["crossingRecall"]);
    }

    // ---- the real tool source ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_tool_source_contacts_only_the_selected_servers_and_keeps_the_allow_list()
    {
        await using var code = new WebApplicationFactory<Maf.Lab.CodeSearch.Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Development");
            b.UseSetting("Qdrant:GrpcPort", "1");
            b.UseSetting(JevCredential.EnvironmentVariable, FakeJev.TestKey);
            b.ConfigureLogging(l => l.SetMinimumLevel(LogLevel.Warning));
        });
        var router = new HostRouter(code.Server.CreateHandler());
        var source = new McpToolSource(Options.Create(new AgentOptions
        {
            // Billing is unreachable: a turn that did not select it must neither contact it nor fail on it.
            McpEndpoint = "http://billing.test/mcp",
            Servers = [new McpServerOptions { Domain = Domains.Codebase, Endpoint = "http://code.test/mcp", Tools = [CodeTools.Search] }],
        }), LoggerFactory.Create(_ => { }), new CodeTestClients(() => new HttpClient(router, disposeHandler: false)));
        var (token, _) = Maf.Lab.Retrieval.Auth.DevJwt.Issue(new Maf.Lab.Domain.Configuration.AuthOptions(), "alice", TenantId.Firm("firm-a"), Role.FIRM_ADMIN, []);

        await using var set = await source.GetToolsAsync(token, null, Ct, new HashSet<string> { Domains.Codebase });

        Assert.Equal([CodeTools.Search], set.Names);
        Assert.Equal(Domains.Codebase, set.DomainOf(CodeTools.Search));
        Assert.DoesNotContain("billing.test", router.Hosts);

        // Every server, as a confirmation asks: billing is contacted, and its failure fails the call as it always did.
        await Assert.ThrowsAnyAsync<Exception>(() => source.GetToolsAsync(token, null, Ct));
        Assert.Contains("billing.test", router.Hosts);
    }

    /// <summary>Sends code.test to the in-process code server and refuses every other host, recording what was asked for.</summary>
    private sealed class HostRouter(HttpMessageHandler code) : DelegatingHandler(code)
    {
        public List<string> Hosts { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            lock (Hosts)
            {
                Hosts.Add(request.RequestUri!.Host);
            }
            return request.RequestUri!.Host == "code.test"
                ? base.SendAsync(request, ct)
                : throw new HttpRequestException("Connection refused");
        }
    }
}

file sealed class CodeTestClients : IHttpClientFactory
{
    private readonly Func<HttpClient> _create;

    public CodeTestClients(HttpClient client) => _create = () => client;

    public CodeTestClients(Func<HttpClient> create) => _create = create;

    public HttpClient CreateClient(string name) => _create();
}
