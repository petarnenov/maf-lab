using System.Text.Json;
using Maf.Lab.Domain.Admin;

namespace Maf.Lab.Tests;

/// <summary>
/// The admin contracts: what an operator's screen is told about the index. They are records, so their behaviour
/// is value equality, with-expressions, deconstruction and the JSON shape the API serializes.
/// </summary>
public class AdminContractsTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset End = new(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_drift_report_is_equal_to_a_copy_and_differs_by_its_numbers()
    {
        var stale = new List<StaleDocument> { new("doc-1", "docs/fee.md", Start, End) };
        var report = new DriftReport(10, 1, 10.0, stale, ["docs/missing.md"]);

        Assert.Equal(report, report with { });
        Assert.Equal(10, report.TotalDocuments);
        Assert.Equal(1, report.StaleDocuments);
        Assert.Equal(10.0, report.StalePercent);
        Assert.Equal("docs/missing.md", report.MissingFromIndex.Single());
        Assert.NotEqual(report, report with { StalePercent = 20.0 });
        Assert.NotEqual(report, report with { MissingFromIndex = [] });
    }

    [Fact]
    public void A_drift_report_deconstructs_into_its_counts_and_documents()
    {
        var staleDoc = new StaleDocument("doc-1", "docs/fee.md", Start, End);
        var report = new DriftReport(2, 1, 50.0, [staleDoc], ["docs/gone.md"]);

        var (total, staleCount, percent, stale, missing) = report;

        Assert.Equal(2, total);
        Assert.Equal(1, staleCount);
        Assert.Equal(50.0, percent);
        Assert.Equal([staleDoc], stale);
        Assert.Equal(["docs/gone.md"], missing);
    }

    [Fact]
    public void A_stale_document_names_what_is_out_of_date_and_by_when()
    {
        var doc = new StaleDocument("doc-1", "docs/fee.md", Start, End);

        Assert.Equal(doc, doc with { });
        Assert.Equal(("doc-1", "docs/fee.md", Start, End), (doc.DocId, doc.SourcePath, doc.SourceUpdatedAt, doc.IndexedUpdatedAt));
        Assert.NotEqual(doc, doc with { IndexedUpdatedAt = Start });
        Assert.NotEqual(doc, new StaleDocument("doc-2", "docs/fee.md", Start, End));
    }

    [Fact]
    public void A_model_version_count_carries_its_chunk_total()
    {
        var count = new ModelVersionCount("v2", 42);

        Assert.Equal(count, count with { });
        Assert.Equal("v2", count.ModelVersion);
        Assert.Equal(42, count.Chunks);
        Assert.NotEqual(count, new ModelVersionCount("v1", 42));
        Assert.NotEqual(count, new ModelVersionCount("v2", 41));
    }

    [Fact]
    public void An_index_status_reports_its_versions_vector_and_current_job()
    {
        var job = new AdminJob("job-1", "reindex", AdminJobStates.Running, Start, null, null);
        var status = new IndexStatus([new ModelVersionCount("v2", 42)], "dense-v2", job);

        Assert.Equal(status, status with { });
        Assert.Equal("dense-v2", status.ActiveDenseVector);
        Assert.Equal(job, status.CurrentJob);
        Assert.Equal("v2", status.ModelVersions.Single().ModelVersion);
        Assert.NotEqual(status, status with { CurrentJob = null });
        Assert.NotEqual(status, status with { ActiveDenseVector = "dense-v1" });
    }

    [Fact]
    public void An_index_run_summary_counts_what_was_indexed_changed_and_refused()
    {
        var rejected = new RejectedDocument("docs/broken.md", "unreadable");
        var summary = new IndexRunSummary(9, 3, 120, 2, [rejected]);

        Assert.Equal(summary, summary with { });
        Assert.Equal(9, summary.DocumentsIndexed);
        Assert.Equal(3, summary.DocumentsUnchanged);
        Assert.Equal(120, summary.ChunksWritten);
        Assert.Equal(2, summary.ChunksDeleted);
        Assert.Equal(rejected, summary.Rejected.Single());
        Assert.NotEqual(summary, summary with { ChunksDeleted = 0 });
        Assert.NotEqual(summary, summary with { Rejected = [] });
    }

    [Fact]
    public void A_rejected_document_says_which_path_and_why()
    {
        var rejected = new RejectedDocument("docs/broken.md", "unreadable");

        Assert.Equal(rejected, rejected with { });
        Assert.Equal(("docs/broken.md", "unreadable"), (rejected.Path, rejected.Reason));
        Assert.NotEqual(rejected, rejected with { Reason = "empty" });
    }

    [Fact]
    public void A_migration_summary_says_what_moved_and_what_was_already_current()
    {
        var summary = new MigrationSummary("v2", 7, 3);

        Assert.Equal(summary, summary with { });
        Assert.Equal("v2", summary.TargetModelVersion);
        Assert.Equal(7, summary.Migrated);
        Assert.Equal(3, summary.AlreadyCurrent);
        Assert.NotEqual(summary, summary with { Migrated = 8 });
    }

    [Fact]
    public void The_contracts_serialize_as_camel_case_json_and_round_trip()
    {
        var report = new DriftReport(2, 1, 50.0, [new StaleDocument("doc-1", "docs/fee.md", Start, End)], ["docs/gone.md"]);
        var status = new IndexStatus([new ModelVersionCount("v2", 42)], "dense-v2",
            new AdminJob("job-1", "reindex", AdminJobStates.Running, Start, null, null));

        var reportJson = JsonSerializer.Serialize(report, Json);
        var statusJson = JsonSerializer.Serialize(status, Json);

        Assert.Contains("\"totalDocuments\":2", reportJson);
        Assert.Contains("\"stalePercent\":50", reportJson);
        Assert.Contains("\"sourceUpdatedAt\"", reportJson);
        Assert.Contains("\"missingFromIndex\"", reportJson);
        Assert.Contains("\"modelVersions\"", statusJson);
        Assert.Contains("\"activeDenseVector\":\"dense-v2\"", statusJson);
        Assert.Contains("\"currentJob\"", statusJson);

        var roundTripped = JsonSerializer.Deserialize<DriftReport>(reportJson, Json)!;
        Assert.Equal(report.TotalDocuments, roundTripped.TotalDocuments);
        Assert.Equal(report.StaleDocuments, roundTripped.StaleDocuments);
        Assert.Equal(report.StalePercent, roundTripped.StalePercent);
        Assert.Equal(report.Stale.Single().DocId, roundTripped.Stale.Single().DocId);
        Assert.Equal(report.MissingFromIndex, roundTripped.MissingFromIndex);
    }

    [Fact]
    public void The_job_states_are_the_names_the_screen_shows()
    {
        Assert.Equal("queued", AdminJobStates.Queued);
        Assert.Equal("running", AdminJobStates.Running);
        Assert.Equal("succeeded", AdminJobStates.Succeeded);
        Assert.Equal("failed", AdminJobStates.Failed);
    }
}