using Maf.Lab.A2A;
using A2A;
using Maf.Lab.Plugins.A2A;
// Both libraries have a Role; the message role is the one this file means.
using MessageRole = A2A.Role;
using Maf.Lab.Api.Agent;
using Maf.Lab.Api.Compliance;
using Maf.Lab.Domain.Tenancy;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using Maf.Lab.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Maf.Lab.Tests;

/// <summary>
/// What a partner actually gets: a message for a question, a task for work that takes time, a question back when
/// something is missing, a rejection with no data when it asks about a firm it may not see.
/// </summary>
public class A2AHandlerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Seed = """
        [{"firmId":"firm-a","runId":"4417","status":"failed","periodStart":"2026-06-01","periodEnd":"2026-06-30",
          "accountCount":1240,"failureReason":"FS-REQUIRED: fee schedule missing","updatedAt":"2026-07-01T00:00:00Z"},
         {"firmId":"firm-b","runId":"5001","status":"completed","periodStart":"2026-06-01","periodEnd":"2026-06-30",
          "accountCount":88,"failureReason":null,"updatedAt":"2026-07-01T00:00:00Z"}]
        """;

    private static (AssistantAgentHandler Handler, ApiFactory Api) Build(params string[] firms) =>
        Build(new A2AOptions { SimulatedStepMs = 1 }, null, firms);

    private static (AssistantAgentHandler Handler, ApiFactory Api) Build(A2AOptions a2a, string? dataDir, params string[] firms)
        => Build(a2a, dataDir, TimeProvider.System, firms);

    private static (AssistantAgentHandler Handler, ApiFactory Api) Build(A2AOptions a2a, string? dataDir, TimeProvider time, params string[] firms)
        => Build(a2a, dataDir, time, null, firms);

    private static (AssistantAgentHandler Handler, ApiFactory Api) Build(A2AOptions a2a, string? dataDir, TimeProvider time,
        Maf.Lab.Plugins.Abstractions.IAssistantAnswer? assistant, params string[] firms)
    {
        var api = new ApiFactory(ApiFactory.ProceduralModel(), dataDir: dataDir) { InstalledPlugins = A2APluginSupport.Installed };
        foreach (var firm in firms) api.ClientFor("fixture", firm, Maf.Lab.Domain.Tenancy.Role.USER).Dispose();
        var partner = new PartnerPrincipal("acme-portal", firms.Select(TenantId.Firm).ToHashSet(),
            new HashSet<string> { A2AScopes.BillingRead });
        var handler = new AssistantAgentHandler(
            new FixedPartner(partner),
            new DomainToolCall(new SeededBillingServer(Seed),
                api.Services.GetRequiredService<IOptions<Maf.Lab.Domain.Configuration.AuthOptions>>(), NullLogger<DomainToolCall>.Instance,
                api.Services.GetRequiredService<DomainCatalogue>(),
                api.Services.GetRequiredService<Maf.Lab.Plugins.Abstractions.IPluginAccess>()),
            Options.Create(a2a),
            api.Services.GetRequiredService<Maf.Lab.Plugins.Abstractions.IActivityAudit>(),
            assistant ?? api.Services.GetRequiredService<Maf.Lab.Plugins.Abstractions.IAssistantAnswer>(),
            api.Services.GetRequiredService<Maf.Lab.Plugins.Abstractions.IPluginAccess>(),
            api.Services.GetRequiredService<Maf.Lab.Plugins.Abstractions.IInstalledPlugins>(),
            api.Services.GetRequiredService<IA2ATaskOwner>(),
            api.Services.GetRequiredService<ITaskStore>(),
            api.Services.GetRequiredService<Microsoft.Extensions.Hosting.IHostApplicationLifetime>(),
            time,
            NullLogger<AssistantAgentHandler>.Instance);
        return (handler, api);
    }

    private static async Task<List<StreamResponse>> DrainAsync(
        Func<AgentEventQueue, System.Threading.Tasks.Task> run, CancellationToken ct, Action<StreamResponse>? onEvent = null)
    {
        var queue = new AgentEventQueue();
        var collected = new List<StreamResponse>();
        var reader = System.Threading.Tasks.Task.Run(async () =>
        {
            await foreach (var item in queue.WithCancellation(ct))
            {
                collected.Add(item);
                onEvent?.Invoke(item);
            }
        }, ct);
        try
        {
            await run(queue);
        }
        finally
        {
            queue.Complete();
            await reader;
        }
        return collected;
    }

    private static RequestContext Context(string text, string taskId = "t-1", AgentTask? existing = null) => new()
    {
        TaskId = taskId,
        ContextId = "ctx-1",
        Message = new Message { MessageId = "m-1", Role = MessageRole.User, Parts = [new Part { Text = text }] },
        Task = existing,
        StreamingResponse = true,
    };

    private static IEnumerable<TaskState> States(IEnumerable<StreamResponse> events) =>
        events.Select(e => e.StatusUpdate?.Status?.State).Where(s => s is not null).Select(s => s!.Value);

    [Fact]
    public async Task A_question_about_an_entitled_run_is_answered_with_a_message()
    {
        var (handler, api) = Build("firm-a");
        using var _ = api;

        var events = await DrainAsync(q => handler.ExecuteAsync(Context("status of run 4417"), q, Ct), Ct);

        var message = Assert.Single(events, e => e.Message is not null).Message!;
        var text = string.Join("", message.Parts!.Select(p => p.Text));
        Assert.Contains("4417", text);
        Assert.Contains("failed", text, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(States(events)); // a direct answer is not a task
    }

    [Fact]
    public async Task A_question_about_another_firm_returns_the_fixed_refusal_and_no_data()
    {
        var (handler, api) = Build("firm-a");
        using var _ = api;

        // Run 5001 exists — for firm-b. The partner must not learn that, nor anything about it.
        var events = await DrainAsync(q => handler.ExecuteAsync(Context("status of run 5001"), q, Ct), Ct);

        var text = string.Join("", events.Single(e => e.Message is not null).Message!.Parts!.Select(p => p.Text));
        Assert.Equal(AssistantAgentHandler.OutOfScope, text);
        Assert.DoesNotContain("firm-b", text);
        Assert.DoesNotContain("5001", text);
        Assert.DoesNotContain("completed", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_status_found_in_a_later_registered_firm_is_audited_as_the_reader_that_answered()
    {
        var (handler, api) = Build("firm-a", "firm-b");
        using var _ = api;
        var events = await DrainAsync(queue => handler.ExecuteAsync(Context("status of run 5001"), queue, Ct), Ct);
        Assert.Contains("firm-b", string.Join("", events.Single(item => item.Message is not null).Message!.Parts!.Select(part => part.Text)));
        await using var db = ChatApiTests.Db(api);
        Assert.Equal("firm-b", (await db.Audit.SingleAsync(audit => audit.Kind == AuditKinds.A2ARequest, Ct)).TenantId);
    }

    [Fact]
    public async Task A_question_naming_a_registered_firm_uses_that_same_reader_for_the_assistant_and_audit()
    {
        var assistant = new CapturingAssistant();
        var (handler, api) = Build(new A2AOptions { SimulatedStepMs = 1 }, null, TimeProvider.System, assistant, "firm-a", "firm-b");
        using var _ = api;
        await DrainAsync(queue => handler.ExecuteAsync(Context("What is the fee schedule procedure for firm-b?"), queue, Ct), Ct);
        Assert.Equal("firm-b", assistant.Reader?.TenantId.Value);
        await using var db = ChatApiTests.Db(api);
        Assert.Equal("firm-b", (await db.Audit.SingleAsync(audit => audit.Kind == AuditKinds.A2ARequest, Ct)).TenantId);
    }

    private sealed class CapturingAssistant : Maf.Lab.Plugins.Abstractions.IAssistantAnswer
    {
        public Principal? Reader { get; private set; }
        public Task<string> AnswerAsync(Principal principal, string question, CancellationToken ct)
        {
            Reader = principal;
            return System.Threading.Tasks.Task.FromResult("Answer from the selected tenant.");
        }
    }

    [Fact]
    public async Task Starting_a_run_reports_progress_and_ends_with_a_structured_artifact()
    {
        var (handler, api) = Build("firm-a");
        using var _ = api;

        var events = await DrainAsync(q => handler.ExecuteAsync(Context("start a billing run for firm-a 2026-06"), q, Ct), Ct);

        var states = States(events).ToList();
        Assert.Contains(TaskState.Working, states);
        Assert.Equal(TaskState.Completed, states[^1]);

        var artifact = Assert.Single(events, e => e.ArtifactUpdate is not null).ArtifactUpdate!.Artifact!;
        var data = Assert.Single(artifact.Parts!).Data!.Value;
        Assert.Equal("firm-a", data.GetProperty("firmId").GetString());
        Assert.Equal("2026-06", data.GetProperty("period").GetString());
        Assert.True(data.GetProperty("simulated").GetBoolean()); // never mistaken for a real run
    }

    [Fact]
    public async Task A_run_without_a_period_asks_for_one_and_finishes_when_it_is_supplied()
    {
        var (handler, api) = Build("firm-a");
        using var _ = api;

        var asked = await DrainAsync(q => handler.ExecuteAsync(Context("start a billing run for firm-a"), q, Ct), Ct);
        Assert.Equal(TaskState.InputRequired, States(asked).Last());
        var question = asked.Last(e => e.StatusUpdate is not null).StatusUpdate!.Status!.Message!;
        Assert.Contains("period", string.Join("", question.Parts!.Select(p => p.Text)), StringComparison.OrdinalIgnoreCase);

        // The caller answers under the same task; the earlier turn is in the task's history.
        var existing = new AgentTask
        {
            Id = "t-1",
            ContextId = "ctx-1",
            Status = new global::A2A.TaskStatus { State = TaskState.InputRequired },
            History = [new Message { MessageId = "m-1", Role = MessageRole.User, Parts = [new Part { Text = "start a billing run for firm-a" }] }],
        };
        var resumed = await DrainAsync(q => handler.ExecuteAsync(Context("2026-06", existing: existing), q, Ct), Ct);

        Assert.Equal(TaskState.Completed, States(resumed).Last());
    }

    [Fact]
    public async Task Starting_a_run_for_another_firm_is_rejected()
    {
        var (handler, api) = Build("firm-a");
        using var _ = api;

        var events = await DrainAsync(q => handler.ExecuteAsync(Context("start a billing run for firm-b 2026-06"), q, Ct), Ct);

        Assert.Equal(TaskState.Rejected, States(events).Last());
        var message = events.Last(e => e.StatusUpdate is not null).StatusUpdate!.Status!.Message!;
        Assert.Equal(AssistantAgentHandler.OutOfScope, string.Join("", message.Parts!.Select(p => p.Text)));
    }

    [Fact]
    public async Task Cancelling_moves_the_task_to_cancelled()
    {
        var (handler, api) = Build("firm-a");
        using var _ = api;

        var events = await DrainAsync(q => handler.CancelAsync(Context("start a billing run"), q, Ct), Ct);

        Assert.Equal(TaskState.Canceled, States(events).Last());
    }

    [Fact]
    public async Task A_run_cancelled_through_the_other_replica_stops_before_its_next_stage()
    {
        // Two api replicas over one database: the run goes on one, the cancel is recorded through the other.
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var (handler, working) = Build(new A2AOptions { SimulatedStepMs = 400, CancelPollMs = 20 }, null, time, "firm-a");
        using var _ = working;
        using var other = new ApiFactory(ApiFactory.ProceduralModel(), dataDir: working.DataDir) { InstalledPlugins = A2APluginSupport.Installed };
        var shared = other.Services.GetRequiredService<ITaskStore>();
        await shared.SaveTaskAsync("t-x", new AgentTask
        {
            Id = "t-x", ContextId = "ctx-1",
            Status = new global::A2A.TaskStatus { State = TaskState.Working, Timestamp = DateTimeOffset.UtcNow },
        }, Ct);

        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var running = DrainAsync(q => handler.ExecuteAsync(Context("start a billing run for firm-a 2026-06", "t-x"), q, Ct), Ct,
            item => { if (item.StatusUpdate?.Status?.State == TaskState.Working) started.TrySetResult(); });
        await started.Task.WaitAsync(TimeSpan.FromSeconds(20), Ct);
        await shared.SaveTaskAsync("t-x", new AgentTask
        {
            Id = "t-x", ContextId = "ctx-1",
            Status = new global::A2A.TaskStatus { State = TaskState.Canceled, Timestamp = DateTimeOffset.UtcNow },
        }, Ct);
        // Only the cancel watch advances; the next simulated stage stays held until cancellation reaches it.
        time.Advance(TimeSpan.FromMilliseconds(20));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running.WaitAsync(TimeSpan.FromSeconds(5), Ct));
        Assert.Equal(TaskState.Canceled, (await shared.GetTaskAsync("t-x", Ct))!.Status!.State);
    }

    [Fact]
    public async Task Every_request_is_audited_without_its_text()
    {
        var (handler, api) = Build("firm-a");
        using var _ = api;

        await DrainAsync(q => handler.ExecuteAsync(Context("status of run 4417"), q, Ct), Ct);

        await using var db = ChatApiTests.Db(api);
        var row = Assert.Single(await db.Audit.Where(a => a.Kind == AuditKinds.A2ARequest).ToListAsync(Ct));
        Assert.Equal("acme-portal", row.PrincipalId);
        Assert.Equal("a2a.message", row.ToolName);
        Assert.Contains("taskId=t-1", row.Arguments);
        Assert.DoesNotContain("4417", row.Arguments); // identifiers of the request, not its content
        Assert.True(AuditChain.Verify(await db.Audit.OrderBy(a => a.Id).ToListAsync(Ct)).Intact);
    }
}

/// <summary>A fixed partner, standing in for the one a request's token would resolve to.</summary>
internal sealed class FixedPartner(PartnerPrincipal partner) : IPartnerAccessor
{
    public PartnerPrincipal Current { get; } = partner;
}

/// <summary>
/// Billing's server as the A2A handler reaches it (introduce-plugins 8.1, extract-billing): its two run tools over a seed,
/// each answering only for the tenant the caller's token names — as the real server scopes every read by its principal.
/// </summary>
file sealed class SeededBillingServer(string seed) : IToolSource
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly JsonArray _runs = JsonNode.Parse(seed)!.AsArray();

    public Task<ToolSet> GetToolsAsync(string bearerToken, ConfirmationSink? confirmations, CancellationToken ct,
        IReadOnlySet<string>? domains = null)
    {
        var tenant = TenantOf(bearerToken);
        var mine = _runs.Where(r => r!["firmId"]!.GetValue<string>() == tenant).ToList();
        var status = AIFunctionFactory.Create((string runId) =>
            mine.FirstOrDefault(r => r!["runId"]!.GetValue<string>() == runId) is { } run
                ? Result(run.DeepClone(), false)
                : Result(new JsonObject { ["error"] = "not found" }, true), "get_billing_run_status");
        var search = AIFunctionFactory.Create((int? maxResults = null) =>
            Result(new JsonObject
            {
                ["runs"] = new JsonArray([.. mine.Take(maxResults ?? 10).Select(r => r!.DeepClone())]),
                ["totalMatches"] = mine.Count,
                ["truncated"] = false,
            }, false), "search_billing_runs");
        return Task.FromResult(new ToolSet([status, search], null));
    }

    private static JsonElement Result(JsonNode structured, bool isError) =>
        JsonSerializer.SerializeToElement(new JsonObject { ["structuredContent"] = structured, ["isError"] = isError }, Json);

    private static string TenantOf(string token) =>
        JsonNode.Parse(Convert.FromBase64String(Pad(token.Split('.')[1])))!["tenant_id"]!.GetValue<string>();

    private static string Pad(string part) =>
        part.Replace('-', '+').Replace('_', '/') + new string('=', (4 - part.Length % 4) % 4);
}
