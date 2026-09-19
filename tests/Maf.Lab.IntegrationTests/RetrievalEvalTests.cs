using System.Text.Json.Nodes;
using Maf.Lab.Api.Endpoints;
using Maf.Lab.Api.Feedback;
using Maf.Lab.Api.Storage;
using Maf.Lab.Domain.Feedback;
using Maf.Lab.Eval;
using Maf.Lab.Eval.Suites;
using Maf.Lab.Retrieval.Search;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Maf.Lab.IntegrationTests;

[Collection(CorpusCollection.Name)]
public class RetrievalEvalTests(CorpusIndexFixture corpus)
{
    [Fact]
    public async Task Report_has_hybrid_dense_and_sparse_and_includes_a_freshly_labeled_feedback_row()
    {
        // A copy of the repo datasets, plus the row a reviewer creates after "wrong document" feedback.
        var root = Directory.CreateTempSubdirectory("maf-evals-").FullName;
        foreach (var file in Directory.EnumerateFiles(Path.Combine(CorpusIndexFixture.RepoRoot(), "evals"), "*.jsonl"))
        {
            File.Copy(file, Path.Combine(root, Path.GetFileName(file)));
        }
        var turn = new TurnRow { Id = "t_feedback", ConversationId = "c", UserId = "adam", FirmId = "firm-a", Question = "how do I void an invoice and issue a new one" };
        var (row, _) = FeedbackEndpoints.BuildRow(turn, new LabelRequest(EvalDataset.Retrieval, null,
            ["shared/procedures/void-and-reissue-invoice.txt#voiding-and-reissuing-an-invoice"], null, null));
        var writer = new DatasetWriter(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Evals:Root"] = root }).Build());
        Assert.True(await writer.AppendAsync(EvalDataset.Retrieval, row!, TestContext.Current.CancellationToken));

        await using var services = corpus.Qdrant.Services(corpus.Collection, corpus.CorpusRoot);
        var search = services.GetRequiredService<DocumentSearchService>();
        var options = new EvalOptions { Thresholds = new() { ["retrieval"] = new() { ["recall@5"] = 1.01 } } };
        var ctx = new SuiteContext(root, options, null, _ => { });

        var variants = await new RetrievalSuite().RunAsync(ctx, RetrievalSuite.DefaultVariants(search, "dense_v1", rerank: false), TestContext.Current.CancellationToken);

        Assert.Equal(["hybrid", "hybrid-dbsf", "dense", "sparse"], variants.Select(v => v.Name));
        var expectedCases = Maf.Lab.Eval.Datasets.DatasetLoader.Retrieval(root).Count;
        Assert.Contains(Maf.Lab.Eval.Datasets.DatasetLoader.Retrieval(root), r => r.Id == "fb-retrieval-t_feedback");
        Assert.All(variants, v => Assert.Equal(expectedCases, v.Cases));
        Assert.All(variants, v => Assert.InRange(v.Metrics["recall@20"], 0, 1));
        var hybrid = variants[0];
        Assert.False(hybrid.Passed);                 // threshold above achievable → failed
        Assert.All(variants.Skip(1), v => Assert.True(v.Passed)); // comparison variants carry no thresholds
    }
}
