extern alias service;
using Metrics = global::Maf.Lab.Eval.Suites.Metrics;
using System.Text.Json;
using Maf.Lab.Api.Agent;
using Maf.Lab.Eval.Datasets;
using service::Maf.Lab.Eval.Suites;

namespace Maf.Lab.Tests;

/// <summary>The presentation suite's deterministic checks (add-system-prompt-v3): tables and amounts read off an answer.</summary>
public class PresentationSuiteTests
{
    [Fact]
    public void A_markdown_table_is_two_or_more_piped_lines()
    {
        Assert.True(PresentationSuite.HasTable("Plan:\n| Class | Trade |\n|---|---|\n| US equity | -8 000 $ |"));
        Assert.False(PresentationSuite.HasTable("No rebalance is needed | every class is within 5 %."));
    }

    [Fact]
    public void The_answer_language_is_the_script_most_of_its_letters_are_in()
    {
        Assert.Equal("bg", PresentationSuite.LanguageOf("За A-1043 не е нужно ребалансиране: rebalanceNeeded е false."));
        Assert.Equal("en", PresentationSuite.LanguageOf("A-1043 needs no rebalance."));
    }

    [Fact]
    public void A_bullet_per_card_row_is_the_card_restated()
    {
        var content = JsonDocument.Parse("""{"holdings":[{"assetClass":"US equity"},{"assetClass":"International equity"},{"assetClass":"Cash"}]}""").RootElement;
        TurnCard[] cards = [new("c1", "card-c1", "maf-lab/holdings", content)];

        Assert.True(PresentationSuite.ListsRows("Plan:\n* **US equity** – sell\n* **International equity** – sell\n* **Cash** – buy", cards));
        Assert.False(PresentationSuite.ListsRows("No rebalance is needed; US equity is 0.6 points over its target.", cards));
    }

    [Theory]
    [InlineData("Продажба на 268 000 $ US equity.", 268000)]
    [InlineData("Sell $268,000 of US equity.", 268000)]
    [InlineData("Buy 8,000 USD of cash.", 8000)]
    [InlineData("Промяна −1 000 $ за годината.", -1000)]
    [InlineData("Общо 1 300 000 $.", 1300000)]
    [InlineData("A trade of $9000.", 9000)]
    [InlineData("Sell $225 k of international equity.", 225000)]
    [InlineData("Купи за 9,5 хил. $ пари в брой.", 9500)]
    [InlineData("AUM of $1.3M at quarter end.", 1300000)]
    public void An_amount_next_to_a_currency_is_read_as_a_number(string answer, double expected)
    {
        Assert.Equal([(decimal)expected], PresentationSuite.Amounts(answer));
    }

    [Theory]
    [InlineData("Account A-1043 is within tolerance.")]
    [InlineData("As of 2026 the drift is 0.6 %.")]
    [InlineData("Weights after: 20 %, 10 %, 60 %, 10 %.")]
    public void Ids_years_and_percentages_are_not_amounts(string answer)
    {
        Assert.Empty(PresentationSuite.Amounts(answer));
    }

    [Fact]
    public void Card_numbers_are_every_number_in_the_content_as_an_absolute_value()
    {
        var content = JsonDocument.Parse("""{"totalMarketValue":1300000,"holdings":[{"tradeToTarget":-8000,"weightAfterPct":20}]}""").RootElement;

        var numbers = PresentationSuite.CardNumbers([new TurnCard("c1", "card-c1", "maf-lab/holdings", content)]);

        Assert.Contains(1300000m, numbers);
        Assert.Contains(8000m, numbers);
        Assert.Contains(20m, numbers);
    }

    [Fact]
    public void The_dataset_asks_in_both_languages_and_names_a_verdict_both_ways()
    {
        var cases = DatasetLoader.Presentation(Path.Combine(Repo(), "evals"));

        Assert.Contains(cases, c => c.Language == "bg");
        Assert.Contains(cases, c => c.Language == "en");
        Assert.Contains(cases, c => c.RebalanceNeeded == true);
        Assert.Contains(cases, c => c.RebalanceNeeded == false);
        Assert.All(cases, c => Assert.True(c.Carded));
        // The question that asks about every class is the one a list may answer.
        Assert.Equal(["p-05"], cases.Where(c => c.RowsRequested).Select(c => c.Id));
    }

    private static string Repo()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "maf-lab.sln")))
        {
            dir = dir.Parent;
        }
        return dir!.FullName;
    }
}
