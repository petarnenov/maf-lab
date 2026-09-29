using System.Net;
using System.Net.Http.Json;
using Maf.Lab.Api.Code;
using Maf.Lab.Domain.Code;
using Maf.Lab.Domain.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Maf.Lab.Tests;

/// <summary>The api's bridge from the Code snippets tab to the codebase server (add-codebase-search).</summary>
public class CodeSnippetsApiTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class FakeSource(Func<string, CodeSearchResult> answer) : ICodeSnippetSource
    {
        public List<(string Token, string Question, int? Max)> Calls { get; } = [];

        public Task<CodeSearchResult> SearchAsync(string bearerToken, string question, int? maxResults, CancellationToken ct)
        {
            Calls.Add((bearerToken, question, maxResults));
            return Task.FromResult(answer(question));
        }
    }

    private static ApiFactory Api(ICodeSnippetSource source) => new(ApiFactory.ProceduralModel())
    {
        ConfigureTestServices = s =>
        {
            s.RemoveAll<ICodeSnippetSource>();
            s.AddSingleton(source);
        },
    };

    [Fact]
    public async Task Snippets_for_a_question_are_fetched_as_the_user()
    {
        var snippet = new CodeSnippet("src/Maf.Lab.Retrieval/Store/TenantScopedSearch.cs", 121, 136, "TenantFilter.For",
            "src/Maf.Lab.Retrieval/Store/TenantScopedSearch.cs > TenantFilter.For", CodeKinds.Code, "csharp", 0.8, "public static Filter For(...)");
        var source = new FakeSource(_ => new CodeSearchResult([snippet], 1, false, null));
        using var api = Api(source);
        var client = api.ClientFor("adam", "firm-a", Role.ADVISOR);

        var response = await client.PostAsJsonAsync("/api/code/snippets", new { question = "  where is the tenant filter built?  " }, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<CodeSearchResult>(Ct);
        Assert.Equal(snippet, Assert.Single(result!.Results));
        var call = Assert.Single(source.Calls);
        Assert.Equal("where is the tenant filter built?", call.Question);
        Assert.Equal(client.DefaultRequestHeaders.Authorization!.Parameter, call.Token);
        Assert.Equal(8, call.Max);
    }

    [Fact]
    public async Task A_code_server_that_cannot_answer_is_503_without_a_host()
    {
        using var api = Api(new FakeSource(_ => throw new CodeSearchUnavailableException("Code search is unavailable right now.",
            new HttpRequestException("Connection refused (lb:80)"))));
        var client = api.ClientFor("adam", "firm-a", Role.ADVISOR);

        var response = await client.PostAsJsonAsync("/api/code/snippets", new { question = "anything" }, Ct);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(Ct);
        Assert.Contains("Code search is unavailable", body);
        Assert.DoesNotContain("lb:80", body);
    }

    [Fact]
    public async Task No_token_is_401_and_no_question_is_400()
    {
        var source = new FakeSource(_ => new CodeSearchResult([], 0, false, null));
        using var api = Api(source);

        var anonymous = await api.CreateClient().PostAsJsonAsync("/api/code/snippets", new { question = "x" }, Ct);
        var empty = await api.ClientFor("adam", "firm-a", Role.ADVISOR).PostAsJsonAsync("/api/code/snippets", new { question = " " }, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Empty(source.Calls);
    }

    [Fact]
    public async Task An_unreachable_endpoint_becomes_unavailable_not_an_exception()
    {
        var source = new McpCodeSnippetSource(
            Microsoft.Extensions.Options.Options.Create(new CodeSearchClientOptions { Endpoint = "http://127.0.0.1:1/mcp" }),
            new ServiceCollection().AddHttpClient().BuildServiceProvider().GetRequiredService<IHttpClientFactory>(),
            Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);

        var ex = await Assert.ThrowsAsync<CodeSearchUnavailableException>(() => source.SearchAsync("t", "q", 5, Ct));
        Assert.DoesNotContain("127.0.0.1", ex.Message);
    }
}
