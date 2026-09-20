using Maf.Lab.A2A;
using A2A;
using Maf.Lab.Api.A2A;
// Both libraries have a Role; the message role is the one this file means.
using MessageRole = A2A.Role;
using Maf.Lab.Api.Agent;
using Maf.Lab.Api.Compliance;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Retrieval.Billing;
using Maf.Lab.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

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

    private static (BillingAgentHandler Handler, ApiFactory Api) Build(params string[] firms)
    {
        var api = new ApiFactory(ApiFactory.ProceduralModel());
        var partner = new PartnerPrincipal("acme-portal", firms.Select(TenantId.Firm).ToHashSet(),
            new HashSet<string> { A2AScopes.BillingRead });
        var handler = new BillingAgentHandler(
            new FixedPartner(partner),
            new BillingSeedStore(Seed),
            Options.Create(new A2AOptions { SimulatedStepMs = 1 }),
            api.Services.GetRequiredService<ToolAudit>(),
            api.Services.GetRequiredService<AssistantBridge>(),
            api.Services.GetRequiredService<Microsoft.Extensions.Hosting.IHostApplicationLifetime>(),
            TimeProvider.System,
            NullLogger<BillingAgentHandler>.Instance);
        return (handler, api);
    }

    private static async Task<List<StreamResponse>> DrainAsync(
        Func<AgentEventQueue, System.Threading.Tasks.Task> run, CancellationToken ct)
    {
        var queue = new AgentEventQueue();
        var collected = new List<StreamResponse>();
        var reader = System.Threading.Tasks.Task.Run(async () =>
        {
            await foreach (var item in queue.WithCancellation(ct))
            {
                collected.Add(item);
            }
        }, ct);
        await run(queue);
        queue.Complete();
        await reader;
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
        Assert.Equal(BillingAgentHandler.OutOfScope, text);
        Assert.DoesNotContain("firm-b", text);
        Assert.DoesNotContain("5001", text);
        Assert.DoesNotContain("completed", text, StringComparison.OrdinalIgnoreCase);
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
        Assert.Equal(BillingAgentHandler.OutOfScope, string.Join("", message.Parts!.Select(p => p.Text)));
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
