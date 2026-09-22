using Maf.Lab.Eval.Datasets;

namespace Maf.Lab.Tests;

/// <summary>
/// A retrieval row with no relevant chunks is either a question the corpus cannot answer or a row somebody
/// forgot to label. The marker is what tells them apart, and an unmarked one must not be read as the first.
/// </summary>
public class OffDomainCaseTests
{
    private static string Dataset(params string[] rows)
    {
        var root = Directory.CreateTempSubdirectory("maf-offdomain-").FullName;
        File.WriteAllLines(Path.Combine(root, "retrieval.jsonl"), rows);
        return root;
    }

    [Fact]
    public void A_marked_row_may_have_no_relevant_chunks()
    {
        var cases = DatasetLoader.Retrieval(Dataset("""{"id":"of-x","query":"what is JWE","relevantChunkIds":[],"offDomain":true}"""));

        var only = Assert.Single(cases);
        Assert.True(only.OffDomain);
        Assert.Empty(only.RelevantChunkIds);
    }

    [Fact]
    public void An_unmarked_row_with_no_relevant_chunks_is_rejected()
    {
        var root = Dataset("""{"id":"r-x","query":"what is JWE","relevantChunkIds":[]}""");

        var error = Assert.Throws<InvalidDataException>(() => DatasetLoader.Retrieval(root));
        Assert.Contains("relevantChunkIds", error.Message);
    }

    [Fact]
    public void A_marked_row_must_not_claim_relevant_chunks()
    {
        var root = Dataset("""{"id":"of-y","query":"what is JWE","relevantChunkIds":["shared/docs/a.md#x"],"offDomain":true}""");

        var error = Assert.Throws<InvalidDataException>(() => DatasetLoader.Retrieval(root));
        Assert.Contains("off-domain", error.Message);
    }

    [Fact]
    public void An_ordinary_row_is_unchanged()
    {
        var cases = DatasetLoader.Retrieval(Dataset("""{"id":"r-x","query":"missing fee schedule","relevantChunkIds":["shared/procedures/a.txt#x"],"language":"en"}"""));

        var only = Assert.Single(cases);
        Assert.False(only.OffDomain);
        Assert.Equal("en", only.Language);
    }

    [Fact]
    public void The_repository_dataset_holds_off_domain_cases()
    {
        var root = Path.Combine(RepoRoot(), "evals");

        var cases = DatasetLoader.Retrieval(root);

        Assert.Contains(cases, c => c.OffDomain);
        Assert.All(cases.Where(c => c.OffDomain), c => Assert.Empty(c.RelevantChunkIds));
        Assert.All(cases.Where(c => !c.OffDomain), c => Assert.NotEmpty(c.RelevantChunkIds));
    }

    private static string RepoRoot()
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
