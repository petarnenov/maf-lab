using Maf.Lab.Api.Agent;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Retrieval.Auth;
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

        await using var tools = await source.GetToolsAsync(token, TestContext.Current.CancellationToken);

        Assert.Equal(["get_billing_run_status", "search_billing_runs", "search_documents"], tools.Names.Order());
        var search = (AIFunction)tools.Tools.Single(t => t.Name == "search_documents");
        var result = await search.InvokeAsync(new AIFunctionArguments { ["query"] = "household rebalancing fee" }, TestContext.Current.CancellationToken);
        var (payload, structured, isError) = ToolDataEnvelope.Unpack(result);
        Assert.False(isError);
        Assert.NotNull(structured);
        Assert.All(structured!.Value.GetProperty("results").EnumerateArray(), r => Assert.Matches("^(firm-c|shared)/", r.GetProperty("docId").GetString()!));
        Assert.DoesNotContain("NW-CANARY-7731-", payload);
    }

    private sealed class ServerHttpClientFactory(WebApplicationFactory<Maf.Lab.Retrieval.Program> server) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => server.CreateDefaultClient();
    }
}
