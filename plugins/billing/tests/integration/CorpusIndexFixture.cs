using Maf.Lab.TestSupport;
using Maf.Lab.Indexing.Pipeline;
using Microsoft.Extensions.DependencyInjection;

namespace Maf.Lab.IntegrationTests;

/// <summary>Billing's corpus (files/corpus/) indexed once with the fake embedder, shared by acceptance and MCP tests.</summary>
public sealed class CorpusIndexFixture(QdrantFixture qdrant) : IAsyncLifetime
{
    public string Collection { get; } = $"corpus_{Guid.NewGuid():N}";
    public string CorpusRoot { get; } = Path.Combine(Repo.Root(), "plugins", "billing", "files", "corpus");
    public QdrantFixture Qdrant => qdrant;

    public async ValueTask InitializeAsync()
    {
        await using var services = qdrant.Services(Collection, CorpusRoot);
        await services.GetRequiredService<IndexingPipeline>().RunAsync(new IndexRequest(), CancellationToken.None);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    /// <summary>
    /// The domains an agent's tool source contacts servers for: billing as its manifest describes it (the search, and the
    /// fee adjustment that needs a reviewer), beside the built-in ones.
    /// </summary>
    public static Maf.Lab.Api.Agent.DomainCatalogue Domains { get; } = Maf.Lab.Api.Agent.DomainCatalogue.Of(
    [
        new Maf.Lab.Plugins.Abstractions.DomainTable
        {
            Id = "billing", Order = 10, SearchTool = "search_documents",
            ToolRequires = new Dictionary<string, string> { ["propose_fee_adjustment"] = "compliance" },
        }.ToDescriptor(),
        .. Maf.Lab.Api.Agent.DomainCatalogue.AllBuiltIn.All,
    ]);

    /// <summary>The billing server's settings over this collection, with the plugin's own seeds.</summary>
    public Dictionary<string, string?> Config()
    {
        var values = qdrant.Config(Collection, CorpusRoot);
        values["Billing:SeedPath"] = Path.Combine(Repo.Root(), "plugins", "billing", "files", "seed", "billing-runs.json");
        values["Billing:AccountsSeedPath"] = Path.Combine(Repo.Root(), "plugins", "billing", "files", "seed", "billing-accounts.json");
        return values;
    }
}

[CollectionDefinition(Name)]
public sealed class CorpusCollection : ICollectionFixture<CorpusIndexFixture>
{
    public const string Name = "indexed-corpus";
}
