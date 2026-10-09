using Maf.Lab.Plugins.Abstractions;
using Maf.Lab.Retrieval.Configuration;
using Maf.Lab.Retrieval.Models;
using Maf.Lab.TestSupport;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Maf.Lab.Tests;

/// <summary>The core consumes only MEAI clients, selects a named chat provider and preserves profile, purpose and stop.</summary>
public sealed class ModelProviderContractTests
{
    [Fact]
    public void Multiple_chat_providers_select_only_the_named_one_and_keep_model_overrides()
    {
        var first = new ChatProvider("first");
        var selected = new ChatProvider("selected");
        using var models = new ModelProviders(Options.Create(new ModelOptions()), [first, selected], [],
            Options.Create(new InstalledProviders { ChatModel = "selected" }));

        using var client = models.CreateChatClient("task-model");
        Assert.Equal("task-model", selected.Model);
        Assert.Null(first.Model);
        Assert.Same(selected.Client, client);
        Assert.Equal(0, models.BaseChatOptions().Temperature);
    }

    [Fact]
    public async Task Embeddings_keep_prefixes_order_purpose_and_cancellation_through_the_standard_interface()
    {
        var embedding = new EmbeddingsProvider();
        using var models = new ModelProviders(Options.Create(new ModelOptions()), [], [embedding], Options.Create(new InstalledProviders()));
        var encoder = new DenseEncoder(models);
        using var stop = new CancellationTokenSource();
        await encoder.EmbedQueryAsync("dense_v3", "query", stop.Token);
        await encoder.EmbedDocumentsAsync("dense_v3", ["first", "second"], stop.Token);
        await encoder.EmbedQueryAsync("dense_v3", "another", stop.Token);

        Assert.Equal(2, embedding.Generators.Count);
        var query = embedding.Generators[EmbeddingRole.Query];
        var documents = embedding.Generators[EmbeddingRole.Documents];
        Assert.Equal(["task: search result | query: another"], query.Inputs);
        Assert.Equal(["title: none | text: first", "title: none | text: second"], documents.Inputs);
        Assert.Equal(stop.Token, query.Token);
        Assert.Equal(stop.Token, documents.Token);
        Assert.Equal("embeddinggemma", encoder.ModelVersion("dense_v3"));
    }

    [Theory]
    [InlineData("missing", "chat provider 'missing'")]
    [InlineData("fixture-engine", "chat-model")]
    public void A_missing_or_wrong_chat_provider_fails_at_start(string selected, string problem)
    {
        var settings = new Dictionary<string, string?>(TestProviders.FixtureEngineSettings) { ["MAF_CHAT_MODEL"] = selected };
        using var services = new ServiceCollection().AddInstalledProviders(new ConfigurationBuilder().AddInMemoryCollection(settings).Build())
            .BuildServiceProvider();

        var failure = Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<IOptions<InstalledProviders>>().Value);
        Assert.Contains(problem, failure.Message);
    }

    [Fact]
    public void The_fixture_core_providers_pass_without_any_bundled_provider()
    {
        using var services = new ServiceCollection().AddInstalledProviders(
            new ConfigurationBuilder().AddInMemoryCollection(TestProviders.FixtureEngineSettings).Build()).BuildServiceProvider();
        Assert.Null(services.GetRequiredService<IOptions<InstalledProviders>>().Value.Problem);
        Assert.Single(services.GetServices<IChatModelProvider>());
        Assert.Single(services.GetServices<IEmbeddingsProvider>());
    }

    private sealed class ChatProvider(string name) : IChatModelProvider
    {
        public string Name => name;
        public string? Model { get; private set; }
        public IChatClient Client { get; } = new ScriptedChatClient((_, _, _) => ScriptedChatClient.Text("fixture"));
        public IChatClient CreateChatClient(string? model = null)
        {
            Model = model;
            return Client;
        }
        public ChatOptions BaseChatOptions() => new() { Temperature = 0 };
    }

    private sealed class EmbeddingsProvider : IEmbeddingsProvider
    {
        public Dictionary<EmbeddingRole, Generator> Generators { get; } = [];
        public IEmbeddingGenerator<string, Embedding<float>> CreateGenerator(EmbeddingProfile profile, EmbeddingRole role) =>
            Generators[role] = new Generator();
    }

    private sealed class Generator : IEmbeddingGenerator<string, Embedding<float>>
    {
        public string[] Inputs { get; private set; } = [];
        public CancellationToken Token { get; private set; }
        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(IEnumerable<string> values, EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Inputs = values.ToArray();
            Token = cancellationToken;
            return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(Inputs.Select(_ => new Embedding<float>(new float[] { 1, 2 }))));
        }
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
