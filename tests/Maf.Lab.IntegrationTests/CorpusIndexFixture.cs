using Maf.Lab.Indexing.Pipeline;
using Microsoft.Extensions.DependencyInjection;

namespace Maf.Lab.IntegrationTests;

/// <summary>The real sample corpus (data/) indexed once with the fake embedder, shared by acceptance and MCP tests.</summary>
public sealed class CorpusIndexFixture(QdrantFixture qdrant) : IAsyncLifetime
{
    public string Collection { get; } = $"corpus_{Guid.NewGuid():N}";
    public string CorpusRoot { get; } = Path.Combine(RepoRoot(), "data");
    public QdrantFixture Qdrant => qdrant;

    public async ValueTask InitializeAsync()
    {
        await using var services = qdrant.Services(Collection, CorpusRoot);
        await services.GetRequiredService<IndexingPipeline>().RunAsync(new IndexRequest(), CancellationToken.None);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "maf-lab.sln")))
            {
                return dir.FullName;
            }
        }
        throw new InvalidOperationException("repo root not found");
    }
}

[CollectionDefinition(Name)]
public sealed class CorpusCollection : ICollectionFixture<CorpusIndexFixture>
{
    public const string Name = "indexed-corpus";
}
