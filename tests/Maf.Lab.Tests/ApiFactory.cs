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
    /// <summary>
    /// A circuit breaker that opens on the first transient Jev failure and stays open for the test: what a turn does
    /// when Jev is skipped (add-jev-circuit-breaker).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string?> OpensOnFirstFailure = new Dictionary<string, string?>
    {
        ["Jev:Breaker:FailureThreshold"] = "1",
        ["Jev:Breaker:OpenSeconds"] = "600",
    };

    /// <summary>
    /// The installed plugin set this host starts with (introduce-plugins task 3.4): written as its plugins/.installed.
    /// The stand-in billing domain by default (<see cref="StandInDomains.Installed"/>), the domain the shared fakes speak;
    /// [] is the core alone. The fixture plugins in this test assembly are found by name.
    /// </summary>
    public IReadOnlyList<Maf.Lab.Plugins.Abstractions.PluginManifest> InstalledPlugins { get; init; } = StandInDomains.Installed;
    /// <summary>Plugins installed as having a server part although this assembly holds no code for them.</summary>
    public IReadOnlyCollection<string> ServerWithoutCode { get; init; } = [];

    /// <summary>The environment this host declares (MAF_ENV), which an installed plugin must allow.</summary>
    public string Environment { get; init; } = "dev";

    public string PluginsRoot => Path.Combine(DataDir, "plugins");

    private static readonly IReadOnlySet<string> FixtureNames =
        Maf.Lab.Api.Plugins.PluginHost.Discover([typeof(ApiFactory).Assembly]).Keys.ToHashSet(StringComparer.Ordinal);

    /// <summary>Rewrites the installed set while the host runs, as make does, and makes the catalogue re-read it.</summary>
    public void SetInstalled(IEnumerable<Maf.Lab.Plugins.Abstractions.PluginManifest> plugins)
    {
        WriteInstalled(plugins);
        Services.GetRequiredService<Maf.Lab.Api.Plugins.PluginCatalogue>().Refresh();
    }

    private void WriteInstalled(IEnumerable<Maf.Lab.Plugins.Abstractions.PluginManifest> plugins)
    {
        Directory.CreateDirectory(PluginsRoot);
        var document = new
        {
            schema = 1,
            env = Environment,
            // A plugin "has a server part" exactly when this test assembly holds code for it, as make sees a server/ folder.
            plugins = plugins.Select(m => new { manifest = m, serverJson = (object?)null, hasServer = FixtureNames.Contains(m.Name) || ServerWithoutCode.Contains(m.Name) }),
        };
        File.WriteAllText(Path.Combine(PluginsRoot, ".installed"),
            System.Text.Json.JsonSerializer.Serialize(document, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)));
    }

    /// <summary>Extra configuration for one test, applied over the standard settings.</summary>
    public IReadOnlyDictionary<string, string?> ExtraSettings { get; init; } = new Dictionary<string, string?>();
    /// <summary>Extra service overrides for one test (applied after the standard ones).</summary>
    public Action<IServiceCollection>? ConfigureTestServices { get; set; }
    /// <summary>The shared stores this host runs against, so a test can read what a run left in them.</summary>
    public FakeRunStateStore Runs { get; } = new();
    /// <summary>The live trace of each run, as the monitor reads it while the run is going (agui-protocol-only).</summary>
    public FakeRunTraceStore RunTraces { get; } = new();
    public FakeIdempotencyStore Idempotency { get; } = new();
    /// <summary>
    /// Whether the host's turns are observed by <see cref="TestTraceCapture"/> (the default), so a test reads a turn's
    /// trace with <see cref="TracesOf"/>. Off: the host runs as a core-only deployment, with no observer at all.
    /// </summary>
    public bool ObserveTurns { get; init; } = true;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        WriteInstalled(InstalledPlugins);
        // Read while the api composes its services, before configuration added below exists: host settings are visible then.
        builder.UseSetting("Plugins:Root", PluginsRoot);
        builder.UseSetting("Plugins:ExtraAssemblies:0", typeof(ApiFactory).Assembly.GetName().Name);
        builder.UseSetting("MAF_ENV", Environment);
        builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Storage:ConnectionString"] = $"Data Source={Path.Combine(DataDir, "maf-lab.db")}",
            ["Evals:Root"] = Path.Combine(DataDir, "evals"),
            ["Qdrant:GrpcPort"] = "1",
            ["Agent:EmulateRequiredToolMode"] = EmulateForcing.ToString(),
            [JevCredential.EnvironmentVariable] = FakeJev.TestKey,
            // No warm-up request: these tests count what each turn sends to Jev (the warm-up has tests of its own).
            ["Jev:WarmUp"] = "false",
            // No circuit breaker: these tests script Jev failures turn after turn and count the requests each one sends
            // (the breaker has tests of its own).
            ["Jev:Breaker:FailureThreshold"] = "0",
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
            s.AddSingleton<Maf.Lab.Domain.SharedState.IRunTraceStore>(RunTraces);
            s.RemoveAll<Maf.Lab.Domain.SharedState.IIdempotencyStore>();
            s.AddSingleton<Maf.Lab.Domain.SharedState.IIdempotencyStore>(Idempotency);
            if (ObserveTurns)
            {
                s.AddSingleton<Maf.Lab.Plugins.Abstractions.ITurnObserver, TestTraceCapture>();
            }
            ConfigureTestServices?.Invoke(s);
        });
    }

    public HttpClient ClientFor(string user, string firm, Role role)
    {
        var (token, _) = DevJwt.Issue(new AuthOptions(), user, TenantId.Firm(firm), role);
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>
    /// The core's conversation store as one person's request would reach it (decision 5y): what the list plugin's routes
    /// call, for core tests that need a conversation listed, renamed or deleted without the plugin installed.
    /// </summary>
    public Maf.Lab.Plugins.Abstractions.IConversationStore ConversationsOf(string user, string firm, Role role = Role.USER) =>
        ActivatorUtilities.CreateInstance<Maf.Lab.Api.Storage.ConversationStore>(Services,
            new FixedPrincipalAccessor(new Principal(user, TenantId.Firm(firm), role)));

    /// <summary>Runs a turn the way a client does: a run of the agent on a thread, carrying the user's message.</summary>
    /// <param name="state">The run's AG-UI state as the client sends it (add-focus-state); omitted when null.</param>
    public static async Task<List<SseEvent>> ChatAsync(HttpClient client, string message, string? conversationId = null,
        Action<SseEvent>? onEvent = null, string? runId = null, object? state = null, CancellationToken? cancel = null)
    {
        var ct = cancel is { } stop
            ? CancellationTokenSource.CreateLinkedTokenSource(stop, TestContext.Current.CancellationToken).Token
            : TestContext.Current.CancellationToken;
        var input = new Dictionary<string, object?>
        {
            ["threadId"] = conversationId,
            ["runId"] = runId ?? $"r_{Guid.NewGuid():N}",
            ["messages"] = new[] { new { id = $"u_{Guid.NewGuid():N}", role = "user", content = message } },
        };
        if (state is not null)
        {
            input["state"] = state;
        }
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat") { Content = JsonContent.Create(input) };
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        return await SseReader.ReadAllAsync(await response.Content.ReadAsStreamAsync(ct), onEvent, ct);
    }

    /// <summary>
    /// Comes back to a run the client lost (agui-protocol-only): a run on the same thread that names the lost run as its
    /// parent and says nothing new. The response is returned unread, so a test can see a refusal.
    /// </summary>
    public static Task<HttpResponseMessage> SendRejoinAsync(HttpClient client, string? conversationId, string lostRunId)
    {
        var input = new
        {
            threadId = conversationId,
            runId = $"r_{Guid.NewGuid():N}",
            parentRunId = lostRunId,
            messages = Array.Empty<object>(),
        };
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat") { Content = JsonContent.Create(input) };
        return client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);
    }

    /// <summary>The events of a rejoin, which must be accepted.</summary>
    public static async Task<List<SseEvent>> RejoinAsync(HttpClient client, string conversationId, string lostRunId)
    {
        using var response = await SendRejoinAsync(client, conversationId, lostRunId);
        response.EnsureSuccessStatusCode();
        return await SseReader.ReadAllAsync(await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken), null,
            TestContext.Current.CancellationToken);
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

    /// <summary>
    /// The trace events of a run, as the monitor reads them while it runs (agui-protocol-only): from the live trace store,
    /// by the run's id. They never travel on the stream.
    /// </summary>
    /// <summary>
    /// The turn's core record as kept with it (introduce-plugins 5.3): the subset of its trace the core stores whatever
    /// plugin is installed.
    /// </summary>
    public IReadOnlyList<Maf.Lab.Domain.Tracing.TraceEvent> RecordOf(string turnId)
    {
        using var ctx = Services.GetRequiredService<Microsoft.EntityFrameworkCore.IDbContextFactory<Maf.Lab.Api.Storage.MafDbContext>>().CreateDbContext();
        var json = ctx.Turns.Where(t => t.Id == turnId).Select(t => t.RecordJson).Single();
        return System.Text.Json.JsonSerializer.Deserialize<List<Maf.Lab.Domain.Tracing.TraceEvent>>(json, Maf.Lab.Api.Agent.Tracing.TurnTrace.Json) ?? [];
    }

    public static IEnumerable<System.Text.Json.JsonElement> TracesOf(IEnumerable<SseEvent> events)
    {
        var runId = events.First(e => e.Name == "RUN_STARTED").Data.GetProperty("runId").GetString()!;
        if (!TestTraceCapture.All.TryGetValue(runId, out var trace))
        {
            return [];
        }
        lock (trace)
        {
            return [.. trace.Select(t => System.Text.Json.JsonSerializer.SerializeToElement(t, Maf.Lab.Api.Agent.Tracing.TurnTrace.Json))];
        }
    }

    /// <summary>The sources a run reported, if it reported any: those its searches' tool results carry, deduplicated.</summary>
    public static System.Text.Json.JsonElement? SourcesOf(IEnumerable<SseEvent> events)
    {
        var sources = new System.Text.Json.Nodes.JsonArray();
        var seen = new HashSet<(string?, string?)>();
        foreach (var result in events.Where(e => e.Name == "TOOL_CALL_RESULT"))
        {
            using var content = System.Text.Json.JsonDocument.Parse(result.Data.GetProperty("content").GetString()!);
            if (!content.RootElement.TryGetProperty("sources", out var found))
            {
                continue;
            }
            foreach (var source in found.EnumerateArray())
            {
                var key = (source.GetProperty("docId").GetString(), source.GetProperty("sectionPath").GetString());
                if (seen.Add(key))
                {
                    sources.Add(System.Text.Json.Nodes.JsonNode.Parse(source.GetRawText()));
                }
            }
        }
        return sources.Count == 0 ? null : System.Text.Json.JsonSerializer.SerializeToElement(sources);
    }

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
