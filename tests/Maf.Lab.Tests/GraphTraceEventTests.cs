using System.Text.Json;
using System.Text.Json.Nodes;
using Maf.Lab.Api.Agent;
using Maf.Lab.Api.Agent.Tracing;
using Maf.Lab.CodeSearch;
using Maf.Lab.CodeSearch.Tools;
using Maf.Lab.Domain.Graph;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Domain.Tracing;
using Maf.Lab.Retrieval.Auth;
using Maf.Lab.Retrieval.Graph;
using Maf.Lab.Retrieval.Tools;
using Maf.Lab.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Neo4j.Driver;
using NSubstitute;
using Role = Maf.Lab.Domain.Tenancy.Role;

namespace Maf.Lab.Tests;

/// <summary>
/// The turn trace's <c>graph</c> event (add-graph-trace-event): the read path records each read into the call's log,
/// the graph tools attach it to their result only when asked, and the api turns it into one event per call with
/// structure only — never an argument value, a node property or an exception message.
/// </summary>
public class GraphTraceEventTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly Principal FirmA = new("u-a", TenantId.Firm("firm-a"), Role.ADVISOR, []);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // ── the read log ──────────────────────────────────────────────────────────────────────────────────────────────

    private static GraphReadRecord Read(string query = "firm_runs", int rows = 2, string outcome = GraphReadLog.Ok) =>
        new(query, 5, rows, false, 3.2, outcome, outcome == GraphReadLog.Ok ? null : "ServiceUnavailableException");

    [Fact]
    public void Nothing_is_recorded_outside_a_log()
    {
        Assert.Null(GraphReadLog.Current);
        using (var log = GraphReadLog.Begin())
        {
            Assert.Same(log, GraphReadLog.Current);
        }
        Assert.Null(GraphReadLog.Current);
        Assert.Null(GraphReadLog.BeginIf(false));
    }

    [Fact]
    public void A_nested_log_restores_the_outer_one_and_a_closed_log_stops_recording()
    {
        using var outer = GraphReadLog.Begin();
        var inner = GraphReadLog.Begin();
        GraphReadLog.Current!.Add(Read("inner"), ["firm-a", "shared"]);
        inner.Dispose();

        Assert.Same(outer, GraphReadLog.Current);
        inner.Add(Read("late"), ["firm-a"]);
        Assert.Equal(["inner"], inner.Reads.Select(r => r.Query));
        Assert.Empty(outer.Reads);
    }

    [Fact]
    public void A_result_with_no_reads_is_left_untouched()
    {
        var result = ToolErrors.Error("entityId is required");
        Assert.Same(result, GraphReadLog.Attach(result, null));
        using var empty = GraphReadLog.Begin();
        Assert.Null(GraphReadLog.Attach(result, empty).Meta);
    }

    [Fact]
    public void Attached_reads_carry_structure_only()
    {
        using var log = GraphReadLog.Begin();
        log.Add(Read("billing_neighbourhood_2", 7), ["firm-a", "shared"]);
        log.Add(Read("firm_runs", 2), ["firm-a", "shared"]);

        var meta = GraphReadLog.Attach(new CallToolResult(), log).Meta!;

        var graph = meta[GraphReadLog.MetaKey]!.AsObject();
        Assert.Equal(["instance", "tenantScope", "reads"], graph.Select(p => p.Key));
        Assert.Equal(["firm-a", "shared"], graph["tenantScope"]!.AsArray().Select(t => t!.GetValue<string>()));
        var first = graph["reads"]!.AsArray()[0]!.AsObject();
        Assert.Equal(["query", "limit", "rows", "truncated", "durationMs", "outcome", "errorType"], first.Select(p => p.Key));
        Assert.Equal(graph["instance"]!.GetValue<string>(), meta["maf-lab/instance"]!.GetValue<string>());
    }

    [Theory]
    [InlineData(typeof(ServiceUnavailableException), GraphReadLog.Unavailable)]
    [InlineData(typeof(SessionExpiredException), GraphReadLog.Unavailable)]
    [InlineData(typeof(TransientException), GraphReadLog.Unavailable)]
    [InlineData(typeof(OperationCanceledException), GraphReadLog.Cancelled)]
    [InlineData(typeof(InvalidOperationException), GraphReadLog.Error)]
    public void Each_failure_maps_to_its_outcome(Type exception, string outcome)
    {
        var ex = exception == typeof(TransientException)
            ? new TransientException("Neo.TransientError.General.DatabaseUnavailable", "x")
            : (Exception)Activator.CreateInstance(exception, "x")!;
        Assert.Equal(outcome, GraphReadLog.OutcomeOf(ex));
        Assert.Equal(GraphReadLog.Ok, GraphReadLog.OutcomeOf(null));
    }

    [Fact]
    public async Task The_read_path_records_an_unreachable_graph_without_the_message()
    {
        var driver = Substitute.For<IDriver>();
        driver.ExecutableQuery(Arg.Any<string>()).Returns(_ => throw new ServiceUnavailableException("Failed to connect to server 'bolt://neo4j:7687/'"));
        var graph = new TenantScopedGraph(driver, Options.Create(new GraphOptions()));

        using var log = GraphReadLog.Begin();
        await Assert.ThrowsAsync<ServiceUnavailableException>(() => graph.ReadAsync(FirmA, new BillingNeighbourhood("A-1042", 2), Ct));

        var read = Assert.Single(log.Reads);
        Assert.Equal(("billing_neighbourhood_2", BillingNeighbourhood.DefaultLimit, 0, false), (read.Query, read.Limit, read.Rows, read.Truncated));
        Assert.Equal((GraphReadLog.Unavailable, "ServiceUnavailableException"), (read.Outcome, read.ErrorType));
        Assert.Equal(["firm-a", "shared"], log.TenantScope);
        Assert.DoesNotContain("neo4j:7687", log.ToJson("mcp-1").ToJsonString());
        Assert.DoesNotContain("A-1042", log.ToJson("mcp-1").ToJsonString());
    }

    [Fact]
    public async Task The_read_path_records_nothing_when_nobody_asked()
    {
        var driver = Substitute.For<IDriver>();
        driver.ExecutableQuery(Arg.Any<string>()).Returns(_ => throw new ServiceUnavailableException("down"));
        var graph = new TenantScopedGraph(driver, Options.Create(new GraphOptions()));

        await Assert.ThrowsAsync<ServiceUnavailableException>(() => graph.ReadAsync(FirmA, new FirmRuns("A-1042"), Ct));

        Assert.Null(GraphReadLog.Current);
    }

    // ── the tools ─────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Answers like the graph and records like the read path, so the tools' half is seen on its own.</summary>
    private sealed class RecordingGraph(Func<object, object> answer, Exception? failure = null) : IGraphReader
    {
        public Task<TResult> ReadAsync<TResult>(Principal principal, GraphQuery<TResult> query, CancellationToken ct)
        {
            var tenants = principal.ReadableTenants.Select(t => t.Value).ToList();
            if (failure is not null)
            {
                GraphReadLog.Current?.Add(new GraphReadRecord(query.Name, query.Limit, 0, false, 1.5, GraphReadLog.OutcomeOf(failure), failure.GetType().Name), tenants);
                throw failure;
            }
            var result = (TResult)answer(query);
            GraphReadLog.Current?.Add(new GraphReadRecord(query.Name, query.Limit, 3, false, 2.5, GraphReadLog.Ok, null), tenants);
            return Task.FromResult(result);
        }
    }

    private static object BillingAnswer(object q) => q switch
    {
        BillingNeighbourhood => new BillingNeighbourhoodRows(new BillingEntity(BillingEntityKinds.Account, "A-1042", "Ridgeline Family Trust"),
            [new RelatedBillingEntity(BillingEntityKinds.Household, "HH-RIDGELINE", null, 1, null, "IN_HOUSEHOLD")], [], false),
        FirmRuns => (IReadOnlyList<RelatedBillingRun>)[new RelatedBillingRun("4410", "completed", "2026-01-01", "2026-01-31")],
        _ => throw new InvalidOperationException(),
    };

    private static readonly SymbolCandidate Method =
        new("M:Maf.Lab.Retrieval.Store.TenantScopedSearch.QueryAsync", "TenantScopedSearch.QueryAsync", "Maf.Lab.Retrieval.Store.TenantScopedSearch",
            "src/Maf.Lab.Retrieval/Store/TenantScopedSearch.cs", 10, 40);

    private static object CodeAnswer(object q) => q switch
    {
        SymbolCandidates => (IReadOnlyList<SymbolCandidate>)[Method],
        FileMethods => new FileMethodsRows(true, [Method], false),
        CallTrace => new CallTraceRows([new CodeTraceHit("DocumentSearchService.SearchAsync", "src/Maf.Lab.Retrieval/Search/DocumentSearchService.cs", 1, 9, 1, false)], false),
        _ => throw new InvalidOperationException(),
    };

