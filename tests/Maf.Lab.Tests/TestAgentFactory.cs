using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Maf.Lab.A2A;
using Maf.Lab.Domain.Configuration;
using Maf.Lab.Retrieval.Models;
using Maf.Lab.TestAgent;
using Maf.Lab.TestGen;
using Maf.Lab.TestSupport;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Maf.Lab.Tests;

/// <summary>A task store that also remembers which tasks it has seen, so a test can find one that is still running.</summary>
internal sealed class RecordingTaskStore : global::A2A.ITaskStore
{
    private readonly global::A2A.InMemoryTaskStore _inner = new();

    public ConcurrentQueue<string> Seen { get; } = new();

    public Task<global::A2A.AgentTask?> GetTaskAsync(string taskId, CancellationToken cancellationToken = default) =>
        _inner.GetTaskAsync(taskId, cancellationToken);

    public async Task SaveTaskAsync(string taskId, global::A2A.AgentTask task, CancellationToken cancellationToken = default)
    {
        if (!Seen.Contains(taskId))
        {
            Seen.Enqueue(taskId);
        }
        await _inner.SaveTaskAsync(taskId, task, cancellationToken);
    }

    public Task DeleteTaskAsync(string taskId, CancellationToken cancellationToken = default) => _inner.DeleteTaskAsync(taskId, cancellationToken);

    public Task<global::A2A.ListTasksResponse> ListTasksAsync(global::A2A.ListTasksRequest request, CancellationToken cancellationToken = default) =>
        _inner.ListTasksAsync(request, cancellationToken);
}

/// <summary>
/// The test agent on a real socket, over a temp repository, with a scripted model and a fake runner: what a test
/// drives is the handler's loop, the tools and the protocol, not a model or a build.
/// </summary>
internal sealed class TestAgentFactory(TempGitRepo repo, IChatClient model, FakeCoverageRunner runner, Uri? realRunner = null)
    : IAsyncDisposable
{
    private WebApplication? _app;

    public CapturingLoggerProvider Logs { get; } = new();
    public RecordingTaskStore Tasks { get; } = new();

    public async Task<HttpClient> ClientAsync(bool authenticated = true)
    {
        if (_app is null)
        {
            _app = Maf.Lab.TestAgent.Program.BuildApp(ProjectDir.ContentRootArgs("Maf.Lab.TestAgent"), builder =>
            {
                builder.WebHost.UseUrls("http://127.0.0.1:0");
                builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["A2A:Audience"] = "maf-lab-test-agent",
                    ["A2A:PublicBaseUrl"] = "",
                    ["A2A:RequiredScope"] = TestAgentCard.RunScope,
                    ["A2A:StoreKeyspace"] = "testgen",
                    ["A2A:Partners:maf-lab-assistant:Secret"] = "assistant-secret",
                    ["A2A:Partners:maf-lab-assistant:Scopes:0"] = TestAgentCard.RunScope,
                    ["TestAgent:RepoRoot"] = repo.Root,
                    ["TestAgent:WorkRoot"] = Directory.CreateTempSubdirectory("maf-agent-work-").FullName,
                    ["TestAgent:RunnerPollEvery"] = "00:00:00.010",
                });
                builder.Logging.ClearProviders();
                builder.Logging.AddProvider(Logs).SetMinimumLevel(LogLevel.Debug);
                builder.Services.AddSingleton<global::A2A.ITaskStore>(Tasks);
                builder.Services.AddSingleton<IPushConfigStore>(new FakePushConfigStore());
                builder.Services.AddSingleton<IChatClientFactory>(new FixedChatClientFactory(model));
                builder.Services.AddSingleton(realRunner is null
                    ? new CoverageRunnerClient(new HttpClient(runner) { BaseAddress = new Uri("http://runner.test/") },
                        _ => Task.FromResult("t"), TimeSpan.FromMilliseconds(10))
                    : new CoverageRunnerClient(new HttpClient { BaseAddress = realRunner },
                        _ => Task.FromResult(PartnerJwt.Issue(new AuthOptions(), new A2AOptions { Audience = CoverageRunnerClient.Audience },
                            "maf-lab-test-agent", [CoverageRunnerClient.ScopeRun]).Token), TimeSpan.FromMilliseconds(20)));
            });
            await _app.StartAsync();
        }
        var address = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        var client = new HttpClient { BaseAddress = new Uri(address), Timeout = TimeSpan.FromMinutes(2) };
        if (authenticated)
        {
            var response = await client.PostAsJsonAsync("/a2a/token", new A2AEndpoints.TokenRequest("maf-lab-assistant", "assistant-secret"));
            response.EnsureSuccessStatusCode();
            var token = (await response.Content.ReadFromJsonAsync<A2AEndpoints.TokenResponse>())!.AccessToken;
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        return client;
    }

    public static TestGenRequest Request(string commit, int target = 85, int attempts = 5, double maxCost = 5, long maxTokens = 1_000_000) =>
        new(TestGenKinds.Request, "r_1", commit, "src/Lab/Calc.cs", "dotnet", target, attempts, "glm-5.3:cloud",
            new ModelPrice(0.6, 2.2), new TestGenBudget(maxTokens, maxCost));

    public static async Task<JsonElement> RpcAsync(HttpClient client, string method, object @params, CancellationToken ct)
    {
        var response = await client.PostAsJsonAsync("/a2a", new { jsonrpc = "2.0", id = 1, method, @params }, ct);
        var body = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync(ct));
        Assert.False(body.TryGetProperty("error", out var error), error.ToString());
        return body.GetProperty("result");
    }

    public static object Send(TestGenRequest request) => new
    {
        message = new
        {
            kind = "message",
            messageId = Guid.NewGuid().ToString("N"),
            role = "user",
            parts = new object[] { new { kind = "data", data = JsonSerializer.SerializeToElement(request, TestGenKinds.Json) } },
        },
    };

    public static TestGenReport Report(JsonElement task) =>
        task.GetProperty("artifacts").EnumerateArray()
            .Single(a => a.TryGetProperty("name", out var name) && name.GetString() == TestGenKinds.ReportArtifact)
            .GetProperty("parts")[0].GetProperty("data").Deserialize<TestGenReport>(TestGenKinds.Json)!;

    /// <summary>Every activity entry the task holds, in the order the agent numbered them.</summary>
    public static IReadOnlyList<TestGenActivity> Activity(JsonElement task) =>
        task.GetProperty("artifacts").EnumerateArray()
            .Where(a => a.TryGetProperty("name", out var name) && name.GetString() == TestGenKinds.ActivityArtifact)
            .SelectMany(a => a.GetProperty("parts").EnumerateArray())
            .Select(p => p.GetProperty("data").Deserialize<TestGenActivity>(TestGenKinds.Json)!)
            .OrderBy(e => e.Seq)
            .ToList();

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }
}
