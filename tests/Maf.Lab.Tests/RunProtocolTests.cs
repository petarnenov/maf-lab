using System.Net;
using System.Net.Http.Json;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.TestSupport;
using Microsoft.Extensions.AI;

namespace Maf.Lab.Tests;

/// <summary>
/// The shape of a run on the wire: it begins once, ends once, says nothing after that, and carries only what a
/// client may see. And it can be stopped.
/// </summary>
public class RunProtocolTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_run_begins_once_ends_once_and_says_nothing_after()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel());
        var events = await ApiFactory.ChatAsync(api.ClientFor("adam", "firm-a", Role.ADVISOR), "hello");

        var names = events.Select(e => e.Name).ToList();
        Assert.Equal("RUN_STARTED", names[0]);
        Assert.Single(names, n => n == "RUN_STARTED");
        Assert.Single(names, n => n is "RUN_FINISHED" or "RUN_ERROR");
        Assert.Equal("RUN_FINISHED", names[^1]);
    }

    [Fact]
    public async Task Every_event_of_a_run_names_the_run_it_belongs_to()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel());
        var events = await ApiFactory.ChatAsync(api.ClientFor("adam", "firm-a", Role.ADVISOR), "hello", runId: "r_known");

        foreach (var e in events.Where(e => e.Name is "RUN_STARTED" or "RUN_FINISHED"))
        {
            Assert.Equal("r_known", e.Data.GetProperty("runId").GetString());
        }
    }

    [Fact]
    public async Task A_run_without_a_thread_creates_one_and_reports_it()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel());
        var events = await ApiFactory.ChatAsync(api.ClientFor("adam", "firm-a", Role.ADVISOR), "hello");

        Assert.StartsWith("c_", ApiFactory.ThreadOf(events));
    }

    [Fact]
    public async Task A_failing_turn_ends_as_an_error_with_nothing_internal_in_it()
    {
        var chat = new ScriptedChatClient((_, _, _) => throw new InvalidOperationException("Qdrant at 10.0.0.7:6334 refused the connection"));
        using var api = new ApiFactory(chat);

        var events = await ApiFactory.ChatAsync(api.ClientFor("adam", "firm-a", Role.ADVISOR), "hello");

        var last = events[^1];
        Assert.Equal("RUN_ERROR", last.Name);
        var message = last.Data.GetProperty("message").GetString()!;
        foreach (var internals in new[] { "Qdrant", "10.0.0.7", "InvalidOperationException", "at Maf.Lab" })
        {
            Assert.DoesNotContain(internals, message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task A_turn_with_no_answer_opens_no_message()
    {
        var chat = new ScriptedChatClient((_, _, _) => ScriptedChatClient.Text(""));
        using var api = new ApiFactory(chat);

        var events = await ApiFactory.ChatAsync(api.ClientFor("adam", "firm-a", Role.ADVISOR), "hello");

        Assert.DoesNotContain(events, e => e.Name == "TEXT_MESSAGE_START");
    }

    [Fact]
    public async Task The_words_a_user_typed_do_not_come_back_in_a_tool_call()
    {
        var tools = new FakeToolSource();
        using var api = new ApiFactory(ApiFactory.ProceduralModel(), tools);

        var events = await ApiFactory.ChatAsync(
            api.ClientFor("adam", "firm-a", Role.ADVISOR),
            "what is the procedure when SECRET-PHRASE-9 is missing");

        var wire = string.Join("\n", events.Where(e => e.Name.StartsWith("TOOL_CALL")).Select(e => e.Data.ToString()));
        Assert.NotEmpty(wire);
        Assert.DoesNotContain("SECRET-PHRASE-9", wire);
    }

    [Fact]
    public async Task A_run_stopped_by_its_client_ends_and_nothing_runs_after()
    {
        // A stop is the client walking away from the request (agui-protocol-only): there is no stop endpoint, and the run's
        // token is the request's own, so the replica serving the request is always the one that stops.
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tools = new FakeToolSource
        {
            BeforeSearchExecutes = async () =>
            {
                reached.TrySetResult();
                await release.Task.WaitAsync(TimeSpan.FromSeconds(10));
            },
        };
        using var api = new ApiFactory(ApiFactory.ProceduralModel(), tools);
        var client = api.ClientFor("adam", "firm-a", Role.ADVISOR);
        using var stop = new CancellationTokenSource();

        var run = ApiFactory.ChatAsync(client, "what is the procedure when a fee schedule is missing", runId: "r_stop", cancel: stop.Token);
        await reached.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        await stop.CancelAsync();
        release.TrySetResult();
        // The client walked away; the test host reports that as a cancellation, an aborted read or a failed request,
        // whichever lands first. What the server did about it is what is asserted below.
        var walkedAway = await Assert.ThrowsAnyAsync<Exception>(() => run.WaitAsync(TimeSpan.FromSeconds(10), Ct));
        Assert.True(walkedAway is OperationCanceledException or IOException or HttpRequestException, walkedAway.GetType().Name);

        // The turn stopped where it was: one tool had begun, nothing was invoked after the stop, and the run is kept as
        // cancelled rather than answered.
        await WaitUntil(async () => (await api.Runs.GetAsync("r_stop", Ct))?.Outcome == Maf.Lab.Domain.SharedState.RunOutcomes.Cancelled);
        Assert.Equal(["search_documents"], tools.Invocations);
    }

    [Fact]
    public async Task A_tool_call_a_stop_interrupts_is_cancelled_and_audited_and_records_no_turn()
    {
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tools = new FakeToolSource
        {
            WhileSearching = async ct =>
            {
                reached.TrySetResult();
                try
                {
                    await Task.Delay(Timeout.Infinite, ct);
                }
                catch (OperationCanceledException)
                {
                    cancelled.TrySetResult();
                    throw;
                }
            },
        };
        using var api = new ApiFactory(ApiFactory.ProceduralModel(), tools);
        var client = api.ClientFor("adam", "firm-a", Role.ADVISOR);
        using var stop = new CancellationTokenSource();

        var run = ApiFactory.ChatAsync(client, "what is the procedure when a fee schedule is missing", runId: "r_held", cancel: stop.Token);
        await reached.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        await stop.CancelAsync();
        // The client walked away; the test host reports that as a cancellation, an aborted read or a failed request,
        // whichever lands first. What the server did about it is what is asserted below.
        var walkedAway = await Assert.ThrowsAnyAsync<Exception>(() => run.WaitAsync(TimeSpan.FromSeconds(10), Ct));
        Assert.True(walkedAway is OperationCanceledException or IOException or HttpRequestException, walkedAway.GetType().Name);

        // The call itself saw the stop: it was cancelled, not left to finish for nobody.
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        // The audit says what became of it, without the query; and the stopped run recorded no turn.
        Maf.Lab.Api.Storage.AuditRow? row = null;
        await WaitUntil(async () =>
        {
            await using var db = ChatApiTests.Db(api);
            row = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleOrDefaultAsync(
                db.Audit.Where(a => a.ToolName == "search_documents"), Ct);
            return row is not null;
        });
        Assert.Equal("cancelled", row!.Outcome);
        Assert.True(row.DurationMs >= 0);
        Assert.DoesNotContain("fee schedule", row.Arguments);
        await using (var db = ChatApiTests.Db(api))
        {
            Assert.Equal(0, await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.CountAsync(db.Turns, Ct));
        }
    }

    [Fact]
    public async Task A_run_stopped_while_the_model_answers_cancels_the_model_call()
    {
        // Where the money is: a paid model still streaming its answer when the person stops the run.
        var model = ApiFactory.ProceduralModel();
        model.HoldAfterWords = 3;
        using var api = new ApiFactory(model);
        var client = api.ClientFor("adam", "firm-a", Role.ADVISOR);
        using var stop = new CancellationTokenSource();

        var run = ApiFactory.ChatAsync(client, "what is the procedure when a fee schedule is missing", runId: "r_model", cancel: stop.Token);
        await model.Holding.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        var yielded = model.Yielded;
        var calls = model.Requests.Count;
        await stop.CancelAsync();
        // The client walked away; how the test host reports it is not what is asserted here.
        await Assert.ThrowsAnyAsync<Exception>(() => run.WaitAsync(TimeSpan.FromSeconds(10), Ct));

        // The call itself was cancelled, not waited for: its token fired, nothing more was read from it, no call followed.
        await model.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        // A stopped run's state is written as its request winds down, which takes longer while the whole suite runs.
        await WaitUntil(async () => (await api.Runs.GetAsync("r_model", Ct))?.Outcome == Maf.Lab.Domain.SharedState.RunOutcomes.Cancelled, seconds: 30);
        Assert.Equal(yielded, model.Yielded);
        Assert.Equal(calls, model.Requests.Count);
        await using var db = ChatApiTests.Db(api);
        Assert.Equal(0, await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.CountAsync(db.Turns, Ct));
    }

    [Fact]
    public async Task A_run_stopped_while_a_jev_call_is_out_cancels_it_and_calls_nothing_after()
    {
        var model = ApiFactory.ProceduralModel();
        // The run's first Jev call — the intent classification — is held until its request is cancelled.
        var jev = new FakeJev { HoldWhen = _ => true };
        using var api = new ApiFactory(model, jev: jev);
        var client = api.ClientFor("adam", "firm-a", Role.ADVISOR);
        using var stop = new CancellationTokenSource();

        var run = ApiFactory.ChatAsync(client, "what is the procedure when a fee schedule is missing", runId: "r_jev", cancel: stop.Token);
        await jev.Holding.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        var jevCalls = jev.Requests.Count;
        await stop.CancelAsync();
        await Assert.ThrowsAnyAsync<Exception>(() => run.WaitAsync(TimeSpan.FromSeconds(10), Ct));

        await jev.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        // A stopped run's state is written as its request winds down, which takes longer while the whole suite runs.
        string? outcome = null;
        for (var i = 0; i < 300 && outcome != Maf.Lab.Domain.SharedState.RunOutcomes.Cancelled; i++)
        {
            outcome = (await api.Runs.GetAsync("r_jev", Ct))?.Outcome;
            await Task.Delay(100, Ct);
        }
        Assert.True(outcome == Maf.Lab.Domain.SharedState.RunOutcomes.Cancelled, $"the run ended {outcome ?? "with no state"}");
        await Task.Delay(300, Ct);
        Assert.Equal(jevCalls, jev.Requests.Count);
        Assert.Empty(model.Requests);
    }

    [Fact]
    public async Task A_run_id_used_before_starts_no_run()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel());
        var client = api.ClientFor("adam", "firm-a", Role.ADVISOR);
        var thread = ApiFactory.ThreadOf(await ApiFactory.ChatAsync(client, "hello", runId: "r_once"));

        // The run id names the turn it recorded: a second run under it is refused before it starts.
        var response = await client.PostAsJsonAsync("/api/chat", new
        {
            threadId = thread,
            runId = "r_once",
            messages = new[] { new { id = "u2", role = "user", content = "hello again" } },
        }, Ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await using var db = ChatApiTests.Db(api);
        Assert.Equal(1, await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.CountAsync(db.Turns, Ct));
    }

    [Fact]
    public async Task There_is_no_stop_endpoint_beside_the_protocol()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel());
        var client = api.ClientFor("adam", "firm-a", Role.ADVISOR);

        var response = await client.PostAsync("/api/chat/r_nothing/stop", null, Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task WaitUntil(Func<Task<bool>> condition, int seconds = 10)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (!await condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "the condition was not met in time");
            await Task.Delay(20, Ct);
        }
    }

    [Fact]
    public async Task A_run_needs_something_to_run_on()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel());
        var client = api.ClientFor("adam", "firm-a", Role.ADVISOR);

        var response = await client.PostAsJsonAsync("/api/chat", new
        {
            runId = "r_empty",
            messages = Array.Empty<object>(),
        }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
