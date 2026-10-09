using System.Text.Json;
using Maf.Lab.Domain.Feedback;
using Maf.Lab.Plugins.Abstractions;
using Maf.Lab.Plugins.FeedbackReview;

namespace Maf.Lab.Tests;

public class LabelRowTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static string? DomainOf(string tool) => tool == "search_portfolio_documents" ? "portfolio" : "billing";
    private static ReviewStoredTurn Turn(params ToolCallRecord[] calls) => new("t1", "c1", "adam", "firm-a", "q", "", "[]",
        JsonSerializer.Serialize(calls, Json), "[]", DateTimeOffset.UnixEpoch, false, []);
    private static ToolCallRecord Search(string tool, params string[] docIds) => new(tool, "", "ok", docIds.Length, docIds, [], "x", "done");

    [Fact]
    public void A_retrieval_label_records_the_domain_whose_search_found_its_chunks()
    {
        var turn = Turn(Search("search_documents", "shared/docs/tiered-fee-calculation.md"),
            Search("search_portfolio_documents", "shared/docs/portfolio-quarter-end-valuation.md"));
        LabelRequest Label(params string[] chunks) => new(Maf.Lab.Domain.Feedback.EvalDataset.Retrieval, null, chunks, null, null);

        var portfolio = Label("shared/docs/portfolio-quarter-end-valuation.md#quarter-end-valuation-handoff-to-billing");
        var billing = Label("shared/docs/tiered-fee-calculation.md#x");
        var both = Label("shared/docs/tiered-fee-calculation.md#x", "shared/docs/portfolio-quarter-end-valuation.md#y");

        Assert.Equal("portfolio", FeedbackReviewEndpoints.RetrievalDomain(turn, portfolio, DomainOf));
        Assert.Equal("billing", FeedbackReviewEndpoints.RetrievalDomain(turn, billing, DomainOf));
        Assert.Null(FeedbackReviewEndpoints.RetrievalDomain(turn, both, DomainOf));

        var (row, _) = FeedbackReviewEndpoints.BuildRow(turn, portfolio, "portfolio");
        Assert.Equal("portfolio", row!["domain"]!.GetValue<string>());
        var (billingRow, _) = FeedbackReviewEndpoints.BuildRow(turn, billing, "billing");
        Assert.Null(billingRow!["domain"]);
        var (none, error) = FeedbackReviewEndpoints.BuildRow(turn, both, null);
        Assert.Null(none);
        Assert.Contains("both domains", error);
    }

}
