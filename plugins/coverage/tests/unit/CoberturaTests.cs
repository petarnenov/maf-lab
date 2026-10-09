using System.Text;
using Maf.Lab.Plugins.Coverage;
using Maf.Lab.TestGen.Coverage;

namespace Maf.Lab.Tests;

/// <summary>Reading Cobertura from both toolchains into one model, and naming its files (coverage-ingestion).</summary>
public sealed class CoberturaTests
{
    private static readonly IReadOnlySet<string> Repo = new HashSet<string>
    {
        "src/Maf.Lab.Api/Coverage/CoverageTree.cs",
        "src/Maf.Lab.Api/Coverage/CoverageStore.cs",
        "web/src/coverage/CoveragePage.tsx",
        "web/src/coverage/CoveragePage.test.tsx",
    };

    private static RawCoverageReport Fixture(string name)
    {
        using var file = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Coverage", name));
        return CoberturaParser.Parse(file);
    }

    private static RawCoverageReport ParseText(string xml) => CoberturaParser.Parse(new MemoryStream(Encoding.UTF8.GetBytes(xml)));

    [Fact]
    public void A_dotnet_report_is_merged_per_file_and_line()
    {
        var report = Fixture("dotnet.cobertura.xml");

        var tree = report.Files.Single(f => f.Path.EndsWith("CoverageTree.cs"));
        Assert.Equal([10, 11, 12, 20], tree.Lines.Select(l => l.Line));
        // Line 11 appears in two classes: the most hits and the most branches taken win.
        Assert.Equal(new LineCoverage(11, 6, 2, 2), tree.Lines.Single(l => l.Line == 11));
        Assert.Equal(3, tree.LinesCovered);
        Assert.Equal(4, tree.LinesTotal);
        Assert.Equal(75.0, tree.LinePct);
    }

    [Fact]
    public void A_partial_branch_line_is_partial()
    {
        var page = Fixture("vitest.cobertura.xml").Files.Single(f => f.Path == "src/coverage/CoveragePage.tsx");

        var statuses = page.Lines.ToDictionary(l => l.Line, LineStatus.Of);
        Assert.Equal(LineStatus.Covered, statuses[5]);
        Assert.Equal(LineStatus.Partial, statuses[6]);
        Assert.Equal(LineStatus.Uncovered, statuses[9]);
        Assert.Equal((1, 2), (page.Lines[1].BranchesCovered, page.Lines[1].BranchesTotal));
    }

    [Fact]
    public void The_vitest_doctype_is_ignored_not_fetched()
    {
        var report = Fixture("vitest.cobertura.xml");

        Assert.Equal(["/work/r_1/web"], report.Sources);
    }

    [Theory]
    [InlineData("not xml at all")]
    [InlineData("<coverage><sources/></coverage>")]
    [InlineData("<report><packages/></report>")]
    [InlineData("<coverage><packages><package><classes><class filename=\"a.cs\"><lines><line number=\"x\" hits=\"1\"/></lines></class></classes></package></packages></coverage>")]
    public void A_malformed_report_is_rejected(string xml)
    {
        var ex = Assert.Throws<CoberturaFormatException>(() => ParseText(xml));
        Assert.DoesNotContain(xml, ex.Message);
    }

    [Fact]
    public void A_dotnet_path_from_inside_a_container_becomes_repo_relative()
    {
        var normalised = CoveragePaths.Normalise(Fixture("dotnet.cobertura.xml"), measuredRoot: null, Repo);

        Assert.Equal(["src/Maf.Lab.Api/Coverage/CoverageTree.cs"], normalised.Files.Select(f => f.Path));
        // /opt/elsewhere/Other.cs names nothing in the repository.
        Assert.Equal(1, normalised.Dropped);
    }

    [Fact]
    public void A_host_path_under_the_measured_root_is_stripped()
    {
        var raw = new RawCoverageReport([], [new FileCoverage("/Users/me/maf-lab/src/Maf.Lab.Api/Coverage/CoverageStore.cs", [])]);

        var normalised = CoveragePaths.Normalise(raw, "/Users/me/maf-lab", Repo);

        Assert.Equal("src/Maf.Lab.Api/Coverage/CoverageStore.cs", normalised.Files.Single().Path);
    }

    [Fact]
    public void A_vitest_path_relative_to_its_source_is_resolved_and_test_files_are_not_targets()
    {
        var normalised = CoveragePaths.Normalise(Fixture("vitest.cobertura.xml"), measuredRoot: "/work/r_1", Repo);

        Assert.Equal(["web/src/coverage/CoveragePage.tsx"], normalised.Files.Select(f => f.Path));
        Assert.Equal(0, normalised.Dropped);
    }

    [Fact]
    public void A_source_relative_to_the_repo_is_combined()
    {
        var raw = new RawCoverageReport(["web/src"], [new FileCoverage("coverage/CoveragePage.tsx", [])]);

        Assert.Equal("web/src/coverage/CoveragePage.tsx", CoveragePaths.Normalise(raw, null, Repo).Files.Single().Path);
    }

    [Theory]
    [InlineData("/etc/passwd")]
    [InlineData("../../etc/passwd")]
    [InlineData("/src/src/../../etc/src/Maf.Lab.Api/Coverage/CoverageTree.cs")]
    [InlineData("src/Maf.Lab.Api/../../../outside.cs")]
    public void Paths_outside_the_repository_are_dropped(string name)
    {
        var raw = new RawCoverageReport([], [new FileCoverage(name, [])]);

        var normalised = CoveragePaths.Normalise(raw, "/src", Repo);

        Assert.Empty(normalised.Files);
        Assert.Equal(1, normalised.Dropped);
    }

    [Theory]
    [InlineData("src/Maf.Lab.Api/Program.cs", true)]
    [InlineData("src/Maf.Lab.Api/obj/Debug/X.g.cs", false)]
    [InlineData("tests/Maf.Lab.Tests/CoberturaTests.cs", false)]
    [InlineData("web/src/App.tsx", true)]
    [InlineData("web/src/App.test.tsx", false)]
    [InlineData("web/src/test/setup.ts", false)]
    [InlineData("web/src/vite-env.d.ts", false)]
    public void Only_production_source_is_a_target(string path, bool target) => Assert.Equal(target, CoveragePaths.IsTarget(path));
}
