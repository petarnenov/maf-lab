using System.Diagnostics;
using Maf.Lab.Api.Agent;
using Maf.Lab.Hosting;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Retrieval.Auth;
using Maf.Lab.Domain.Configuration;
using Maf.Lab.Retrieval.Configuration;
using Maf.Lab.Retrieval.Models;
using Maf.Lab.TestSupport;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Maf.Lab.IntegrationTests;

/// <summary>The agent host's MCP tool source against the real retrieval MCP server (in-memory host, real Qdrant).</summary>
[Collection(CorpusCollection.Name)]
public sealed class AgentMcpIntegrationTests(CorpusIndexFixture corpus)
{
    [Fact]
    public async Task Agent_tool_source_lists_the_three_tools_and_invokes_search_as_the_user()
    {
        var values = corpus.Qdrant.Config(corpus.Collection, corpus.CorpusRoot);
        await using var server = new WebApplicationFactory<Maf.Lab.Retrieval.Program>().WithWebHostBuilder(b =>
        {
            b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(values));
            b.ConfigureTestServices(s =>
            {
                s.RemoveAll<IDenseEncoder>();
                s.AddSingleton<IDenseEncoder>(FakeDenseEncoder.Default());
            });
        });
        var source = new McpToolSource(Options.Create(new AgentOptions { McpEndpoint = new Uri(server.Server.BaseAddress, "/mcp").ToString() }),
            NullLoggerFactory.Instance, new ServerHttpClientFactory(server));
        var (token, _) = DevJwt.Issue(new AuthOptions(), "chris", TenantId.Firm("firm-c"), Role.ADVISOR, []);

        await using var tools = await source.GetToolsAsync(token, null, TestContext.Current.CancellationToken);

        Assert.Equal(
            ["get_billing_run_status", "propose_fee_adjustment", "search_billing_runs", "search_documents"],
            tools.Names.Order());
        var search = (AIFunction)tools.Tools.Single(t => t.Name == "search_documents");
        var result = await search.InvokeAsync(new AIFunctionArguments { ["query"] = "household rebalancing fee" }, TestContext.Current.CancellationToken);
        var (payload, structured, isError) = ToolDataEnvelope.Unpack(result);
        Assert.False(isError);
        Assert.NotNull(structured);
        Assert.All(structured!.Value.GetProperty("results").EnumerateArray(), r => Assert.Matches("^(firm-c|shared)/", r.GetProperty("docId").GetString()!));
        Assert.DoesNotContain("NW-CANARY-7731-", payload);
    }

    [Fact]
    public async Task A_real_search_writes_the_spans_no_library_writes_for_it()
    {
        var spans = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == LabTelemetry.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = spans.Add,
        };
        ActivitySource.AddActivityListener(listener);

        var values = corpus.Qdrant.Config(corpus.Collection, corpus.CorpusRoot);
        await using var server = new WebApplicationFactory<Maf.Lab.Retrieval.Program>().WithWebHostBuilder(b =>
        {
            b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(values));
            b.ConfigureTestServices(s =>
            {
                s.RemoveAll<IDenseEncoder>();
                s.AddSingleton<IDenseEncoder>(FakeDenseEncoder.Default());
            });
        });
        var source = new McpToolSource(Options.Create(new AgentOptions { McpEndpoint = new Uri(server.Server.BaseAddress, "/mcp").ToString() }),
            NullLoggerFactory.Instance, new ServerHttpClientFactory(server));
        var (token, _) = DevJwt.Issue(new AuthOptions(), "chris", TenantId.Firm("firm-c"), Role.ADVISOR, []);
        await using var tools = await source.GetToolsAsync(token, null, TestContext.Current.CancellationToken);
        var search = (AIFunction)tools.Tools.Single(t => t.Name == "search_documents");

        await search.InvokeAsync(new AIFunctionArguments { ["query"] = "household rebalancing fee" }, TestContext.Current.CancellationToken);

        var names = spans.Select(a => a.OperationName).ToList();
        Assert.Contains("mcp.tool", names);
        Assert.Contains("retrieval.embed", names);
        Assert.Contains("retrieval.sparse_encode", names);
        Assert.Contains("retrieval.query", names);

        // The stages happened inside the tool call, and the query itself is in none of them.
        var tool = spans.Single(a => a.OperationName == "mcp.tool");
        Assert.All(spans.Where(a => a.OperationName.StartsWith("retrieval.")), a => Assert.Equal(tool.TraceId, a.TraceId));
        Assert.DoesNotContain("household rebalancing", string.Join(" ",
            spans.SelectMany(a => a.TagObjects).Select(t => $"{t.Key}={t.Value}")));
    }

    [Fact]
    public async Task The_mcp_server_continues_the_trace_the_api_sent_rather_than_starting_its_own()
    {
        // Injection is the HTTP stack's job and a test server has no HTTP stack; what this system decides is the
        // other half — that the server reads the incoming context and hangs its work under it. A trace id nothing
        // in this process knows proves it came off the header and not from an ambient activity.
        var spans = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == LabTelemetry.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = spans.Add,
        };
        ActivitySource.AddActivityListener(listener);

        var traceId = ActivityTraceId.CreateRandom();
        var values = corpus.Qdrant.Config(corpus.Collection, corpus.CorpusRoot);
        await using var server = new WebApplicationFactory<Maf.Lab.Retrieval.Program>().WithWebHostBuilder(b =>
        {
            b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(values));
            b.ConfigureTestServices(s =>
            {
                s.RemoveAll<IDenseEncoder>();
                s.AddSingleton<IDenseEncoder>(FakeDenseEncoder.Default());
            });
        });
        var sending = new SendingHandler($"00-{traceId.ToHexString()}-{ActivitySpanId.CreateRandom().ToHexString()}-01");
        var source = new McpToolSource(Options.Create(new AgentOptions { McpEndpoint = new Uri(server.Server.BaseAddress, "/mcp").ToString() }),
            NullLoggerFactory.Instance, new ServerHttpClientFactory(server, sending));
        var (token, _) = DevJwt.Issue(new AuthOptions(), "chris", TenantId.Firm("firm-c"), Role.ADVISOR, []);

        await using var tools = await source.GetToolsAsync(token, null, TestContext.Current.CancellationToken);
        var search = (AIFunction)tools.Tools.Single(t => t.Name == "search_documents");
        await search.InvokeAsync(new AIFunctionArguments { ["query"] = "household rebalancing fee" }, TestContext.Current.CancellationToken);

        var tool = spans.Single(a => a.OperationName == "mcp.tool");
        Assert.Equal(traceId, tool.TraceId);
        Assert.All(spans.Where(a => a.OperationName.StartsWith("retrieval.")), a => Assert.Equal(traceId, a.TraceId));
    }

    private sealed class ServerHttpClientFactory(WebApplicationFactory<Maf.Lab.Retrieval.Program> server, DelegatingHandler? handler = null)
        : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            handler is null ? server.CreateDefaultClient() : server.CreateDefaultClient(handler);
    }

    /// <summary>Puts a trace on the wire the way a real HTTP stack would, which a test server does not do.</summary>
    private sealed class SendingHandler(string traceParent) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.Headers.Remove("traceparent");
            request.Headers.Add("traceparent", traceParent);
            return base.SendAsync(request, cancellationToken);
        }
    }
}
