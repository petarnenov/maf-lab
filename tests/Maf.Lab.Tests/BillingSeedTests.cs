using System.Text.Json;
using Maf.Lab.Domain.Billing;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Retrieval.Billing;

namespace Maf.Lab.Tests;

public class BillingSeedTests
{
    private static readonly Principal FirmA = new("adam", TenantId.Firm("firm-a"), Role.ADVISOR, []);
    private static readonly Principal FirmB = new("bianca", TenantId.Firm("firm-b"), Role.ADVISOR, []);

    private static BillingSeedStore Load() =>
        new(File.ReadAllText(Path.Combine(CorpusLoaderTests.RepoRoot(), "compose", "seed", "billing-runs.json")));

    [Fact]
    public void Seed_loads_and_run_4417_is_firm_a_failed_with_fs_required()
    {
        var status = Load().GetStatus(FirmA, "run #4417");
        Assert.NotNull(status);
        Assert.Equal("failed", status.Status);
        Assert.StartsWith("FS-REQUIRED", status.FailureReason);
        Assert.Equal(new DateOnly(2026, 6, 1), status.PeriodStart);
    }

    [Fact]
    public void Another_firms_run_is_not_found()
    {
        Assert.Null(Load().GetStatus(FirmB, "4417"));
    }

    [Fact]
    public void Output_contracts_have_no_note_field_even_when_the_seed_has_one()
    {
        var seed = File.ReadAllText(Path.Combine(CorpusLoaderTests.RepoRoot(), "compose", "seed", "billing-runs.json"));
        Assert.Contains("external@evil.example", seed);

        var store = Load();
        var all = store.Search(FirmA, null, null, null, 20);
        var json = JsonSerializer.Serialize(all) + string.Concat(all.Runs.Select(r => JsonSerializer.Serialize(store.GetStatus(FirmA, r.RunId))));

        Assert.DoesNotContain("external@evil.example", json);
        Assert.DoesNotContain("note", json, StringComparison.OrdinalIgnoreCase);
        Assert.Null(typeof(BillingRunStatus).GetProperty("Note"));
        Assert.Null(typeof(BillingRunSummary).GetProperty("Note"));
    }

    [Fact]
    public void Search_is_firm_scoped_and_filters_by_status()
    {
        var failed = Load().Search(FirmA, "failed", null, null, 20);
        Assert.All(failed.Runs, r => Assert.Equal("failed", r.Status));
        Assert.Contains(failed.Runs, r => r.RunId == "4417");
        Assert.DoesNotContain(Load().Search(FirmB, null, null, null, 20).Runs, r => r.RunId == "4417");
    }
}

public class BillingSeedPathTests
{
    [Fact]
    public void Seed_is_found_from_the_repository_without_configuration()
    {
        var path = BillingSeedStore.ResolvePath(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        Assert.True(File.Exists(path), path);
        Assert.EndsWith(Path.Combine("compose", "seed", "billing-runs.json"), path);
    }
}
