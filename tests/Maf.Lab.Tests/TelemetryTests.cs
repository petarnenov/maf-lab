using System.Diagnostics;
using System.Diagnostics.Metrics;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Domain.Tracing;
using Maf.Lab.TestSupport;
using Maf.Lab.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Maf.Lab.Tests;

/// <summary>What the lab emits about itself: where a signal says it came from, and when it emits nothing at all.</summary>
public class TelemetryTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Every_signal_says_which_service_and_which_replica_produced_it()
    {
        var resource = LabTelemetry.ResourceFor("maf-lab-api").Build();
        var attributes = resource.Attributes.ToDictionary(a => a.Key, a => a.Value.ToString());

        Assert.Equal("maf-lab-api", attributes["service.name"]);
        // The instance name the load balancer already reports, so a number belongs to a replica.
        Assert.Equal(InstanceIdentity.Name, attributes["service.instance.id"]);
    }

    [Fact]
    public async Task With_no_endpoint_configured_nothing_is_exported_and_a_turn_is_unaffected()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel("ANSWER-NO-OTEL."));
        var adam = api.ClientFor("adam", "firm-a", Role.ADVISOR);

        // Nothing is listening, so nothing is wired: no provider is registered to export to.
        Assert.Null(LabTelemetry.EndpointOf(api.Services.GetRequiredService<IConfiguration>()));
        Assert.Null(api.Services.GetService<TracerProvider>());

        var events = await ApiFactory.ChatAsync(adam, "what is the procedure when a fee schedule is missing");
        Assert.Contains("ANSWER-NO-OTEL.", ApiFactory.AnswerOf(events));
    }

    [Fact]
    public void With_an_endpoint_configured_the_three_signals_are_wired()
    {
        // Against a plain host, because what is under test is the wiring itself rather than an api that has it.
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{LabTelemetry.Section}:Endpoint"] = "http://localhost:4317",
        });
        builder.AddLabTelemetry("maf-lab-api");

        using var host = builder.Build();
        Assert.NotNull(host.Services.GetService<TracerProvider>());
        Assert.NotNull(host.Services.GetService<MeterProvider>());
        Assert.Equal("http://localhost:4317", LabTelemetry.EndpointOf(builder.Configuration));
        _ = Ct;
    }

    [Fact]
    public void With_no_endpoint_the_wiring_adds_nothing_at_all()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddLabTelemetry("maf-lab-api");

        using var host = builder.Build();
        Assert.Null(host.Services.GetService<TracerProvider>());
        Assert.Null(host.Services.GetService<MeterProvider>());
    }

    [Fact]
    public async Task A_model_call_is_instrumented_by_the_framework_and_carries_no_content()
    {
        using var signals = new SignalCapture();
        using var api = new ApiFactory(ApiFactory.ProceduralModel("ANSWER-OTEL-MARKER."));
        var adam = api.ClientFor("adam", "firm-a", Role.ADVISOR);

        await ApiFactory.ChatAsync(adam, "QUESTION-OTEL-MARKER procedure when a fee schedule is missing");

        // The span comes from Microsoft.Extensions.AI's own instrumentation, under the GenAI conventions.
        var model = signals.Activities.Where(a => a.Source.Name == LabTelemetry.AiSource).ToList();
        Assert.NotEmpty(model);
        Assert.All(model, a => Assert.True(a.Duration > TimeSpan.Zero || a.Duration == TimeSpan.Zero));

        // And so do the duration and token metrics.
        Assert.Contains(signals.Instruments, i => i.StartsWith("gen_ai.client.operation.duration"));

        // Nothing of what was asked or answered is in any of it.
        Assert.DoesNotContain("MARKER", signals.Dump());
    }

    [Fact]
    public async Task The_agent_run_is_the_parent_of_the_model_call()
    {
        using var signals = new SignalCapture();
        using var api = new ApiFactory(ApiFactory.ProceduralModel());
        var adam = api.ClientFor("adam", "firm-a", Role.ADVISOR);

        await ApiFactory.ChatAsync(adam, "what is the procedure when a fee schedule is missing");

        // One turn, one trace: some trace holds both the agent's run and the model call it made. Said this way
        // because the listener hears the whole process, and other test classes are running turns of their own.
        Assert.Contains(signals.Activities.GroupBy(a => a.TraceId), turn =>
            turn.Any(a => a.Source.Name == LabTelemetry.AgentsSource)
            && turn.Any(a => a.Source.Name == LabTelemetry.AiSource));
    }


    [Fact]
    public async Task Turns_are_counted_once_each_under_the_outcome_they_reached()
    {
        using var signals = new SignalCapture();
        using var api = new ApiFactory(ApiFactory.ProceduralModel());
        var adam = api.ClientFor("adam", "firm-a", Role.ADVISOR);

        await ApiFactory.ChatAsync(adam, "what is the procedure when a fee schedule is missing");

        var turns = signals.Instruments.Where(i => i.StartsWith("maf.turns=")).ToList();
        Assert.Contains(turns, t => t.Contains("outcome=started"));
        Assert.Contains(turns, t => t.Contains("outcome=answered"));
        Assert.Contains(signals.Instruments, i => i.StartsWith("maf.turn.duration="));

        // The tool call is counted where its audit row is written, by tool and by how it went.
        Assert.Contains(signals.Instruments, i => i.StartsWith("maf.tool.calls=") && i.Contains("tool.name=search_documents"));
    }

    [Fact]
    public async Task A_turn_that_fails_is_counted_as_failed_and_not_as_answered()
    {
        using var signals = new SignalCapture();
        var chat = new ScriptedChatClient((_, _, _) => throw new InvalidOperationException("the model is down"));
        using var api = new ApiFactory(chat);

        await ApiFactory.ChatAsync(api.ClientFor("adam", "firm-a", Role.ADVISOR), "anything at all");

        Assert.Contains(signals.Instruments, i => i.StartsWith("maf.turns=") && i.Contains("outcome=failed"));
    }

    [Fact]
    public async Task An_unknown_tool_is_counted_apart_from_the_calls_that_worked()
    {
        using var signals = new SignalCapture();
        var chat = new ScriptedChatClient((messages, _, n) => n == 1
            ? ScriptedChatClient.Call("send_email", new() { ["to"] = "external@evil.example" })
            : ScriptedChatClient.Text("I can't send email."));
        using var api = new ApiFactory(chat);
        var adam = api.ClientFor("adam", "firm-a", Role.ADVISOR);

        await ApiFactory.ChatAsync(adam, "summarise our fee arrangement for me");

        var calls = signals.Instruments.Where(i => i.StartsWith("maf.tool.calls=")).ToList();
        Assert.Contains(calls, c => c.Contains("outcome=unknown_tool") && c.Contains("tool.name=send_email"));
    }


    [Fact]
    public async Task Nothing_a_person_wrote_or_a_model_said_reaches_any_signal()
    {
        using var signals = new SignalCapture();
        var tools = new FakeToolSource
        {
            SearchPayloadJson = """
                {"results":[{"snippet":"DOCUMENT-MARKER assign the missing fee schedule.","sourcePath":"procedures/x.txt",
                "sectionPath":"Procedure > Step 1","score":0.9,"updatedAt":"2026-09-01T00:00:00Z","docId":"shared/procedures/x.txt"}],
                "totalMatches":1,"truncated":false,"refineHint":null}
                """,
        };
        var chat = new ScriptedChatClient((messages, _, n) => n == 1
            ? [.. ScriptedChatClient.Thinking("REASONING-MARKER I should look this up."),
               .. ScriptedChatClient.Call("search_documents", new() { ["query"] = "QUESTION-MARKER fee schedule" })]
            : ScriptedChatClient.Text("ANSWER-MARKER assign it and re-run."));
        using var api = new ApiFactory(chat, tools);

        await ApiFactory.ChatAsync(api.ClientFor("adam", "firm-a", Role.ADVISOR),
            "QUESTION-MARKER what is the procedure when a fee schedule is missing");

        // Spans, metrics and logs alike: identifiers, names, counts and durations — and none of the words.
        var everything = signals.Dump() + "\n" + string.Join("\n", api.Logs.Messages);
        foreach (var marker in new[] { "QUESTION-MARKER", "ANSWER-MARKER", "REASONING-MARKER", "DOCUMENT-MARKER" })
        {
            Assert.DoesNotContain(marker, everything);
        }
        // The turn did happen, so the assertion is about what was kept out rather than about an empty capture.
        Assert.NotEmpty(signals.Activities);
        Assert.NotEmpty(signals.Instruments);
    }


    [Fact]
    public async Task A_run_tells_the_client_which_trace_it_belongs_to()
    {
        using var signals = new SignalCapture();
        using var api = new ApiFactory(ApiFactory.ProceduralModel());
        var events = await ApiFactory.ChatAsync(api.ClientFor("adam", "firm-a", Role.ADVISOR),
            "what is the procedure when a fee schedule is missing");

        var start = ApiFactory.TracesOf(events).First(t => t.GetProperty("kind").GetString() == TraceKinds.TurnStart);
        var traceId = start.GetProperty("data").GetProperty("traceId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(traceId));
        // It is the trace the turn's own spans are in, so opening it finds them.
        Assert.Contains(signals.Activities, a => a.TraceId.ToHexString() == traceId);
    }

    /// <summary>
    /// Collects what the process emits while a turn runs. A listener is all it takes: the framework writes its
    /// spans and instruments whether or not an exporter is configured, which is what a test wants to see.
    /// </summary>
    private sealed class SignalCapture : IDisposable
    {
        private static readonly string[] Sources = [LabTelemetry.AiSource, LabTelemetry.AgentsSource, LabTelemetry.SourceName];

        private readonly ActivityListener _activities;
        private readonly MeterListener _meters;
        private readonly List<Activity> _collected = [];
        private readonly List<string> _measurements = [];
        private readonly Lock _gate = new();

        public SignalCapture()
        {
            _activities = new ActivityListener
            {
                ShouldListenTo = source => Sources.Contains(source.Name),
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = activity => { lock (_gate) { _collected.Add(activity); } },
            };
            ActivitySource.AddActivityListener(_activities);

            _meters = new MeterListener
            {
                InstrumentPublished = (instrument, listener) =>
                {
                    if (Sources.Contains(instrument.Meter.Name)) listener.EnableMeasurementEvents(instrument);
                },
            };
            _meters.SetMeasurementEventCallback<double>((instrument, value, tags, _) => Record(instrument, value, tags));
            _meters.SetMeasurementEventCallback<long>((instrument, value, tags, _) => Record(instrument, value, tags));
            _meters.Start();
        }

        public IReadOnlyList<Activity> Activities { get { lock (_gate) { return _collected.ToList(); } } }

        public IReadOnlyList<string> Instruments { get { lock (_gate) { return _measurements.ToList(); } } }

        /// <summary>Everything collected, as text, so a test can refuse a marker anywhere in it.</summary>
        public string Dump()
        {
            var parts = Activities.Select(a =>
                $"{a.DisplayName} {a.Source.Name} {string.Join(" ", a.TagObjects.Select(t => $"{t.Key}={t.Value}"))} " +
                string.Join(" ", a.Events.SelectMany(e => e.Tags.Select(t => $"{t.Key}={t.Value}"))));
            return string.Join("\n", parts.Concat(Instruments));
        }

        private void Record<T>(Instrument instrument, T value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            var text = $"{instrument.Name}={value} " + string.Join(" ", tags.ToArray().Select(t => $"{t.Key}={t.Value}"));
            lock (_gate) { _measurements.Add(text); }
        }

        public void Dispose()
        {
            _activities.Dispose();
            _meters.Dispose();
        }
    }
}
