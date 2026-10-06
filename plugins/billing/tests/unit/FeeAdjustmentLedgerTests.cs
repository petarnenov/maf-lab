using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Retrieval.Billing;

namespace Maf.Lab.Tests;

public class FeeAdjustmentLedgerTests : IDisposable
{
    private static readonly Principal FirmA = new("adam", TenantId.Firm("firm-a"), Role.USER);
    private static readonly Principal FirmB = new("bianca", TenantId.Firm("firm-b"), Role.USER);
    private static readonly DateTimeOffset At = new(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"maf-lab-ledger-{Guid.NewGuid():N}.db");

    private FeeAdjustmentLedger Ledger() => new($"Data Source={_path}");

    public void Dispose()
    {
        // Only this fixture's own pool: ClearAllPools is process-wide and can dispose a connection another test's host
        // is opening (dotnet/efcore#38854).
        Microsoft.Data.Sqlite.SqliteConnection.ClearPool(new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_path}"));
        foreach (var file in new[] { _path, _path + "-wal", _path + "-shm" })
        {
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void The_schema_is_created_on_an_empty_file_and_creating_it_twice_is_harmless()
    {
        var ledger = Ledger();
        ledger.Initialize();
        ledger.Initialize();
        Assert.True(File.Exists(_path));
        Assert.Equal(0m, ledger.AppliedTotal(FirmA, "A-1042"));
    }

    [Fact]
    public void Applying_returns_the_resulting_fee()
    {
        var applied = Ledger().Apply(FirmA, "adj-1", "A-1042", -200m, seededFee: 1200m, "USD", At);

        Assert.False(applied.AlreadyApplied);
        Assert.Equal(1200m, applied.PreviousFee);
        Assert.Equal(-200m, applied.Amount);
        Assert.Equal(1000m, applied.CurrentFee);
        Assert.Equal("adj-1", applied.AdjustmentId);
    }

    [Fact]
    public void The_same_proposal_applies_once_however_often_it_is_confirmed()
    {
        var ledger = Ledger();
        var first = ledger.Apply(FirmA, "adj-1", "A-1042", -200m, 1200m, "USD", At);
        var second = ledger.Apply(FirmA, "adj-1", "A-1042", -200m, 1200m, "USD", At.AddMinutes(5));

        Assert.False(first.AlreadyApplied);
        Assert.True(second.AlreadyApplied);
        Assert.Equal(first.AdjustmentId, second.AdjustmentId);
        Assert.Equal(first.CurrentFee, second.CurrentFee);
        Assert.Equal(-200m, ledger.AppliedTotal(FirmA, "A-1042"));
    }

    [Fact]
    public void Two_replicas_confirming_at_once_apply_it_once()
    {
        // Separate instances over one file is what two replicas sharing a volume look like.
        var ledgers = Enumerable.Range(0, 8).Select(_ => Ledger()).ToList();
        var results = new Maf.Lab.Domain.Billing.FeeAdjustmentApplied[ledgers.Count];

        Parallel.For(0, ledgers.Count, i =>
            results[i] = ledgers[i].Apply(FirmA, "adj-race", "A-1042", -50m, 1200m, "USD", At));

        Assert.Single(results, r => !r.AlreadyApplied);
        Assert.All(results, r => Assert.Equal(1150m, r.CurrentFee));
        Assert.Equal(-50m, Ledger().AppliedTotal(FirmA, "A-1042"));
    }

    [Fact]
    public void Applied_adjustments_outlive_the_process()
    {
        Ledger().Apply(FirmA, "adj-1", "A-1042", -200m, 1200m, "USD", At);

        // A new instance over the same file is the same store after a restart.
        Assert.Equal(-200m, Ledger().AppliedTotal(FirmA, "A-1042"));
    }

    [Fact]
    public void Adjustments_accumulate_in_the_order_they_were_applied()
    {
        var ledger = Ledger();
        ledger.Apply(FirmA, "adj-1", "A-1042", -200m, 1200m, "USD", At);
        var second = ledger.Apply(FirmA, "adj-2", "A-1042", -100m, 1200m, "USD", At.AddHours(1));

        Assert.Equal(1000m, second.PreviousFee);
        Assert.Equal(900m, second.CurrentFee);
        Assert.Equal(-300m, ledger.AppliedTotal(FirmA, "A-1042"));
    }

    [Fact]
    public void Another_firms_adjustments_are_never_counted()
    {
        var ledger = Ledger();
        ledger.Apply(FirmA, "adj-1", "A-1042", -200m, 1200m, "USD", At);

        Assert.Equal(0m, ledger.AppliedTotal(FirmB, "A-1042"));

        // The same adjustment id in another firm is another adjustment.
        var other = ledger.Apply(FirmB, "adj-1", "B-200", -10m, 2410m, "USD", At);
        Assert.False(other.AlreadyApplied);
        Assert.Equal(2400m, other.CurrentFee);
        Assert.Equal(-200m, ledger.AppliedTotal(FirmA, "A-1042"));
    }

    [Fact]
    public void Money_survives_the_round_trip_without_rounding()
    {
        var applied = Ledger().Apply(FirmA, "adj-1", "A-1044", -0.01m, 3120.75m, "USD", At);
        Assert.Equal(3120.74m, applied.CurrentFee);
        Assert.Equal(-0.01m, Ledger().AppliedTotal(FirmA, "A-1044"));
    }

    // ---- a reduction never takes the fee below zero --------------------------------------------------

    [Fact]
    public void A_reduction_below_zero_is_refused_and_writes_nothing()
    {
        var ledger = Ledger();

        var refused = Assert.Throws<FeeWouldGoBelowZeroException>(() =>
            ledger.Apply(FirmA, "adj-1", "A-1042", -4116m, 812m, "USD", At));

        Assert.Equal(812m, refused.PreviousFee);
        Assert.Equal(-3304m, refused.ResultingFee);
        Assert.Equal(0m, ledger.AppliedTotal(FirmA, "A-1042"));
    }

    [Fact]
    public void A_reduction_to_exactly_zero_is_applied()
    {
        var applied = Ledger().Apply(FirmA, "adj-1", "A-1042", -812m, 812m, "USD", At);

        Assert.Equal(0m, applied.CurrentFee);
    }

    [Fact]
    public void Two_reductions_fine_alone_apply_only_while_the_fee_stays_at_or_above_zero()
    {
        var ledger = Ledger();
        ledger.Apply(FirmA, "adj-1", "A-1042", -500m, 812m, "USD", At);

        Assert.Throws<FeeWouldGoBelowZeroException>(() =>
            ledger.Apply(FirmA, "adj-2", "A-1042", -500m, 812m, "USD", At.AddMinutes(1)));

        Assert.Equal(-500m, ledger.AppliedTotal(FirmA, "A-1042"));
    }

    [Fact]
    public void Raising_a_fee_already_below_zero_is_applied()
    {
        var applied = Ledger().Apply(FirmA, "adj-1", "A-1042", 1000m, -6048m, "USD", At);

        Assert.Equal(-5048m, applied.CurrentFee);
    }

    [Fact]
    public void A_repeated_confirmation_after_the_fee_moved_reports_already_applied()
    {
        var ledger = Ledger();
        var first = ledger.Apply(FirmA, "adj-1", "A-1042", -500m, 812m, "USD", At);
        ledger.Apply(FirmA, "adj-2", "A-1042", -312m, 812m, "USD", At.AddMinutes(1));

        var again = ledger.Apply(FirmA, "adj-1", "A-1042", -500m, 812m, "USD", At.AddMinutes(2));

        Assert.True(again.AlreadyApplied);
        Assert.Equal(first.CurrentFee, again.CurrentFee);
        Assert.Equal(-812m, ledger.AppliedTotal(FirmA, "A-1042"));
    }
}

public class AccountFeesTests : IDisposable
{
    private static readonly Principal FirmA = new("adam", TenantId.Firm("firm-a"), Role.USER);
    private static readonly Principal FirmB = new("bianca", TenantId.Firm("firm-b"), Role.USER);
    private static readonly DateTimeOffset At = new(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"maf-lab-fees-{Guid.NewGuid():N}.db");

    private FeeAdjustmentLedger Ledger() => new($"Data Source={_path}");

    private AccountFees Fees() => new(BillingAccountTests.Load(), Ledger());

    public void Dispose()
    {
        // Only this fixture's own pool: ClearAllPools is process-wide and can dispose a connection another test's host
        // is opening (dotnet/efcore#38854).
        Microsoft.Data.Sqlite.SqliteConnection.ClearPool(new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_path}"));
        foreach (var file in new[] { _path, _path + "-wal", _path + "-shm" })
        {
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void A_seeded_account_reads_its_seeded_fee()
    {
        Assert.Equal(1200m, Fees().Current(FirmA, "A-1042")!.Fee);
    }

    [Fact]
    public void An_applied_adjustment_moves_the_current_fee()
    {
        Ledger().Apply(FirmA, "adj-1", "A-1042", -200m, 1200m, "USD", At);

        Assert.Equal(1000m, Fees().Current(FirmA, "A-1042")!.Fee);
        Assert.Equal(1200m, Fees().SeededFee(FirmA, "A-1042"));
    }

    [Fact]
    public void Another_firms_adjustment_never_moves_this_firms_fee()
    {
        Ledger().Apply(FirmB, "adj-1", "B-200", -410m, 2410m, "USD", At);

        Assert.Equal(1200m, Fees().Current(FirmA, "A-1042")!.Fee);
        Assert.Equal(2000m, Fees().Current(FirmB, "B-200")!.Fee);
    }

    [Fact]
    public void Another_firms_account_has_no_fee_to_read()
    {
        Assert.Null(Fees().Current(FirmA, "B-200"));
        Assert.Null(Fees().SeededFee(FirmA, "B-200"));
    }
}
