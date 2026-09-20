using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Maf.Lab.Domain.Billing;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Retrieval.Billing;

namespace Maf.Lab.Tests;

public class BillingAccountTests
{
    private static readonly Principal FirmA = new("adam", TenantId.Firm("firm-a"), Role.ADVISOR, []);
    private static readonly Principal FirmB = new("bianca", TenantId.Firm("firm-b"), Role.ADVISOR, []);

    internal static BillingAccountStore Load() =>
        new(File.ReadAllText(Path.Combine(CorpusLoaderTests.RepoRoot(), "compose", "seed", "billing-accounts.json")));

    [Fact]
    public void Seed_loads_and_a_1042_is_firm_a_at_1200()
    {
        var account = Load().Find(FirmA, "A-1042");
        Assert.NotNull(account);
        Assert.Equal("Ridgeline Family Trust", account.Name);
        Assert.Equal(1200.00m, account.Fee);
        Assert.Equal("USD", account.Currency);
        Assert.Equal(new DateOnly(2026, 10, 1), account.NextPeriodStart);
    }

    [Fact]
    public void An_account_is_found_however_the_advisor_writes_it()
    {
        Assert.NotNull(Load().Find(FirmA, "account a-1042"));
        Assert.NotNull(Load().Find(FirmA, " A1042 "));
    }

    [Fact]
    public void Another_firms_account_is_not_found()
    {
        Assert.Null(Load().Find(FirmA, "B-200"));
        Assert.Null(Load().Find(FirmB, "A-1042"));
        Assert.NotNull(Load().Find(FirmB, "B-200"));
    }

    [Fact]
    public void Output_contracts_have_no_note_field_even_when_the_seed_has_one()
    {
        var seed = File.ReadAllText(Path.Combine(CorpusLoaderTests.RepoRoot(), "compose", "seed", "billing-accounts.json"));
        Assert.Contains("external@evil.example", seed);
        Assert.Contains("ACME-CANARY-4410", seed);

        var store = Load();
        var json = string.Concat(new[] { "A-1042", "A-1043", "A-1044" }.Select(id => JsonSerializer.Serialize(store.Find(FirmA, id))));

        Assert.DoesNotContain("external@evil.example", json);
        Assert.DoesNotContain("CANARY", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("note", json, StringComparison.OrdinalIgnoreCase);
        Assert.Null(typeof(BillingAccount).GetProperty("Note"));
    }
}

public class BillingAccountPathTests
{
    [Fact]
    public void Accounts_seed_is_found_from_the_repository_without_configuration()
    {
        var path = BillingAccountStore.ResolvePath(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        Assert.True(File.Exists(path), path);
        Assert.EndsWith(Path.Combine("compose", "seed", "billing-accounts.json"), path);
    }

    [Fact]
    public void Configuration_wins_over_the_repository()
    {
        var configuration = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Billing:AccountsSeedPath"] = "/seed/billing-accounts.json" })
            .Build();
        Assert.Equal(Path.GetFullPath("/seed/billing-accounts.json"), BillingAccountStore.ResolvePath(configuration));
    }

    [Fact]
    public void The_runs_seed_still_resolves_the_way_it_did()
    {
        var path = BillingSeedStore.ResolvePath(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        Assert.True(File.Exists(path), path);
        Assert.EndsWith(Path.Combine("compose", "seed", "billing-runs.json"), path);
    }
}
