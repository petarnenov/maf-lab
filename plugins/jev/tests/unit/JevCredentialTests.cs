using Maf.Lab.Plugins.Jev;
using Maf.Lab.TestSupport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Maf.Lab.Tests;

/// <summary>The key is read from JEV_MAF_LAB alone; without one the engine sends nothing and says so once, never the key.</summary>
public class JevCredentialTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Without_a_key_nothing_is_sent_and_the_missing_key_is_reported_once(string? key)
    {
        var logs = new CapturingLoggerProvider();
        var loggers = LoggerFactory.Create(b => b.AddProvider(logs).SetMinimumLevel(LogLevel.Trace));
        var credential = new JevCredential(
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [JevCredential.EnvironmentVariable] = key }).Build(),
            loggers.CreateLogger<JevCredential>());
        var jev = new FakeJev();
        var engine = new JevDecisionEngine(JevSupport.Client(jev, key: key));

        var first = await engine.DecideAsync(new { text = "a" }, new Dictionary<string, Maf.Lab.Plugins.Abstractions.DecisionQuestion>
        {
            ["q"] = new Maf.Lab.Plugins.Abstractions.NoulQuestion("Is `text` about fees?"),
        }, TimeSpan.FromSeconds(1), Ct);

        Assert.False(credential.IsConfigured);
        Assert.False(engine.IsConfigured);
        Assert.Empty(jev.Requests);
        Assert.Equal("no key", first.Failure);
        var warning = Assert.Single(logs.Messages, m => m.Contains(JevCredential.EnvironmentVariable));
        Assert.Contains("not set", warning);
    }
}
