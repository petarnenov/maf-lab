using System.Diagnostics.Metrics;
using Maf.Lab.Api.Agent;
using Maf.Lab.Api.Agent.Decisions;
using Maf.Lab.Api.BuiltIn;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Maf.Lab.Tests;

/// <summary>Each decision request's input tokens are measured by call site and by the number of domains in use (5k).</summary>
public class DecisionMetricsTests
{
    [Fact]
    public async Task The_turns_request_records_its_input_tokens_with_the_domains_in_use()
    {
        using var domains = DomainCatalogue.Use(StandInDomains.WithBilling);
        var seen = new List<(long Tokens, Dictionary<string, object?> Tags)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Name == "maf.decision.input_tokens")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            var copy = new Dictionary<string, object?>();
            foreach (var tag in tags)
            {
                copy[tag.Key] = tag.Value;
            }
            lock (seen)
            {
                seen.Add((value, copy));
            }
        });
        listener.Start();
        var options = Options.Create(new IntentOptions());
        var classifier = new DecisionIntentClassifier(new FakeDecisionEngine(new FakeJev()), options, LoggerFactory.Create(_ => { }));

        await classifier.ClassifyAsync("what is the procedure when a fee schedule is missing", TestContext.Current.CancellationToken);

        lock (seen)
        {
            // Other tests record on the same meter in parallel: this request's measurement is among them.
            Assert.Contains(seen, m => m.Tokens == 400 && (string?)m.Tags["call_site"] == "intent"
                && m.Tags.TryGetValue("domains.count", out var n) && (int)n! == StandInDomains.WithBilling.All.Count);
        }
    }
}
