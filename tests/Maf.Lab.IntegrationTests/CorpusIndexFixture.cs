using System.Diagnostics;
using Maf.Lab.Indexing.Pipeline;
using Microsoft.Extensions.DependencyInjection;
using Qdrant.Client.Grpc;

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
        await SettledAsync();
    }

    /// <summary>
    /// Waits until Qdrant has finished optimizing the new collection (stabilize-corpus-fixture). The corpus has many
    /// exactly tied BM25 scores, and which tied chunk makes a prefetch limit depends on the segment layout, which the
    /// optimizer keeps changing for a while after indexing. On a slow CI runner two identical queries in one test could
    /// otherwise straddle a merge and disagree.
    /// </summary>
    private async Task SettledAsync()
    {
        using var client = qdrant.RawClient();
        var clock = Stopwatch.StartNew();
        CollectionStatus? first = null;
        while (true)
        {
            var info = await client.GetCollectionInfoAsync(Collection);
            first ??= info.Status;
            if (info.Status == CollectionStatus.Green && info.OptimizerStatus.Ok)
            {
                TestContext.Current.SendDiagnosticMessage(
                    $"corpus collection settled after {clock.ElapsedMilliseconds} ms (first status {first})");
                return;
            }
            if (clock.Elapsed > TimeSpan.FromSeconds(120))
            {
                throw new TimeoutException(
                    $"Qdrant did not finish optimizing {Collection} within 120 s (status {info.Status}, optimizer {info.OptimizerStatus})");
            }
            await Task.Delay(200);
        }
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
