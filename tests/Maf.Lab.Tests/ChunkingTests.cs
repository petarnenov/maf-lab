using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Indexing.Chunking;
using Maf.Lab.Indexing.Corpus;

namespace Maf.Lab.Tests;

public class ChunkingTests
{
    [Fact]
    public void Markdown_chunks_carry_heading_path()
    {
        const string md = """
            # Billing
            Intro text.
            ## Fee schedules
            Schedules define rates.
            ### Missing schedule
            When a schedule is missing the run fails with FS-REQUIRED.
            ## Proration
            Prorate new accounts.
            """;
        var chunks = new MarkdownChunker().Chunk(md, "docs/billing.md", 1500);

        Assert.Contains(chunks, c => c.SectionPath == "Billing > Fee schedules > Missing schedule" && c.Text.Contains("FS-REQUIRED"));
        Assert.Contains(chunks, c => c.SectionPath == "Billing > Proration");
        Assert.Contains(chunks, c => c.SectionPath == "Billing" && c.Text.Contains("Intro"));
    }

    [Fact]
    public void Procedure_chunks_align_to_steps_and_sections()
    {
        const string text = """
            Procedure: Missing fee schedule
            Section 1: Identify
            1. Open the failed run.
            2. List accounts flagged FS-REQUIRED.
               Export them to CSV.
            Section 2: Fix
            1. Assign a schedule to each account.
            2. Validate the assignment.
            3. Re-run billing.
            """;
        var chunks = new ProcedureChunker().Chunk(text, "procedures/missing.txt", 1500);

        Assert.Equal(5, chunks.Count);
        Assert.Equal("Procedure: Missing fee schedule > Section 1: Identify > Step 2", chunks[1].SectionPath);
        Assert.Contains("Export them to CSV.", chunks[1].Text);
        Assert.Equal("Procedure: Missing fee schedule > Section 2: Fix > Step 3", chunks[4].SectionPath);
    }

    [Fact]
    public void Oversize_step_is_split_but_other_steps_stay_whole()
    {
        var longStep = "1. " + string.Join(" ", Enumerable.Repeat("verify the account valuation and the custodian feed.", 60));
        var text = $"Title\nSection 1: Big\n{longStep}\n2. Short step.";
        var chunks = new ProcedureChunker().Chunk(text, "procedures/big.txt", 400);

        Assert.True(chunks.Count(c => c.SectionPath.EndsWith("Step 1")) > 1);
        Assert.Single(chunks, c => c.SectionPath.EndsWith("Step 2"));
        Assert.All(chunks, c => Assert.True(c.Text.Length <= 400));
    }

    [Fact]
    public void Csharp_code_chunks_by_class_and_method_with_symbol_names()
    {
        const string code = """
            namespace Billing;

            public sealed class FeeCalculator
            {
                private readonly decimal _floor = 0m;

                public decimal Tiered(decimal aum)
                {
                    if (aum > 1_000_000m) { return aum * 0.008m; }
                    return aum * 0.01m;
                }

                public decimal Prorate(decimal fee, int days, int periodDays)
                {
                    return fee * days / periodDays;
                }
            }
            """;
        var chunks = new CodeChunker().Chunk(code, "code/FeeCalculator.cs", 1500);

        Assert.Contains(chunks, c => c.Symbol == "FeeCalculator.Tiered" && c.Text.Contains("0.008m") && c.SectionPath == "FeeCalculator.cs > FeeCalculator.Tiered");
        Assert.Contains(chunks, c => c.Symbol == "FeeCalculator.Prorate" && c.Text.Contains("periodDays"));
        Assert.Contains(chunks, c => c.Symbol == "FeeCalculator" && c.Text.Contains("_floor"));
    }

    [Fact]
    public void Typescript_and_python_functions_are_symbols()
    {
        const string ts = """
            export function formatInvoice(total: number): string {
              return `$${total.toFixed(2)}`;
            }

            export const lineItems = (rows: number[]) => {
              return rows.map(r => r * 2);
            };
            """;
        const string py = """
            def prorate(fee, days, period_days):
                return fee * days / period_days


            class Valuation:
                def __init__(self, aum):
                    self.aum = aum

                def is_stale(self, age_days):
                    return age_days > 3
            """;
        var tsChunks = new CodeChunker().Chunk(ts, "code/invoice.ts", 1500);
        var pyChunks = new CodeChunker().Chunk(py, "code/valuation.py", 1500);

        Assert.Contains(tsChunks, c => c.Symbol == "formatInvoice");
        Assert.Contains(tsChunks, c => c.Symbol == "lineItems");
        Assert.Contains(pyChunks, c => c.Symbol == "prorate");
        Assert.Contains(pyChunks, c => c.Symbol == "Valuation.is_stale" && c.Text.Contains("age_days > 3"));
    }

    [Fact]
    public void Chunk_ids_are_stable_readable_and_unique()
    {
        var doc = new SourceDocument(TenantId.Firm("firm-a"), "docs", "docs/policy.md", "/x/policy.md",
            "# Policy\n## Approvals\nA\n\nB\n## Approvals\nC", DateTimeOffset.UnixEpoch);
        var first = ChunkBuilder.Build(doc, 1500).Select(c => c.ChunkId).ToList();
        var second = ChunkBuilder.Build(doc, 1500).Select(c => c.ChunkId).ToList();

        Assert.Equal(first, second);
        Assert.Equal(first.Count, first.Distinct().Count());
        Assert.Contains("firm-a/docs/policy.md#policy-approvals", first);
        Assert.Contains("firm-a/docs/policy.md#policy-approvals-2", first);
        Assert.Equal("firm-a/docs/policy.md", doc.DocId);
    }
}
