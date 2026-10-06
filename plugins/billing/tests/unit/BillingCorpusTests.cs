using Maf.Lab.Eval.Datasets;
using Maf.Lab.Indexing.Chunking;
using Maf.Lab.Indexing.Corpus;
using Maf.Lab.Plugins.Billing;

namespace Maf.Lab.Tests;

/// <summary>Billing's corpus (files/corpus/): its layout, and the retrieval eval's rows that point into it.</summary>
public class BillingCorpusTests
{
    [Fact]
    public void Real_corpus_rejects_the_orphan_and_has_the_expected_tenants()
    {
        var root = Path.Combine(BillingPluginSupport.Folder, "files", "corpus");
        var snapshot = CorpusLoader.Load(root);

        Assert.Contains(snapshot.Rejected, r => r.Path == "unowned/docs/orphan-notes.md");
        Assert.Equal(["firm-a", "firm-b", "firm-c", "shared"], snapshot.Documents.Select(d => d.Tenant.Value).Distinct().Order());
        Assert.Equal(50, snapshot.Documents.Count(d => d.Tenant.Value == "firm-c"));
        Assert.True(snapshot.Documents.Count(d => d.Tenant.Value == "firm-b") >= 8 * 50);
    }

    [Fact]
    public void Retrieval_dataset_references_chunks_the_chunkers_actually_produce()
    {
        // Each domain's rows against its own corpus: billing's from this folder, a portfolio row's from data-portfolio/.
        HashSet<string> Ids(string corpus) => CorpusLoader.Load(Path.Combine(CorpusLoaderTests.RepoRoot(), corpus)).Documents
            .SelectMany(d => ChunkBuilder.Build(d, 1500)).Select(c => c.ChunkId).ToHashSet();
        var byDomain = new Dictionary<string, HashSet<string>>
        {
            ["billing"] = Ids(Path.Combine("plugins", BillingPlugin.PluginName, "files", "corpus")),
            ["portfolio"] = Ids("data-portfolio"),
        };
        var missing = DatasetLoader.Retrieval(Path.Combine(CorpusLoaderTests.RepoRoot(), "evals"))
            .SelectMany(r => r.RelevantChunkIds.Where(id => !byDomain[r.Domain].Contains(id)).Select(id => $"{r.Domain}:{id}")).ToList();
        Assert.True(missing.Count == 0, "Dataset references unknown chunk ids (did chunking change?): " + string.Join(", ", missing));
    }
}
