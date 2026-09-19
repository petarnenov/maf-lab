using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Indexing.Corpus;

namespace Maf.Lab.Tests;

public sealed class CorpusLoaderTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("maf-corpus-").FullName;

    [Fact]
    public void Tenant_comes_from_layout_and_unowned_documents_are_rejected_not_defaulted()
    {
        Write("firm-a/docs/a.md", "# A");
        Write("shared/procedures/p.txt", "Title\n1. Step");
        Write("unowned/docs/orphan.md", "# Orphan");
        Write("loose.md", "# Loose");
        Write("firm-a/images/x.md", "# wrong source type");
        Write("firm-a/code/tool.exe", "binary");

        var snapshot = CorpusLoader.Load(_root);

        Assert.Equal(["firm-a/docs/a.md", "shared/procedures/p.txt"], snapshot.Documents.Select(d => d.DocId).Order());
        Assert.DoesNotContain(snapshot.Documents, d => d.RelativePath.Contains("orphan"));
        Assert.Contains(snapshot.Rejected, r => r.Path == "unowned/docs/orphan.md" && r.Reason.Contains("tenant cannot be determined"));
        Assert.Contains(snapshot.Rejected, r => r.Path == "loose.md");
        Assert.Contains(snapshot.Rejected, r => r.Path == "firm-a/images/x.md");
        Assert.Contains(snapshot.Rejected, r => r.Path == "firm-a/code/tool.exe");
    }

    [Fact]
    public void Tenant_filter_limits_documents()
    {
        Write("firm-a/docs/a.md", "# A");
        Write("firm-b/docs/b.md", "# B");
        var snapshot = CorpusLoader.Load(_root, new HashSet<TenantId> { TenantId.Firm("firm-b") });
        Assert.Equal(["firm-b/docs/b.md"], snapshot.Documents.Select(d => d.DocId));
    }

    [Fact]
    public void Real_corpus_rejects_the_orphan_and_has_the_expected_tenants()
    {
        var root = Path.Combine(RepoRoot(), "data");
        var snapshot = CorpusLoader.Load(root);

        Assert.Contains(snapshot.Rejected, r => r.Path == "unowned/docs/orphan-notes.md");
        Assert.Equal(["firm-a", "firm-b", "firm-c", "shared"], snapshot.Documents.Select(d => d.Tenant.Value).Distinct().Order());
        Assert.Equal(50, snapshot.Documents.Count(d => d.Tenant.Value == "firm-c"));
        Assert.True(snapshot.Documents.Count(d => d.Tenant.Value == "firm-b") >= 8 * 50);
    }

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

    private void Write(string relative, string content)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
