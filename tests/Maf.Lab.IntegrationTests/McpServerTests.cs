using System.Net.Http.Headers;
using System.Text.Json;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Retrieval.Auth;
using Maf.Lab.Domain.Configuration;
using Maf.Lab.Retrieval.Configuration;
using Maf.Lab.Retrieval.Models;
using Maf.Lab.TestSupport;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Role = Maf.Lab.Domain.Tenancy.Role;

namespace Maf.Lab.IntegrationTests;

[Collection(CorpusCollection.Name)]
public sealed class McpServerTests(CorpusIndexFixture corpus) : IAsyncDisposable
{
    private readonly List<IAsyncDisposable> _owned = [];
    private readonly List<string> _ledgers = [];
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Server_lists_its_tools_annotated_honestly_and_without_tenant_inputs()
    {
        var client = await ClientAsync("adam", "firm-a", Role.ADVISOR);
        var tools = await client.ListToolsAsync(cancellationToken: Ct);

        Assert.Equal(
            ["get_billing_run_status", "propose_fee_adjustment", "search_billing_runs", "search_documents"],
            tools.Select(t => t.Name).Order());

        string[] reading = ["get_billing_run_status", "search_billing_runs", "search_documents"];
        foreach (var tool in tools.Where(t => reading.Contains(t.Name)).Select(t => t.ProtocolTool))
        {
            Assert.True(tool.Annotations!.ReadOnlyHint);
            Assert.True(tool.Annotations.IdempotentHint);
            Assert.False(tool.Annotations.DestructiveHint);
            Assert.False(tool.Annotations.OpenWorldHint);
        }

        // A tool that writes does not get to claim otherwise.
        var write = tools.Single(t => t.Name == "propose_fee_adjustment").ProtocolTool;
        Assert.False(write.Annotations!.ReadOnlyHint);
        Assert.False(write.Annotations.IdempotentHint);
        Assert.True(write.Annotations.DestructiveHint);
        Assert.False(write.Annotations.OpenWorldHint);
        Assert.Contains("only a proposal a person confirmed is applied", write.Description);
        Assert.Contains("search_documents", write.Description);

        foreach (var tool in tools.Select(t => t.ProtocolTool))
        {
            var schema = tool.InputSchema.GetRawText();
            Assert.DoesNotContain("tenant", schema, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("firm", schema, StringComparison.OrdinalIgnoreCase);
            Assert.NotNull(tool.OutputSchema);
        }

        var search = tools.Single(t => t.Name == "search_documents").ProtocolTool;
        Assert.Contains("Use when", search.Description);
        Assert.Contains("Do not use for", search.Description);
        Assert.Contains("get_billing_run_status", search.Description);
        Assert.Contains("search_billing_runs", search.Description);
        var input = search.InputSchema;
        Assert.Equal(["query"], input.GetProperty("required").EnumerateArray().Select(e => e.GetString()));
        Assert.Contains("procedures", input.GetProperty("properties").GetProperty("sourceTypes").GetRawText());
        var output = search.OutputSchema!.Value.GetProperty("properties");
        foreach (var field in new[] { "results", "totalMatches", "truncated", "refineHint" })
        {
            Assert.True(output.TryGetProperty(field, out _), field);
        }
        Assert.Contains("search_documents", tools.Single(t => t.Name == "get_billing_run_status").Description);
        Assert.Contains("search_documents", tools.Single(t => t.Name == "search_billing_runs").Description);
    }

    [Fact]
    public async Task Requests_without_a_token_are_rejected_and_calls_are_sessionless()
    {
        var factory = Factory();
        var anonymous = factory.CreateClient();
        var response = await anonymous.PostAsync("/mcp", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"), Ct);
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);

        var client = await ClientAsync("adam", "firm-a", Role.ADVISOR, factory);
        Assert.Null(client.SessionId);
        var first = await client.CallToolAsync("get_billing_run_status", new Dictionary<string, object?> { ["runId"] = "4417" }, cancellationToken: Ct);
        var second = await client.CallToolAsync("get_billing_run_status", new Dictionary<string, object?> { ["runId"] = "4417" }, cancellationToken: Ct);
        Assert.NotEqual(true, first.IsError);
        Assert.Equal(first.StructuredContent!.Value.GetRawText(), second.StructuredContent!.Value.GetRawText());
    }

    [Fact]
    public async Task Search_documents_caps_results_filters_and_ignores_a_tenant_argument()
    {
        var client = await ClientAsync("adam", "firm-a", Role.ADVISOR);

        var capped = await client.CallToolAsync("search_documents",
            new Dictionary<string, object?> { ["query"] = "fee schedule procedure", ["maxResults"] = 50 }, cancellationToken: Ct);
        var result = capped.StructuredContent!.Value;
        Assert.InRange(result.GetProperty("results").GetArrayLength(), 1, 10);
        Assert.True(result.GetProperty("truncated").GetBoolean());

        var code = await client.CallToolAsync("search_documents",
            new Dictionary<string, object?> { ["query"] = "tiered fee calculation", ["sourceTypes"] = new[] { "code" } }, cancellationToken: Ct);
        Assert.All(code.StructuredContent!.Value.GetProperty("results").EnumerateArray(),
            r => Assert.Contains("/code/", "/" + r.GetProperty("docId").GetString()!.Split('/', 2)[1]));

        var sneaky = await client.CallToolAsync("search_documents",
            new Dictionary<string, object?> { ["query"] = "Northwind household rebalancing fee schedule", ["firmId"] = "firm-b", ["tenant_id"] = "firm-b" }, cancellationToken: Ct);
        Assert.NotEqual(true, sneaky.IsError);
        Assert.All(sneaky.StructuredContent!.Value.GetProperty("results").EnumerateArray(),
            r => Assert.Matches("^(firm-a|shared)/", r.GetProperty("docId").GetString()!));

        var id = await client.CallToolAsync("search_documents", new Dictionary<string, object?> { ["query"] = "4417" }, cancellationToken: Ct);
        Assert.Equal(0, id.StructuredContent!.Value.GetProperty("results").GetArrayLength());
        Assert.Contains("get_billing_run_status", id.StructuredContent!.Value.GetProperty("refineHint").GetString());

        var snippet = capped.StructuredContent!.Value.GetProperty("results")[0];
        foreach (var field in new[] { "snippet", "sourcePath", "sectionPath", "score", "updatedAt", "docId" })
        {
            Assert.True(snippet.TryGetProperty(field, out _), field);
        }
        Assert.False(snippet.TryGetProperty("answer", out _));
    }

    [Fact]
    public async Task Billing_tools_are_firm_scoped_and_never_return_the_note()
    {
        var a = await ClientAsync("adam", "firm-a", Role.ADVISOR);
        var b = await ClientAsync("bianca", "firm-b", Role.ADVISOR);

        var own = await a.CallToolAsync("get_billing_run_status", new Dictionary<string, object?> { ["runId"] = "4417" }, cancellationToken: Ct);
        Assert.NotEqual(true, own.IsError);
        var status = own.StructuredContent!.Value;
        Assert.Equal("failed", status.GetProperty("status").GetString());
        Assert.StartsWith("FS-REQUIRED", status.GetProperty("failureReason").GetString());

        var foreign = await b.CallToolAsync("get_billing_run_status", new Dictionary<string, object?> { ["runId"] = "4417" }, cancellationToken: Ct);
        Assert.True(foreign.IsError);
        Assert.Contains("not found", Text(foreign));

        var list = await a.CallToolAsync("search_billing_runs", new Dictionary<string, object?>(), cancellationToken: Ct);
        var raw = list.StructuredContent!.Value.GetRawText() + Text(list);
        Assert.DoesNotContain("note", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("external@evil.example", raw);
        foreach (var run in list.StructuredContent!.Value.GetProperty("runs").EnumerateArray())
        {
            var detail = await a.CallToolAsync("get_billing_run_status", new Dictionary<string, object?> { ["runId"] = run.GetProperty("runId").GetString() }, cancellationToken: Ct);
            Assert.DoesNotContain("external@evil.example", Text(detail));
        }
    }

    [Fact]
    public async Task Vector_store_outage_returns_a_short_error_without_internals()
    {
        var factory = Factory(o => o["Qdrant:GrpcPort"] = "1");
        var client = await ClientAsync("adam", "firm-a", Role.ADVISOR, factory);

        var result = await client.CallToolAsync("search_documents", new Dictionary<string, object?> { ["query"] = "fee schedule procedure" }, cancellationToken: Ct);

        Assert.True(result.IsError);
        var text = Text(result);
        Assert.Equal("Document search is temporarily unavailable; try again shortly.", text);
        foreach (var forbidden in new[] { "localhost", "127.0.0.1", "Grpc", "Exception", " at ", ":1", "Qdrant", "select" })
        {
            Assert.DoesNotContain(forbidden, text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task A_proposal_over_the_wire_asks_for_input_and_writes_nothing()
    {
        var factory = Factory();
        var client = await ClientAsync("adam", "firm-a", Role.ADVISOR, factory);

        var asked = await Assert.ThrowsAnyAsync<Exception>(async () => await client.CallToolAsync(
            "propose_fee_adjustment",
            new Dictionary<string, object?> { ["accountId"] = "A-1042", ["amount"] = -200m, ["reason"] = "moved to the flat schedule" },
            cancellationToken: Ct));

        // Whatever the client surfaces, it must not be a silent success and nothing may have been written.
        // The SDK client resolves input requests itself, so without a handler it says so rather than writing.
        Assert.Contains("ElicitationHandler", asked.Message);
        Assert.DoesNotContain("applied", asked.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static string Text(CallToolResult result) => string.Join("\n", result.Content.OfType<TextContentBlock>().Select(t => t.Text));

    private WebApplicationFactory<Maf.Lab.Retrieval.Program> Factory(Action<Dictionary<string, string?>>? configure = null)
    {
        var values = corpus.Qdrant.Config(corpus.Collection, corpus.CorpusRoot);
        values["Billing:SeedPath"] = Path.Combine(CorpusIndexFixture.RepoRoot(), "compose", "seed", "billing-runs.json");
        values["Billing:AccountsSeedPath"] = Path.Combine(CorpusIndexFixture.RepoRoot(), "compose", "seed", "billing-accounts.json");
        // Its own file per factory, so one test's adjustments are not another's.
        var ledger = Path.Combine(Path.GetTempPath(), $"maf-lab-mcp-{Guid.NewGuid():N}.db");
        values["Billing:AdjustmentsConnectionString"] = $"Data Source={ledger}";
        values["Auth:SigningKey"] = new AuthOptions().SigningKey;
        _ledgers.Add(ledger);
        configure?.Invoke(values);
        var factory = new WebApplicationFactory<Maf.Lab.Retrieval.Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Development");
            b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(values));
            b.ConfigureTestServices(s =>
            {
                s.RemoveAll<IDenseEncoder>();
                s.AddSingleton<IDenseEncoder>(FakeDenseEncoder.Default());
            });
        });
        _owned.Add(factory);
        return factory;
    }

    private async Task<McpClient> ClientAsync(string user, string firm, Role role, WebApplicationFactory<Maf.Lab.Retrieval.Program>? factory = null)
    {
        factory ??= Factory();
        var (token, _) = DevJwt.Issue(new AuthOptions(), user, TenantId.Firm(firm), role, []);
        var http = factory.CreateDefaultClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(http.BaseAddress!, "/mcp"),
            TransportMode = HttpTransportMode.StreamableHttp,
        }, http, NullLoggerFactory.Instance, ownsHttpClient: true);
        var client = await McpClient.CreateAsync(transport, cancellationToken: Ct);
        _owned.Add(client);
        return client;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var d in Enumerable.Reverse(_owned))
        {
            await d.DisposeAsync();
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var file in _ledgers.SelectMany(l => new[] { l, l + "-wal", l + "-shm" }).Where(File.Exists))
        {
            File.Delete(file);
        }
    }
}

[Collection(CorpusCollection.Name)]
public sealed class McpDiagnosticsTests(CorpusIndexFixture corpus)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Diagnostics_only_on_request_tenant_scoped_and_structured_content_unchanged()
    {
        var values = corpus.Qdrant.Config(corpus.Collection, corpus.CorpusRoot);
        await using var factory = new WebApplicationFactory<Maf.Lab.Retrieval.Program>().WithWebHostBuilder(b =>
        {
            b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(values));
            b.ConfigureTestServices(s =>
            {
                s.RemoveAll<IDenseEncoder>();
                s.AddSingleton<IDenseEncoder>(FakeDenseEncoder.Default());
            });
        });
        var (token, _) = DevJwt.Issue(new AuthOptions(), "chris", TenantId.Firm("firm-c"), Role.ADVISOR, []);
        var http = factory.CreateDefaultClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        await using var client = await McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(http.BaseAddress!, "/mcp"), TransportMode = HttpTransportMode.StreamableHttp,
        }, http, NullLoggerFactory.Instance, ownsHttpClient: true), cancellationToken: Ct);

        var tools = await client.ListToolsAsync(cancellationToken: Ct);
        Assert.All(tools, t => Assert.DoesNotContain("\"context\"", t.ProtocolTool.InputSchema.GetRawText()));

        var args = new Dictionary<string, object?> { ["query"] = "household rebalancing fee schedule" };
        var plain = await client.CallToolAsync("search_documents", args, cancellationToken: Ct);
        Assert.True(plain.Meta is null || !plain.Meta.ContainsKey("maf-lab/trace"));

        var search = tools.Single(t => t.Name == "search_documents").WithMeta(new System.Text.Json.Nodes.JsonObject { ["maf-lab/trace"] = true });
        var traced = (JsonElement)(await search.InvokeAsync(new Microsoft.Extensions.AI.AIFunctionArguments(args), Ct))!;
        Assert.Equal(plain.StructuredContent!.Value.GetRawText(), traced.GetProperty("structuredContent").GetRawText());

        var diag = traced.GetProperty("_meta").GetProperty("maf-lab/trace");
        Assert.Equal(["firm-c", "shared"], diag.GetProperty("tenantScope").EnumerateArray().Select(e => e.GetString()));
        foreach (var list in new[] { "dense", "sparse", "fused" })
        {
            Assert.NotEqual(0, diag.GetProperty(list).GetArrayLength());
            Assert.All(diag.GetProperty(list).EnumerateArray(), c => Assert.Contains(c.GetProperty("tenantId").GetString(), new[] { "firm-c", "shared" }));
        }
        Assert.Contains(diag.GetProperty("query").GetProperty("terms").EnumerateArray(), t => t.GetProperty("term").GetString() == "household");
        var fusedDocs = diag.GetProperty("fused").EnumerateArray().Select(c => c.GetProperty("docId").GetString()).Take(5).ToList();
        var resultDocs = traced.GetProperty("structuredContent").GetProperty("results").EnumerateArray().Select(r => r.GetProperty("docId").GetString()).ToList();
        Assert.Equal(resultDocs, fusedDocs.Take(resultDocs.Count));
        Assert.True(diag.GetProperty("timings").GetProperty("qdrantMs").GetInt64() >= 0);
        Assert.False(string.IsNullOrEmpty(traced.GetProperty("_meta").GetProperty("maf-lab/instance").GetString()));
    }
}
