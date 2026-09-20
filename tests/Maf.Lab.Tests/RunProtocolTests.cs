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
    public async Task A_run_can_be_stopped_and_nothing_runs_after()
    {
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

        var run = ApiFactory.ChatAsync(client, "what is the procedure when a fee schedule is missing", runId: "r_stop");
        await reached.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);

        var stop = await client.PostAsync("/api/chat/r_stop/stop", null, Ct);
        Assert.Equal(HttpStatusCode.Accepted, stop.StatusCode);

        release.TrySetResult();
        var events = await run.WaitAsync(TimeSpan.FromSeconds(10), Ct);

        // The turn stopped where it was: one tool had begun, nothing was invoked after the stop, and the run
        // says it was cancelled rather than that it succeeded.
        Assert.Equal(["search_documents"], tools.Invocations);
        var outcome = events[^1].Data.GetProperty("outcome").GetProperty("type").GetString();
        Assert.Equal("cancelled", outcome);
    }

    [Fact]
    public void Walking_away_from_the_stream_stops_the_run()
    {
        // A run's token hangs off the request's own. When the client goes, the run goes with it — which is why
        // abandoning the stream needs no stop request. The in-memory test host does not abort a request the way
        // a real socket does, so the end-to-end version of this is a live check.
        var registry = new Maf.Lab.Api.Agent.Streaming.RunRegistry();
        using var request = new CancellationTokenSource();
        using var registration = registry.Start("r_abandoned", request.Token);

        Assert.False(registration.Token.IsCancellationRequested);
        Assert.True(registry.IsRunning("r_abandoned"));

        request.Cancel();

        Assert.True(registration.Token.IsCancellationRequested);
    }

    [Fact]
    public async Task A_stop_that_lands_on_the_wrong_replica_finds_the_run_anyway()
    {
        // The balancer sends a stop round-robin, so with two replicas it lands on the other one every time.
        // The replica that was asked resolves its own service and asks the rest.
        var here = new Maf.Lab.Api.Agent.Streaming.RunRegistry();
        var asked = new List<string>();
        var stopper = new Maf.Lab.Api.Agent.Streaming.RunStopper(
            here,
            new StubResolver(new Dictionary<string, string[]> { ["api"] = ["10.0.0.5", "10.0.0.6"] }),
            new StubHttpClientFactory(address => { asked.Add(address); return address == "10.0.0.6"; }),
            Microsoft.Extensions.Options.Options.Create(new Maf.Lab.Api.Agent.Streaming.RunStopOptions()),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Maf.Lab.Api.Agent.Streaming.RunStopper>.Instance);

        Assert.True(await stopper.StopAsync("r_elsewhere", "token", localOnly: false, Ct));
        Assert.Equal(["10.0.0.5", "10.0.0.6"], asked);
    }

    [Fact]
    public async Task A_stop_asked_to_stay_local_does_not_ask_anyone_else()
    {
        var asked = new List<string>();
        var stopper = new Maf.Lab.Api.Agent.Streaming.RunStopper(
            new Maf.Lab.Api.Agent.Streaming.RunRegistry(),
            new StubResolver(new Dictionary<string, string[]> { ["api"] = ["10.0.0.5"] }),
            new StubHttpClientFactory(address => { asked.Add(address); return true; }),
            Microsoft.Extensions.Options.Options.Create(new Maf.Lab.Api.Agent.Streaming.RunStopOptions()),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Maf.Lab.Api.Agent.Streaming.RunStopper>.Instance);

        Assert.False(await stopper.StopAsync("r_elsewhere", "token", localOnly: true, Ct));
        Assert.Empty(asked);
    }

    [Fact]
    public void A_finished_run_is_no_longer_stoppable()
    {
        var registry = new Maf.Lab.Api.Agent.Streaming.RunRegistry();
        using (registry.Start("r_done", CancellationToken.None))
        {
            Assert.True(registry.Stop("r_done"));
        }

        Assert.False(registry.Stop("r_done"));
    }

    [Fact]
    public async Task Stopping_a_run_this_instance_is_not_running_says_so()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel());
        var client = api.ClientFor("adam", "firm-a", Role.ADVISOR);

        var response = await client.PostAsync("/api/chat/r_nothing/stop", null, Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
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

/// <summary>A sibling that either has the run or does not.</summary>
internal sealed class StubHttpClientFactory(Func<string, bool> owns) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(new Handler(owns));

    private sealed class Handler(Func<string, bool> owns) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(owns(request.RequestUri!.Host) ? HttpStatusCode.Accepted : HttpStatusCode.NotFound));
    }
}
