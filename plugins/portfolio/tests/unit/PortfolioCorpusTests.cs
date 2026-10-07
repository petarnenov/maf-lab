using Maf.Lab.Eval.Datasets;
using Maf.Lab.Indexing.Chunking;
using Maf.Lab.Indexing.Corpus;
using Maf.Lab.Plugins.Portfolio;

namespace Maf.Lab.Tests;

/// <summary>Portfolio's corpus (files/corpus/): the retrieval eval's rows that point into it.</summary>
public class PortfolioCorpusTests
{
    [Fact]
    public void Retrieval_dataset_references_chunks_the_chunkers_actually_produce()
    {
        var ids = CorpusLoader.Load(Path.Combine(PortfolioPluginSupport.Folder, "files", "corpus")).Documents
            .SelectMany(d => ChunkBuilder.Build(d, 1500)).Select(c => c.ChunkId).ToHashSet();
        var missing = DatasetLoader.Retrieval(Path.Combine(CorpusLoaderTests.RepoRoot(), "evals"))
            .Where(r => r.Domain == PortfolioPlugin.DomainId)
            .SelectMany(r => r.RelevantChunkIds.Where(id => !ids.Contains(id))).ToList();
        Assert.True(missing.Count == 0, "Dataset references unknown chunk ids (did chunking change?): " + string.Join(", ", missing));
    }
}
