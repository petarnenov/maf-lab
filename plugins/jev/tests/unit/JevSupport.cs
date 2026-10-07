using Maf.Lab.Plugins.Abstractions;
using Maf.Lab.Plugins.Jev;
using Maf.Lab.TestSupport;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Maf.Lab.Tests;

/// <summary>
/// The jev plugin's test support: its real engine over the HTTP stand-in for TypeSafe's Jev (<see cref="FakeJev"/>),
/// alone or inside the api with this plugin installed as the decision engine.
/// </summary>
public static class JevSupport
{
    /// <summary>The plugin's manifest, as make would install it.</summary>
    public static PluginManifest Manifest => new()
    {
        Name = JevPlugin.PluginName,
        Kind = PluginKinds.Provider,
        Provides = ProviderKinds.DecisionEngine,
        Environments = ["dev", "qa", "stage", "prod"],
        Description = "TypeSafe Jev as the decision engine.",
        Progress = "None",
        Stopping = "None",
    };

    /// <summary>The client over <paramref name="jev"/>, with the test key.</summary>
    public static JevClient Client(FakeJev jev, JevOptions? options = null, JevCircuitBreaker? breaker = null, string? key = FakeJev.TestKey)
    {
        var loggers = LoggerFactory.Create(_ => { });
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [JevCredential.EnvironmentVariable] = key }).Build();
        var credential = new JevCredential(configuration, loggers.CreateLogger<JevCredential>());
        var client = new HttpClient(new JevAuthHandler(credential) { InnerHandler = jev }) { BaseAddress = new Uri("https://jev.test/") };
        return new JevClient(new OneClient(client), credential, Options.Create(options ?? new JevOptions()), breaker);
    }

    /// <summary>The engine over <paramref name="jev"/>.</summary>
    public static IDecisionEngine Engine(FakeJev jev, JevOptions? options = null) => new JevDecisionEngine(Client(jev, options));

    /// <summary>The api with this plugin installed as its decision engine, Jev answered by the host's <see cref="ApiFactory.Jev"/>.</summary>
    public static ApiFactory Api(ScriptedChatClient chat)
    {
        ApiFactory? api = null;
        api = new ApiFactory(chat)
        {
            UseFakeEngine = false,
            InstalledPlugins = [.. StandInDomains.Installed, Manifest],
            ExtraSettings = new Dictionary<string, string?> { [JevCredential.EnvironmentVariable] = FakeJev.TestKey },
            ConfigureTestServices = s => s.AddHttpClient(JevClient.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => api!.Jev),
        };
        return api;
    }
}

file sealed class OneClient(HttpClient client) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => client;
}
