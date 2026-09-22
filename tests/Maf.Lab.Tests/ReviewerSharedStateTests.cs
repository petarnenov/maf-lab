using Maf.Lab.A2A;
using Sdk = global::A2A;
using Maf.Lab.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

namespace Maf.Lab.Tests;

/// <summary>
/// Two replicas of the reviewer serve one caller's review. Nothing about it may live in either one's memory: a
/// task started on one has to be readable on the other, and a webhook registered on one honoured by the other.
/// </summary>
public class ReviewerSharedStateTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_task_started_on_one_replica_is_read_on_another()
    {
        // One store, two hosts over it — which is what two replicas behind the balancer are.
        var tasks = new Sdk.InMemoryTaskStore();
        var webhooks = new FakePushConfigStore();
        await using var first = new ComplianceFactory { Tasks = tasks, PushConfigs = webhooks };
        await using var second = new ComplianceFactory { Tasks = tasks, PushConfigs = webhooks };
        await first.ListenAsync();
        await second.ListenAsync();

        await tasks.SaveTaskAsync("t_review", new Sdk.AgentTask
        {
            Id = "t_review",
            ContextId = "c1",
            Status = new Sdk.TaskStatus { State = Sdk.TaskState.Working },
        }, Ct);

        // The replica that never saw it created answers for it.
        var fromSecond = await second.App.Services.GetRequiredService<Sdk.ITaskStore>().GetTaskAsync("t_review", Ct);
        Assert.NotNull(fromSecond);
        Assert.Equal(Sdk.TaskState.Working, fromSecond!.Status?.State);
    }

    [Fact]
    public async Task A_webhook_registered_on_one_replica_is_known_to_the_other()
    {
        var tasks = new Sdk.InMemoryTaskStore();
        var webhooks = new FakePushConfigStore();
        await using var first = new ComplianceFactory { Tasks = tasks, PushConfigs = webhooks };
        await using var second = new ComplianceFactory { Tasks = tasks, PushConfigs = webhooks };
        await first.ListenAsync();
        await second.ListenAsync();

        await first.App.Services.GetRequiredService<IPushConfigStore>()
            .SaveAsync(new PushConfigRecord("w1", "t_review", "http://partner.example/hook", "secret"), Ct);

        var known = await second.App.Services.GetRequiredService<IPushConfigStore>().ListAsync("t_review", Ct);
        var registered = Assert.Single(known);
        Assert.Equal("http://partner.example/hook", registered.Url);
    }

    [Fact]
    public async Task The_reviewer_does_not_start_without_a_store_for_its_tasks()
    {
        // No store registered and none configured: the reviewer says what it needs rather than serving half of it.
        var failure = Assert.Throws<InvalidOperationException>(() =>
            Maf.Lab.ComplianceAgent.Program.BuildApp([], builder =>
            {
                builder.WebHost.UseSetting("urls", "http://127.0.0.1:0");
                builder.Logging.ClearProviders();
            }).StartAsync(Ct).GetAwaiter().GetResult());

        // Whichever notices first — the container building the store, or the check that the store is there — the
        // answer is the same: this replica does not serve, and it names what it could not reach.
        Assert.Contains("RedisTaskStore", failure.Message, StringComparison.Ordinal);
    }
}
