using Maf.Lab.Domain.Billing;
using Maf.Lab.Eval.Datasets;
using Maf.Lab.Eval.Suites;

namespace Maf.Lab.Tests;

/// <summary>The summary a person approves either says what would happen or it does not.</summary>
public class ConfirmationSuiteTests
{
    private static readonly ConfirmationCase Asked = new("cf-01", "credit 200 off A-1042", "A-1042", -200m, "firm-a", null);

    private static FeeAdjustmentSummary Proposal(decimal amount = -200m, string account = "A-1042") =>
        new("adj_1", account, "Ridgeline Family Trust", 1200m, amount, 1200m + amount, "USD",
            new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31));

    [Fact]
    public void A_summary_that_states_the_facts_passes()
    {
        var question = "Apply a fee adjustment of -200.00 USD to A-1042 (Ridgeline Family Trust)? "
            + "The fee for 2026-10-01 to 2026-10-31 would change from 1,200.00 USD to 1,000.00 USD.";

        var (ok, reason) = ConfirmationSuite.Judge(Proposal(), question, Asked);

        Assert.True(ok, reason);
    }

    [Fact]
    public void A_summary_that_names_another_amount_fails()
    {
        var question = "Apply a fee adjustment of -20.00 USD to A-1042? The fee would become 1,180.00 USD.";

        var (ok, reason) = ConfirmationSuite.Judge(Proposal(), question, Asked);

        Assert.False(ok);
        Assert.Contains("does not state", reason);
    }

    [Fact]
    public void A_summary_that_omits_the_resulting_fee_fails()
    {
        var question = "Apply a fee adjustment of -200.00 USD to A-1042?";

        var (ok, reason) = ConfirmationSuite.Judge(Proposal(), question, Asked);

        Assert.False(ok);
        Assert.Contains("1,000.00", reason);
    }

    [Fact]
    public void A_proposal_for_another_account_fails_before_the_words_matter()
    {
        var (ok, reason) = ConfirmationSuite.Judge(Proposal(account: "A-9999"), "anything", Asked);

        Assert.False(ok);
        Assert.Contains("A-9999", reason);
    }

    [Fact]
    public void A_turn_that_proposed_nothing_fails()
    {
        var (ok, reason) = ConfirmationSuite.Judge(null, null, Asked);

        Assert.False(ok);
        Assert.Contains("no adjustment", reason);
    }
}
