namespace Maf.Lab.IntegrationTests;

/// <summary>A throwaway corpus directory for indexing tests.</summary>
public sealed class TempCorpus : IDisposable
{
    public string Root { get; } = Directory.CreateTempSubdirectory("maf-corpus-").FullName;

    public string Write(string relative, string content, DateTime? mtimeUtc = null)
    {
        var path = Path.Combine(Root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        File.SetLastWriteTimeUtc(path, mtimeUtc ?? new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc));
        return path;
    }

    public void Touch(string relative, DateTime mtimeUtc) => File.SetLastWriteTimeUtc(Path.Combine(Root, relative), mtimeUtc);

    public void Delete(string relative) => File.Delete(Path.Combine(Root, relative));

    public static TempCorpus Small()
    {
        var c = new TempCorpus();
        c.Write("shared/docs/billing.md", """
            # Billing overview
            ## Fee schedules
            A fee schedule defines the rate charged on assets under management.
            ### Missing fee schedule
            When a fee schedule is missing the billing run fails with FS-REQUIRED.
            ## Proration
            New accounts are prorated by days in the billing period.
            """);
        c.Write("shared/procedures/missing-fee-schedule.txt", """
            Procedure: Missing fee schedule
            Section 1: Identify
            1. Open the failed billing run and list accounts flagged FS-REQUIRED.
            Section 2: Fix
            1. Assign a fee schedule to each flagged account.
            2. Re-run the billing run.
            """);
        c.Write("shared/code/fees.cs", """
            public static class Fees
            {
                public static decimal Tiered(decimal aum)
                {
                    return aum > 1000000m ? aum * 0.008m : aum * 0.01m;
                }
            }
            """);
        c.Write("firm-a/docs/acme-policy.md", """
            # Acme approval policy
            ## Invoice approval
            Invoices over 25000 dollars need FIRM_ADMIN approval at Acme Wealth Partners. ACME-CANARY-4410
            """);
        for (var i = 0; i < 30; i++)
        {
            c.Write($"firm-b/docs/nw-profile-{i:D3}.md", $"""
                # Northwind household {i}
                ## Household rebalancing fee schedule
                Household rebalancing fee schedule for Northwind Capital client {i}. The fee schedule applies a household rebalancing fee. NW-CANARY-7731-HH{i:D4}
                """);
        }
        c.Write("firm-c/docs/contoso-flat.md", """
            # Contoso flat fee
            ## Household rebalancing fee
            Contoso Advisors charges a flat household rebalancing fee under schedule CONTOSO-FLAT-100. CONTOSO-CANARY-2290
            """);
        return c;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
