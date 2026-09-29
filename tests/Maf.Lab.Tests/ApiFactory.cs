using System.Net.Http.Headers;
using System.Net.Http.Json;
using Maf.Lab.Api.Agent;
using Maf.Lab.Api.Agent.Jev;
using Maf.Lab.Retrieval.Jev;
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
using Microsoft.Extensions.Logging;

namespace Maf.Lab.Tests;

/// <summary>The API host with a scripted model and fake MCP tools; SQLite and eval datasets in a temp folder.</summary>
public sealed class ApiFactory : WebApplicationFactory<Maf.Lab.Api.Program>
{
    public ApiFactory(ScriptedChatClient chat, FakeToolSource? tools = null, string? dataDir = null, bool emulateForcing = true,
        FakeJev? jev = null)
    {
        EmulateForcing = emulateForcing;
        Chat = chat;
        Jev = jev ?? new FakeJev();
        Tools = tools ?? new FakeToolSource();
        DataDir = dataDir ?? Directory.CreateTempSubdirectory("maf-api-").FullName;
    }

    public ScriptedChatClient Chat { get; }
    /// <summary>The intent classifier's endpoint: every classification request lands here, never in <see cref="Chat"/>.</summary>
    public FakeJev Jev { get; }
    public FakeToolSource Tools { get; }
    public string DataDir { get; }
    public CapturingLoggerProvider Logs { get; } = new();
    public bool EmulateForcing { get; }
    /// <summary>How long each stage of a simulated A2A billing run takes; instant unless a test needs to interrupt one.</summary>
    public int SimulatedStepMs { get; init; } = 1;
    /// <summary>Extra configuration for one test, applied over the standard settings.</summary>
    public IReadOnlyDictionary<string, string?> ExtraSettings { get; init; } = new Dictionary<string, string?>();
    /// <summary>Extra service overrides for one test (applied after the standard ones).</summary>
    public Action<IServiceCollection>? ConfigureTestServices { get; set; }
    /// <summary>The shared stores this host runs against, so a test can read what a run left in them.</summary>
    public FakeRunStateStore Runs { get; } = new();
    public FakeIdempotencyStore Idempotency { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Storage:ConnectionString"] = $"Data Source={Path.Combine(DataDir, "maf-lab.db")}",
            ["Evals:Root"] = Path.Combine(DataDir, "evals"),
            ["Qdrant:GrpcPort"] = "1",
            ["Agent:EmulateRequiredToolMode"] = EmulateForcing.ToString(),
            [JevCredential.EnvironmentVariable] = FakeJev.TestKey,
            // No warm-up request: these tests count what each turn sends to Jev (the warm-up has tests of its own).
            ["Jev:WarmUp"] = "false",
            // One partner, so the A2A surface has something to authenticate.
            ["A2A:Partners:acme-portal:Secret"] = "s3cret",
            ["A2A:Partners:acme-portal:Firms:0"] = "firm-a",
            ["A2A:Partners:acme-portal:Scopes:0"] = "a2a.billing.read",
            ["A2A:SimulatedStepMs"] = SimulatedStepMs.ToString(),
        }).AddInMemoryCollection(ExtraSettings));
        builder.ConfigureLogging(l => l.AddProvider(Logs).SetMinimumLevel(LogLevel.Debug));
        builder.ConfigureTestServices(s =>
        {
            s.RemoveAll<IToolSource>();
            s.AddSingleton<IToolSource>(Tools);
            s.RemoveAll<IChatClientFactory>();
            s.AddSingleton<IChatClientFactory>(new FixedChatClientFactory(Chat));
            s.AddHttpClient(JevIntentClassifier.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => Jev);
            // A store, not a particular one: the service requires that there is one, and these tests are not
            // about Redis. The store's own behaviour is proved against a real Redis in the integration tests.
            s.RemoveAll<Maf.Lab.Domain.SharedState.IRunStateStore>();
            s.AddSingleton<Maf.Lab.Domain.SharedState.IRunStateStore>(Runs);
            s.RemoveAll<Maf.Lab.Domain.SharedState.IIdempotencyStore>();
            s.AddSingleton<Maf.Lab.Domain.SharedState.IIdempotencyStore>(Idempotency);
            ConfigureTestServices?.Invoke(s);
        });
    }

    public HttpClient ClientFor(string user, string firm, Role role)
    {
        var (token, _) = DevJwt.Issue(new AuthOptions(), user, TenantId.Firm(firm), role, []);
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>Runs a turn the way a client does: a run of the agent on a thread, carrying the user's message.</summary>
    public static async Task<List<SseEvent>> ChatAsync(HttpClient client, string message, string? conversationId = null,
        Action<SseEvent>? onEvent = null, string? runId = null)
    {
        var input = new
        {
            threadId = conversationId,
            runId = runId ?? $"r_{Guid.NewGuid():N}",
            messages = new[] { new { id = $"u_{Guid.NewGuid():N}", role = "user", content = message } },
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat") { Content = JsonContent.Create(input) };
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        return await SseReader.ReadAllAsync(await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken), onEvent, TestContext.Current.CancellationToken);
    }

    /// <summary>Answers something a previous run stopped for.</summary>
    public static async Task<List<SseEvent>> ResumeAsync(HttpClient client, string conversationId, string interruptId, bool approve,
        Action<SseEvent>? onEvent = null)
    {
        var input = new
        {
            threadId = conversationId,
            runId = $"r_{Guid.NewGuid():N}",
            messages = Array.Empty<object>(),
            resume = new[] { new { interruptId, payload = new { approve } } },
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat") { Content = JsonContent.Create(input) };
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        return await SseReader.ReadAllAsync(await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken), onEvent, TestContext.Current.CancellationToken);
    }

    /// <summary>The whole answer a run streamed.</summary>
    public static string AnswerOf(IEnumerable<SseEvent> events) =>
        string.Concat(events.Where(e => e.Name == "TEXT_MESSAGE_CONTENT").Select(e => e.Data.GetProperty("delta").GetString()));

    /// <summary>The thread a run belongs to, taken from its terminal event.</summary>
    public static string ThreadOf(IEnumerable<SseEvent> events) =>
        events.Last(e => e.Name is "RUN_FINISHED" or "RUN_STARTED").Data.GetProperty("threadId").GetString()!;

    /// <summary>The trace events of a run, which travel as a custom event.</summary>
    public static IEnumerable<System.Text.Json.JsonElement> TracesOf(IEnumerable<SseEvent> events) =>
        events.Where(e => e.Name == "CUSTOM" && e.Data.GetProperty("name").GetString() == "maf-lab/trace")
              .Select(e => e.Data.GetProperty("value"));

    /// <summary>The sources a run reported, if it reported any.</summary>
    public static System.Text.Json.JsonElement? SourcesOf(IEnumerable<SseEvent> events) =>
        events.Where(e => e.Name == "CUSTOM" && e.Data.GetProperty("name").GetString() == "maf-lab/sources")
              .Select(e => (System.Text.Json.JsonElement?)e.Data.GetProperty("value").GetProperty("sources"))
              .LastOrDefault();

    /// <summary>The interrupt a run paused on, if it paused.</summary>
    public static System.Text.Json.JsonElement? InterruptOf(IEnumerable<SseEvent> events)
    {
        var finished = events.LastOrDefault(e => e.Name == "RUN_FINISHED");
        if (finished is null || !finished.Data.TryGetProperty("outcome", out var outcome)) return null;
        if (outcome.GetProperty("type").GetString() != "interrupt") return null;
        return outcome.GetProperty("interrupts")[0];
    }

    /// <summary>Procedural script: first request calls search_documents, the next answers from the tool result.</summary>
    public static ScriptedChatClient ProceduralModel(string answer = "Assign the missing fee schedule and re-run, per Procedure: Missing fee schedule.") =>
        new((messages, options, _) =>
        {
            var last = messages.LastOrDefault(m => m.Role == ChatRole.User)?.Text ?? "";
            if (last.Contains("run 4417", StringComparison.OrdinalIgnoreCase) && !ScriptedChatClient.HasResult(messages, "get_billing_run_status"))
            {
                return ScriptedChatClient.Call("get_billing_run_status", new() { ["runId"] = "4417" });
            }
            if (FakeJev.Classify(last) is "procedural" or "mixed" && !ScriptedChatClient.HasResult(messages, "search_documents"))
            {
                return ScriptedChatClient.Call("search_documents", new() { ["query"] = last, ["sourceTypes"] = new[] { "procedures" } });
            }
            return ScriptedChatClient.Text(last.StartsWith("thanks", StringComparison.OrdinalIgnoreCase) ? "You're welcome." : answer);
        });

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
    }
}
