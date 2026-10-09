using Maf.Lab.Plugins.Code;
using Maf.Lab.Eval.Datasets;
namespace Maf.Lab.Tests;
public sealed class CodeRouteCorpusLabelsTests
{
    [Fact]
    public void Structural_labels_match_the_production_argument_parser()
    {
        var cases = DatasetLoader.CodeRoute(Path.Combine(CorpusLoaderTests.RepoRoot(), "evals"));
        foreach (var c in cases.Where(c => c.Expected is "callers" or "callees" or "impact"))
        {
            var found = c.Expected == "impact" ? CodeToolRouter.Paths(c.Question).Count : CodeToolRouter.Symbols(c.Question).Count;
            Assert.True((found == 1) == c.HasArgument, c.Id);
        }
    }
}
