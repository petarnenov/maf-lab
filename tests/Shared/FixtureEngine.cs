using Maf.Lab.Plugins.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Maf.Lab.Retrieval.Configuration;
using Maf.Lab.Retrieval.Models;
using Microsoft.Extensions.AI;

namespace Maf.Lab.TestSupport;

/// <summary>
/// The test hosts' decision-engine provider (introduce-provider-plugins): a provider plugin of the test assembly (kind
/// provider, provides decision-engine), so a test host satisfies "exactly one decision engine" without any engine's
/// plugin. Its engine has no credential, so nothing is ever asked — as a test host without a key was before; a test that
/// scripts answers replaces it (the api's test host registers its own).
/// </summary>
public sealed class FixtureEnginePlugin : IMafPlugin, IContributesProvider
{
    public const string PluginName = "fixture-engine";

    public string Name => PluginName;

    public string Provides => ProviderKinds.DecisionEngine;

    public void ConfigureProvider(IServiceCollection services, IConfiguration configuration) =>
        services.TryAddSingleton<IDecisionEngine, UnconfiguredDecisionEngine>();

    /// <summary>Its manifest, as make would install it.</summary>
    public static PluginManifest Manifest => new()
    {
        Name = PluginName,
        Kind = PluginKinds.Provider,
        Provides = ProviderKinds.DecisionEngine,
        Environments = ["dev", "qa", "stage", "prod"],
        Description = "The test hosts' decision engine.",
        Progress = "None",
        Stopping = "None",
    };
}

/// <summary>An engine with no credential: every caller skips its request and says "no key".</summary>
public sealed class UnconfiguredDecisionEngine : IDecisionEngine
{
    public string Engine => "fixture-engine";

    public bool IsConfigured => false;

    public Task<DecisionOutcome> DecideAsync(object state, IReadOnlyDictionary<string, DecisionQuestion> questions, TimeSpan budget,
        CancellationToken ct) => Task.FromResult(new DecisionOutcome(null, Engine, null, DecisionFailures.NoKey, 0));
}

/// <summary>The core tests' chat provider: a scripted client, independent of every bundled provider.</summary>
public sealed class FixtureChatPlugin : IMafPlugin, IContributesProvider, IChatModelProvider
{
    public const string PluginName = "fixture-chat";
    public string Name => PluginName;
    public string Provides => ProviderKinds.ChatModel;
    public void ConfigureProvider(IServiceCollection services, IConfiguration configuration) =>
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IChatModelProvider, FixtureChatPlugin>());
    public IChatClient CreateChatClient(string? model = null) => new ScriptedChatClient((_, _, _) => [new ChatResponseUpdate(ChatRole.Assistant, "fixture")]);
    public ChatOptions BaseChatOptions() => new() { Temperature = 0 };
    public static PluginManifest Manifest => new()
    {
        Name = PluginName, Kind = PluginKinds.Provider, Provides = ProviderKinds.ChatModel,
        Environments = ["dev", "qa", "stage", "prod"], Description = "The test hosts' chat model.", Progress = "None", Stopping = "None",
    };
}

/// <summary>The core tests' embeddings provider; a test that embeds supplies its own dense encoder.</summary>
public sealed class FixtureEmbeddingsPlugin : IMafPlugin, IContributesProvider, IEmbeddingsProvider
{
    public const string PluginName = "fixture-embeddings";
    public string Name => PluginName;
    public string Provides => ProviderKinds.Embeddings;
    public void ConfigureProvider(IServiceCollection services, IConfiguration configuration) =>
        services.TryAddSingleton<IEmbeddingsProvider, FixtureEmbeddingsPlugin>();
    public IEmbeddingGenerator<string, Embedding<float>> CreateGenerator(EmbeddingProfile profile, EmbeddingRole role) =>
        throw new InvalidOperationException("A test that embeds must supply its own encoder.");
    public static PluginManifest Manifest => new()
    {
        Name = PluginName, Kind = PluginKinds.Provider, Provides = ProviderKinds.Embeddings,
        Environments = ["dev", "qa", "stage", "prod"], Description = "The test hosts' embeddings.", Progress = "None", Stopping = "None",
    };
}
