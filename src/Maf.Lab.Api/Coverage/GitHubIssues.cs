using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Maf.Lab.TestGen;
using Microsoft.Extensions.Options;

namespace Maf.Lab.Api.Coverage;

public sealed class GitHubOptions
{
    public const string Section = "GitHub";

    /// <summary>The environment variable holding a token limited to this repository's issues. Read by the api only.</summary>
    public string TokenVariable { get; set; } = "GITHUB_ISSUES_TOKEN";

    /// <summary>owner/name. Empty: read from the repository's origin remote.</summary>
    public string Repository { get; set; } = "";

    public string ApiBase { get; set; } = "https://api.github.com";

    /// <summary>Labels every issue the test agent's bugs open.</summary>
    public List<string> Labels { get; set; } = [];
}

public sealed record OpenedIssue(int Number, string Url);

/// <summary>
/// The four GitHub operations this lab uses — open, find, comment, close — on one repository's issues, with a token
/// that can do nothing else. Without a token the lab carries on: <see cref="Configured"/> says so, and nothing is sent.
/// The token is read from the environment, sent only as the bearer header, and never logged.
/// </summary>
public sealed partial class GitHubIssues(IHttpClientFactory http, IOptions<GitHubOptions> options, GitRepository repository,
    ILogger<GitHubIssues> logger)
{
    public const string HttpClientName = "github";
    public static readonly string[] DefaultLabels = ["test-agent", "suspected-bug"];

    private string? _repository;

    public bool Configured => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(options.Value.TokenVariable));

    public async Task<OpenedIssue> CreateAsync(string title, string body, CancellationToken ct)
    {
        var labels = options.Value.Labels.Count > 0 ? options.Value.Labels.ToArray() : DefaultLabels;
        using var response = await SendAsync(HttpMethod.Post, $"repos/{await RepositoryAsync(ct)}/issues", new { title, body, labels }, ct);
        var created = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        return new OpenedIssue(created.GetProperty("number").GetInt32(), created.GetProperty("html_url").GetString()!);
    }

    /// <summary>The test agent's issue whose body carries <paramref name="marker"/>, if one was opened already.</summary>
    public async Task<OpenedIssue?> FindAsync(string marker, CancellationToken ct)
    {
        var labels = string.Join(',', options.Value.Labels.Count > 0 ? options.Value.Labels : DefaultLabels.ToList());
        using var response = await SendAsync(HttpMethod.Get,
            $"repos/{await RepositoryAsync(ct)}/issues?state=all&per_page=100&labels={Uri.EscapeDataString(labels)}", null, ct);
        foreach (var issue in (await response.Content.ReadFromJsonAsync<JsonElement>(ct)).EnumerateArray())
        {
            if (issue.TryGetProperty("body", out var body) && body.GetString()?.Contains(marker, StringComparison.Ordinal) == true)
            {
                return new OpenedIssue(issue.GetProperty("number").GetInt32(), issue.GetProperty("html_url").GetString()!);
            }
        }
        return null;
    }

    public async Task CommentAsync(int number, string body, CancellationToken ct)
    {
        using var _ = await SendAsync(HttpMethod.Post, $"repos/{await RepositoryAsync(ct)}/issues/{number}/comments", new { body }, ct);
    }

    public async Task CloseAsync(int number, CancellationToken ct)
    {
        using var _ = await SendAsync(HttpMethod.Patch, $"repos/{await RepositoryAsync(ct)}/issues/{number}",
            new { state = "closed", state_reason = "not_planned" }, ct);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        var token = Environment.GetEnvironmentVariable(options.Value.TokenVariable);
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("No GitHub issue token is configured.");
        }
        var client = http.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(method, $"{options.Value.ApiBase.TrimEnd('/')}/{path}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.UserAgent.ParseAdd("maf-lab-test-agent");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }
        var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("github {Method} answered {Status}", method.Method, (int)response.StatusCode);
            response.Dispose();
            throw new HttpRequestException($"GitHub answered {(int)response.StatusCode}.", null, response.StatusCode);
        }
        return response;
    }

    private async Task<string> RepositoryAsync(CancellationToken ct)
    {
        if (_repository is not null)
        {
            return _repository;
        }
        if (options.Value.Repository is { Length: > 0 } configured)
        {
            return _repository = configured;
        }
        var origin = (await Git.CheckedAsync(await repository.RootAsync(ct), ["remote", "get-url", "origin"], ct)).Text.Trim();
        return _repository = GitHubRemote().Match(origin) is { Success: true } m
            ? $"{m.Groups[1].Value}/{m.Groups[2].Value}"
            : throw new InvalidOperationException("The origin remote is not a GitHub repository.");
    }

    [GeneratedRegex(@"github\.com[:/]([^/]+)/([^/]+?)(?:\.git)?$")]
    private static partial Regex GitHubRemote();
}