#pragma warning disable MCPEXP002 // A server the tool never calls; the context only carries the request's metadata.
    private static RequestContext<CallToolRequestParams> Traced() =>
        new(new FakeMcpServer(), new JsonRpcRequest { Method = "tools/call" },
            new CallToolRequestParams { Name = "x", Meta = new JsonObject { [SearchDocumentsTool.TraceFlag] = true } });
#pragma warning restore MCPEXP002

    private static BillingGraphTools Billing(IGraphReader graph) => new(graph, new FixedPrincipalAccessor(FirmA), NullLogger<BillingGraphTools>.Instance);

    private static CodeGraphTools Code(IGraphReader graph) => new(graph, new FixedPrincipalAccessor(FirmA), NullLogger<CodeGraphTools>.Instance,
        Options.Create(new CodeSearchOptions()));

    private static List<string> Queries(CallToolResult result) =>
        [.. result.Meta![GraphReadLog.MetaKey]!["reads"]!.AsArray().Select(r => r!["query"]!.GetValue<string>())];

    [Fact]
    public async Task A_traced_billing_lookup_returns_its_reads_beside_an_unchanged_result()
    {
        var plain = await Billing(new RecordingGraph(BillingAnswer)).TraceAsync("A-1042", cancellationToken: Ct);
        var traced = await Billing(new RecordingGraph(BillingAnswer)).TraceAsync("A-1042", cancellationToken: Ct, context: Traced());

        Assert.Null(plain.Meta);
        Assert.Equal(["billing_neighbourhood_2", "firm_runs"], Queries(traced));
        Assert.Equal(plain.StructuredContent!.Value.GetRawText(), traced.StructuredContent!.Value.GetRawText());
        Assert.DoesNotContain("A-1042", traced.Meta![GraphReadLog.MetaKey]!.ToJsonString());
        Assert.Null(GraphReadLog.Current);
    }

    [Fact]
    public async Task An_unavailable_graph_still_returns_the_safe_error_and_says_so_in_its_reads()
    {
        var result = await Billing(new RecordingGraph(BillingAnswer, new ServiceUnavailableException("bolt://neo4j:7687 refused")))
            .TraceAsync("A-1042", cancellationToken: Ct, context: Traced());

        Assert.True(result.IsError);
        Assert.Equal("Graph lookup is temporarily unavailable; try again shortly.", ((TextContentBlock)result.Content.Single()).Text);
        var read = Assert.Single(result.Meta![GraphReadLog.MetaKey]!["reads"]!.AsArray())!;
        Assert.Equal(GraphReadLog.Unavailable, read["outcome"]!.GetValue<string>());
        Assert.DoesNotContain("7687", result.Meta.ToJsonString());
    }

    [Fact]
    public async Task A_call_refused_before_the_graph_has_no_reads()
    {
        var result = await Billing(new RecordingGraph(BillingAnswer)).TraceAsync("  ", cancellationToken: Ct, context: Traced());
        Assert.True(result.IsError);
        Assert.Null(result.Meta);
    }

    [Fact]
    public async Task The_code_graph_tools_return_their_reads_without_symbols_or_paths()
    {
        var tools = Code(new RecordingGraph(CodeAnswer));

        var trace = await tools.TraceAsync("TenantScopedSearch.QueryAsync", cancellationToken: Ct, context: Traced());
        var impact = await tools.ImpactAsync("src/Maf.Lab.Retrieval/Store/TenantScopedSearch.cs", Ct, Traced());

        Assert.Equal(["symbol_candidates", "callers_2"], Queries(trace));
        Assert.Equal(["file_methods", "callers_4"], Queries(impact));
        foreach (var meta in new[] { trace.Meta!.ToJsonString(), impact.Meta!.ToJsonString() })
        {
            Assert.DoesNotContain("TenantScopedSearch", meta);
            Assert.DoesNotContain("src/", meta);
        }
        Assert.Null((await tools.ImpactAsync("src/Maf.Lab.Retrieval/Store/TenantScopedSearch.cs", Ct)).Meta);
    }

    [Theory]
    [InlineData(typeof(BillingGraphTools), nameof(BillingGraphTools.TraceAsync))]
    [InlineData(typeof(CodeGraphTools), nameof(CodeGraphTools.TraceAsync))]
    [InlineData(typeof(CodeGraphTools), nameof(CodeGraphTools.ImpactAsync))]
    public void The_request_context_is_not_a_tool_input(Type type, string method)
    {
        var target = type == typeof(BillingGraphTools) ? (object)Billing(new RecordingGraph(BillingAnswer)) : Code(new RecordingGraph(CodeAnswer));
        var tool = McpServerTool.Create(type.GetMethod(method)!, target);
        Assert.DoesNotContain("\"context\"", tool.ProtocolTool.InputSchema.GetRawText());
    }

    // ── the api's event ───────────────────────────────────────────────────────────────────────────────────────────

    private const string TwoReads = """
        {"instance":"mcp-retrieval-1","tenantScope":["firm-a","shared"],"reads":[
          {"query":"billing_neighbourhood_2","limit":50,"rows":7,"truncated":false,"durationMs":9.4,"outcome":"ok","errorType":null},
          {"query":"firm_runs","limit":5,"rows":2,"truncated":false,"durationMs":3.0,"outcome":"ok","errorType":null}]}
        """;

    [Fact]
    public void Two_reads_become_one_event_with_their_totals()
    {
        var (title, data, duration) = GraphTraceEvent.From("call-1", GraphTools.TraceBilling, JsonNode.Parse(TwoReads))!.Value;

        Assert.Equal("Neo4j billing_neighbourhood_2 + firm_runs · 9 rows", title);
        Assert.Equal(12, duration);
        Assert.Equal(9, data["rows"]!.GetValue<int>());
        Assert.Equal(12.4, data["durationMs"]!.GetValue<double>());
        Assert.Equal("ok", data["outcome"]!.GetValue<string>());
        Assert.Equal("call-1", data["callId"]!.GetValue<string>());
        Assert.Equal(GraphTools.TraceBilling, data["tool"]!.GetValue<string>());
        Assert.Equal("mcp-retrieval-1", data["instance"]!.GetValue<string>());
        Assert.Equal(2, data["reads"]!.AsArray().Count);
    }

    [Theory]
    [InlineData("""{"reads":[{"query":"firm_runs","rows":1,"durationMs":1}]}""", "Neo4j firm_runs · 1 row")]
    [InlineData("""{"reads":[{"query":"callers_4","rows":300,"truncated":true,"durationMs":40,"outcome":"ok"}]}""", "Neo4j callers_4 · 300 rows · truncated")]
    [InlineData("""{"reads":[{"query":"billing_neighbourhood_2","rows":0,"durationMs":2,"outcome":"unavailable","errorType":"ServiceUnavailableException"}]}""", "Neo4j billing_neighbourhood_2 · unavailable")]
    [InlineData("""{"reads":[{"query":"symbol_candidates","rows":4,"outcome":"ok"},{"query":"callers_2","outcome":"error","errorType":"ClientException"}]}""", "Neo4j symbol_candidates + callers_2 · error")]
    public void The_title_says_rows_truncation_or_the_outcome(string payload, string expected) =>
        Assert.Equal(expected, GraphTraceEvent.From("c", "t", JsonNode.Parse(payload))!.Value.Title);

    [Theory]
    [InlineData(null)]
    [InlineData("""{"reads":[]}""")]
    [InlineData("""{"reads":"nope"}""")]
    [InlineData("""{"reads":[{"rows":3}]}""")]
    [InlineData("""[1,2]""")]
    public void A_payload_without_reads_makes_no_event(string? payload) =>
        Assert.Null(GraphTraceEvent.From("c", "t", payload is null ? null : JsonNode.Parse(payload)));

    [Fact]
    public void Only_the_known_fields_reach_the_trace()
    {
        var payload = """{"instance":"mcp-1","secret":"x","reads":[{"query":"firm_runs","rows":1,"id":"A-1042","message":"bolt://neo4j:7687"}]}""";
        var data = GraphTraceEvent.From("c", "t", JsonNode.Parse(payload))!.Value.Data.ToJsonString();

        Assert.DoesNotContain("A-1042", data);
        Assert.DoesNotContain("7687", data);
        Assert.DoesNotContain("secret", data);
    }

    [Fact]
    public async Task A_turn_records_the_graph_event_after_the_tool_result_and_keeps_it_from_the_model()
    {
        var tools = new FakeToolSource { SearchMetaJson = $$"""{"{{TraceMeta.Graph}}":{{TwoReads}},"maf-lab/instance":"mcp-retrieval-1"}""" };
        using var api = new ApiFactory(ApiFactory.ProceduralModel("ANSWER-X per the procedure."), tools);
        var trace = ApiFactory.TracesOf(await ApiFactory.ChatAsync(api.ClientFor("adam", "firm-a", Role.ADVISOR), "what is the procedure when a fee schedule is missing"))
            .Select(t => t.Deserialize<TraceEvent>(Json)!).ToList();
        var kinds = trace.Select(t => t.Kind).ToList();

        var graph = trace.Single(t => t.Kind == TraceKinds.Graph);
        var result = trace.Single(t => t.Kind == TraceKinds.ToolResult);
        Assert.Equal(kinds.IndexOf(TraceKinds.ToolResult) + 1, kinds.IndexOf(TraceKinds.Graph));
        Assert.Equal(result.Data.GetProperty("callId").GetString(), graph.Data.GetProperty("callId").GetString());
        Assert.Equal("Neo4j billing_neighbourhood_2 + firm_runs · 9 rows", graph.Title);
        Assert.Equal(12, graph.DurationMs);
        Assert.Equal("mcp-retrieval-1", result.Data.GetProperty("mcpInstance").GetString());
        Assert.DoesNotContain(TraceMeta.Graph, result.Data.GetRawText());
        Assert.DoesNotContain(TraceMeta.Graph, trace.Single(t => t.Kind == TraceKinds.Envelope).Data.GetRawText());
        Assert.DoesNotContain("billing_neighbourhood_2", trace.Single(t => t.Kind == TraceKinds.Envelope).Data.GetRawText());
    }
}
