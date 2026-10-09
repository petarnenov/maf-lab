extern alias service;
using System.Text.Json;
using Maf.Lab.Domain.Evals;
using service::Maf.Lab.Eval.Reports;

namespace Maf.Lab.Tests;

public sealed class ExistingReportTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static string Root() => Directory.CreateTempSubdirectory("maf-accept-report-").FullName;

    private static async Task<string> WriteAsync(string root, bool passed = true, string suite = "selection", double metric = 0.9)
    {
        const string id = "20261009-010000-selection";
        Directory.CreateDirectory(Path.Combine(root, "reports"));
        var variant = new EvalVariantResult("default", new Dictionary<string, double> { ["accuracy"] = metric },
            new Dictionary<string, double> { ["accuracy"] = 0.8 }, passed, 10, []);
        var report = new EvalReport(id, suite, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow,
            new Dictionary<string, string>(), [variant], passed, []);
        await File.WriteAllTextAsync(Path.Combine(root, "reports", id + ".json"), JsonSerializer.Serialize(report, JsonSerializerOptions.Web), Ct);
        return id;
    }

    [Fact]
    public async Task A_passed_existing_report_updates_the_baseline_without_a_host()
    {
        var root = Root();
        var id = await WriteAsync(root);
        await ExistingReport.AcceptAsync(root, id, Ct);
        var accepted = await BaselineStore.ReadAsync(root, Ct);
        Assert.Equal(id, accepted.Suites["selection"].AcceptedFrom);
        Assert.Equal(0.9, accepted.Suites["selection"].Metrics["default"]["accuracy"]);
    }

    [Theory]
    [InlineData(false, "selection", 0.9)]
    [InlineData(true, "selection", 0.2)]
    [InlineData(true, "graph-depth", 0.9)]
    public async Task A_failed_inconsistent_or_comparison_report_cannot_be_accepted(bool passed, string suite, double metric)
    {
        var root = Root();
        var id = await WriteAsync(root, passed, suite, metric);
        await Assert.ThrowsAsync<InvalidDataException>(() => ExistingReport.AcceptAsync(root, id, Ct));
        Assert.False(File.Exists(BaselineStore.PathFor(root)));
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("/tmp/outside")]
    public async Task A_report_id_cannot_select_a_file_outside_reports(string id) =>
        await Assert.ThrowsAsync<InvalidDataException>(() => ExistingReport.AcceptAsync(Root(), id, Ct));
}
