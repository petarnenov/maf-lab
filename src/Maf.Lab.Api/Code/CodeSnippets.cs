using System.Text.Json;
using Maf.Lab.Domain.Code;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;

namespace Maf.Lab.Api.Code;

public sealed class CodeSearchClientOptions
{
    public const string Section = "CodeSearch";

    /// <summary>The codebase MCP server (compose: http://lb/code/mcp; dev: http://localhost:5092/mcp). Empty: no code search.</summary>
    public string Endpoint { get; set; } = "http://localhost:5092/mcp";
}

/// <summary>Why the code search could not answer, in words that name no host.</summary>
public sealed class CodeSearchUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Code snippets for a question, fetched as the calling user: the seam api tests replace.</summary>
public interface ICodeSnippetSource
{
    Task<CodeSearchResult> SearchAsync(string bearerToken, string question, int? maxResults, CancellationToken ct);
}

/// <summary>
/// Calls search_codebase on the codebase MCP server with the user's own bearer token, as the agent's tool source calls
/// every other server: the server derives the principal itself and the api never passes a tenant.
/// </summary>
public sealed class McpCodeSnippetSource(IOptions<CodeSearchClientOptions> options, IHttpClientFactory http, ILoggerFactory loggers) : ICodeSnippetSource
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<CodeSearchResult> SearchAsync(string bearerToken, string question, int? maxResults, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(options.Value.Endpoint))
        {
            throw new CodeSearchUnavailableException("Code search is not configured.");
        }
        try
        {
            var transport = new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = new Uri(options.Value.Endpoint),
                TransportMode = HttpTransportMode.StreamableHttp,
                AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = $"Bearer {bearerToken}" },
                Name = "maf-lab-code",
            }, http.CreateClient("mcp"), loggers, ownsHttpClient: true);
            await using var client = await McpClient.CreateAsync(transport, loggerFactory: loggers, cancellationToken: ct);
            var arguments = new Dictionary<string, object?> { ["query"] = question };
            if (maxResults is { } max)
            {
                arguments["maxResults"] = max;
            }
            var result = await client.CallToolAsync(CodeTools.Search, arguments, cancellationToken: ct);
            if (result.IsError == true || result.StructuredContent is not { } structured)
            {
                throw new CodeSearchUnavailableException("Code search could not answer; try again shortly.");
            }
            return structured.Deserialize<CodeSearchResult>(Json) ?? new CodeSearchResult([], 0, false, null);
        }
        catch (Exception ex) when (ex is not CodeSearchUnavailableException and not OperationCanceledException)
        {
            throw new CodeSearchUnavailableException("Code search is unavailable right now.", ex);
        }
    }
}

public static class CodeSnippetsEndpoints
{
    public sealed record CodeSnippetsRequest(string? Question, int? MaxResults);

    public static IEndpointRouteBuilder MapCodeSnippets(this IEndpointRouteBuilder app)
    {
        // The Code snippets tab: the repository's answer to a chat question, fetched as the user who asked it.
        app.MapPost("/api/code/snippets", async (CodeSnippetsRequest request, HttpContext http, ICodeSnippetSource source,
            ILoggerFactory loggers, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Question))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["question"] = ["question is required."] });
            }
            var token = http.Request.Headers.Authorization.ToString()["Bearer ".Length..].Trim();
            try
            {
                var result = await source.SearchAsync(token, request.Question.Trim(), Math.Clamp(request.MaxResults ?? 8, 1, 10), ct);
                return Results.Ok(result);
            }
            catch (CodeSearchUnavailableException ex)
            {
                loggers.CreateLogger("CodeSnippets").LogWarning("code snippets unavailable: {ErrorType}", ex.InnerException?.GetType().Name ?? ex.GetType().Name);
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        }).RequireAuthorization();
        return app;
    }
}
