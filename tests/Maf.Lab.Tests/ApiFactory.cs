using System.Net.Http.Headers;
using System.Net.Http.Json;
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
using Microsoft.Extensions.Logging;

namespace Maf.Lab.Tests;

/// <summary>The API host with a scripted model and fake MCP tools; SQLite and eval datasets in a temp folder.</summary>
public sealed class ApiFactory : WebApplicationFactory<Maf.Lab.Api.Program>
{
    /// <summary>The model the intent classifier is pointed at, so its calls never land in <see cref="Chat"/>.</summary>
    public const string IntentModelName = "intent-stub";

    public ApiFactory(ScriptedChatClient chat, FakeToolSource? tools = null, string? dataDir = null, bool emulateForcing = true,
        IChatClient? intent = null)
    {
        EmulateForcing = emulateForcing;
        Chat = chat;
        Intent = intent ?? IntentModel();
        Tools = tools ?? new FakeToolSource();
        DataDir = dataDir ?? Directory.CreateTempSubdirectory("maf-api-").FullName;
    }

    public ScriptedChatClient Chat { get; }
    public IChatClient Intent { get; }
    public FakeToolSource Tools { get; }
    public string DataDir { get; }
    public CapturingLoggerProvider Logs { get; } = new();
    public bool EmulateForcing { get; }
    /// <summary>Extra service overrides for one test (applied after the standard ones).</summary>
    public Action<IServiceCollection>? ConfigureTestServices { get; set; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Storage:ConnectionString"] = $"Data Source={Path.Combine(DataDir, "maf-lab.db")}",
            ["Evals:Root"] = Path.Combine(DataDir, "evals"),
            ["Qdrant:GrpcPort"] = "1",
            ["Agent:EmulateRequiredToolMode"] = EmulateForcing.ToString(),
            ["Agent:IntentModel"] = IntentModelName,
        }));
        builder.ConfigureLogging(l => l.AddProvider(Logs).SetMinimumLevel(LogLevel.Debug));
        builder.ConfigureTestServices(s =>
        {
            s.RemoveAll<IToolSource>();
            s.AddSingleton<IToolSource>(Tools);
            s.RemoveAll<IChatClientFactory>();
            s.AddSingleton<IChatClientFactory>(new FixedChatClientFactory(Chat, IntentModelName, Intent));
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

    public static async Task<List<SseEvent>> ChatAsync(HttpClient client, string message, string? conversationId = null, Action<SseEvent>? onEvent = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat") { Content = JsonContent.Create(new { conversationId, message }) };
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        return await SseReader.ReadAllAsync(await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken), onEvent, TestContext.Current.CancellationToken);
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
            if (IntentClassifier.ForcesRetrieval(IntentClassifier.Classify(last)) && !ScriptedChatClient.HasResult(messages, "search_documents"))
            {
                return ScriptedChatClient.Call("search_documents", new() { ["query"] = last, ["sourceTypes"] = new[] { "procedures" } });
            }
            return ScriptedChatClient.Text(last.StartsWith("thanks", StringComparison.OrdinalIgnoreCase) ? "You're welcome." : answer);
        });

    /// <summary>
    /// Stands in for the classification model: answers a classification request with one label, understanding the
    /// Bulgarian and English words the tests use. Anything else is OTHER, as a real model would answer for an
    /// unclassifiable question.
    /// </summary>
    public static ScriptedChatClient IntentModel() =>
        new((messages, _, _) =>
        {
            var q = (messages.LastOrDefault(m => m.Role == ChatRole.User)?.Text ?? "").ToLowerInvariant();
            var procedural = new[] { "how", "why", "procedure", "what is", "explain", "как", "защо", "процедура", "процедурата", "обясни" }.Any(q.Contains);
            var run = System.Text.RegularExpressions.Regex.IsMatch(q, @"\brun\s*#?\s*\d{3,}|\bрън\s*#?\s*\d{3,}");
            var label = (procedural, run) switch
            {
                (true, true) => "MIXED",
                (true, false) => "PROCEDURAL",
                (false, true) => "DATA",
                _ when new[] { "hi", "hello", "thanks", "здравей", "здрасти", "благодаря", "мерси", "чао" }.Any(q.Contains) => "CHITCHAT",
                _ when new[] { "status", "статус", "списък" }.Any(q.Contains) => "DATA",
                _ => "OTHER",
            };
            return ScriptedChatClient.Text(label);
        });

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
    }
}
