using System.Net.Http.Headers;
using System.Net.Http.Json;
using A2A;
using Maf.Lab.A2A;

namespace Maf.Lab.Tests;

/// <summary>
/// Two reviewer replicas over one store, as in the stack: a review runs on one, its cancel reaches the other, and the
/// review still stops — the store is where the cancel is known, so nothing has to be routed to the replica at work.
/// </summary>
public class ReviewerCancelAcrossReplicasTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<IA2AClient> ClientAsync(ComplianceFactory replica)
    {
        var http = replica.CreateClient();
        var token = await http.PostAsJsonAsync("/a2a/token",
            new A2AEndpoints.TokenRequest("maf-lab-assistant", "assistant-secret"), Ct);
        token.EnsureSuccessStatusCode();
        var issued = await token.Content.ReadFromJsonAsync<A2AEndpoints.TokenResponse>(Ct);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", issued!.AccessToken);
        return new A2AClient(new Uri(new Uri(replica.Address), "/a2a"), http);
    }

    private static Message Ask() => new()
    {
        MessageId = Guid.NewGuid().ToString("N"),
        Role = global::A2A.Role.User,
        Parts =
        [
            new Part
            {
                Data = System.Text.Json.JsonSerializer.SerializeToElement(new
                {
                    adjustmentId = "ADJ-9", firmId = "firm-a", accountId = "ACC-1042", amount = 250m, reason = "Billed twice",
                }),
            },
        ],
    };

    [Fact]
    public async Task A_cancel_through_the_other_replica_stops_the_review()
    {
        var shared = new TerminalGuardTaskStore();
        await using var working = new ComplianceFactory { Tasks = shared, ReviewMs = 3_000 };
        await using var other = new ComplianceFactory { Tasks = shared, ReviewMs = 3_000 };
        await working.ListenAsync();
        await other.ListenAsync();
        var onWorking = await ClientAsync(working);
        var onOther = await ClientAsync(other);

        // The review starts on one replica, streamed; its task id comes with the first update.
        string? taskId = null;
        var stream = Task.Run(async () =>
        {
            await foreach (var update in onWorking.SendStreamingMessageAsync(new SendMessageRequest { Message = Ask() }, Ct))
            {
                taskId ??= update.Task?.Id ?? update.StatusUpdate?.TaskId;
            }
        }, Ct);
        while (taskId is null)
        {
            await Task.Delay(20, Ct);
        }
        await Task.Delay(300, Ct);

        await onOther.CancelTaskAsync(new CancelTaskRequest { Id = taskId }, Ct);

        // Longer than the review would have taken: it stopped, wrote no verdict, and the task stayed canceled.
        await Task.Delay(3_500, Ct);
        // However the working replica ends its stream after the cancel is not what this test is about.
        await stream.ContinueWith(_ => { }, Ct).WaitAsync(TimeSpan.FromSeconds(5), Ct);
        var task = await shared.GetTaskAsync(taskId, Ct);
        Assert.Equal(TaskState.Canceled, task!.Status!.State);
        Assert.True(task.Artifacts is null or { Count: 0 });
        Assert.Contains(working.Logs.Messages, m => m.Contains("review canceled through the store", StringComparison.Ordinal));
    }
}
