using Maf.Lab.Eval.Datasets;
using Maf.Lab.Eval.Judging;

namespace Maf.Lab.Tests;

public class AnswerSentencesTests
{
    private static string EvalsRoot => Path.Combine(CorpusLoaderTests.RepoRoot(), "evals");

    [Fact]
    public void English_prose_is_cut_at_sentence_ends()
    {
        var sentences = AnswerSentences.Split("A run failed. Open it and fix the schedule! Did it work? Re-run it.");

        Assert.Equal(["A run failed.", "Open it and fix the schedule!", "Did it work?", "Re-run it."], sentences);
    }

    [Fact]
    public void Bulgarian_prose_is_cut_at_sentence_ends()
    {
        var sentences = AnswerSentences.Split("Таксата се изчислява след периода. Ако сметката се затвори, клиентът плаща само активните дни.");

        Assert.Equal(["Таксата се изчислява след периода.", "Ако сметката се затвори, клиентът плаща само активните дни."], sentences);
    }

    [Fact]
    public void Latin_script_bulgarian_is_cut_like_any_prose()
    {
        var sentences = AnswerSentences.Split("Filtarat se stroi v TenantFilter.For. Tova e edinstvenoto miasto.");

        Assert.Equal(["Filtarat se stroi v TenantFilter.For.", "Tova e edinstvenoto miasto."], sentences);
    }

    [Fact]
    public void Back_ticked_paths_line_ranges_and_decimals_stay_whole()
    {
        var sentences = AnswerSentences.Split(
            "It is built in `TenantFilter.For` (`src/Maf.Lab.Retrieval/Store/TenantScopedSearch.cs:121-140`). The rate is 0.75 % on $6.5 M. Done.");

        Assert.Equal(
            ["It is built in `TenantFilter.For` (`src/Maf.Lab.Retrieval/Store/TenantScopedSearch.cs:121-140`).", "The rate is 0.75 % on $6.5 M.", "Done."],
            sentences);
    }

    [Fact]
    public void A_dot_inside_back_ticks_followed_by_a_capital_does_not_cut()
    {
        var sentences = AnswerSentences.Split("Call `Wrap. Then` to build it.");

        Assert.Equal(["Call `Wrap. Then` to build it."], sentences);
    }

    [Fact]
    public void List_markers_are_dropped_and_each_item_is_cut()
    {
        var sentences = AnswerSentences.Split("Steps:\n\n1. Open the run. Identify the accounts.\n2) Assign the schedule.\n- Validate.\n* **Re-run** it.\n• Done.");

        Assert.Equal(["Steps:", "Open the run.", "Identify the accounts.", "Assign the schedule.", "Validate.", "**Re-run** it.", "Done."], sentences);
    }

    [Fact]
    public void Headings_and_table_rules_are_dropped_and_each_table_row_is_one_sentence()
    {
        var sentences = AnswerSentences.Split("## Fees\n| Tier | Rate |\n|---|:---:|\n| First $1 M | 1 % |\n| Rest | 0.5 % |");

        Assert.Equal(["| Tier | Rate |", "| First $1 M | 1 % |", "| Rest | 0.5 % |"], sentences);
    }

    [Fact]
    public void A_fenced_code_block_is_one_sentence()
    {
        var sentences = AnswerSentences.Split("It looks like this:\n\n```csharp\npublic static Filter For(TenantId tenant) => new() {\n    Must = { X }\n};\n```\n\nSo it is built there.");

        Assert.Equal(3, sentences.Count);
        Assert.StartsWith("```csharp", sentences[1]);
        Assert.EndsWith("```", sentences[1]);
        Assert.Equal("So it is built there.", sentences[2]);
    }

    [Fact]
    public void Closing_marks_stay_with_the_sentence_they_end()
    {
        var sentences = AnswerSentences.Split("The policy says “a valuation is stale.” Then it blocks the run.");

        Assert.Equal(["The policy says “a valuation is stale.”", "Then it blocks the run."], sentences);
    }

    [Fact]
    public void A_lower_case_continuation_does_not_cut()
    {
        var sentences = AnswerSentences.Split("Use actual/365 (e.g. for quarters) and 30/360 otherwise.");

        Assert.Single(sentences);
    }

    [Fact]
    public void Empty_and_punctuation_only_pieces_are_dropped()
    {
        Assert.Empty(AnswerSentences.Split("\n\n---\n  \n"));
    }

    [Fact]
    public void Every_labelled_answer_splits_into_sentences_that_rebuild_its_words()
    {
        var answers = DatasetLoader.AnswerCheck(EvalsRoot).Select(c => c.Answer)
            .Concat(DatasetLoader.GenerationJudge(EvalsRoot).Select(c => c.Answer));

        foreach (var answer in answers)
        {
            var sentences = AnswerSentences.Split(answer);
            Assert.NotEmpty(sentences);
            // Nothing is lost but markers, headings and rules: every word of a sentence is a word of the answer.
            Assert.All(sentences.SelectMany(s => s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)),
                w => Assert.Contains(w, answer));
        }
    }
}
