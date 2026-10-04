using Maf.Lab.Api.Agent;
using Maf.Lab.Api.Agent.Jev;
using Maf.Lab.Eval;
using Maf.Lab.Eval.Judging;

namespace Maf.Lab.Tests;

public class CitationsTests
{
    private static ReadItem Code(string path, int start, int end) => new($"code:{path}:{start}-{end}", $"{path}:{start}-{end} › X: code", [path], Domains.Codebase);

    private static ReadItem Doc(string docId, string section) => new($"doc:{docId}›{section}", $"{docId} › {section}: text", [], Domains.Billing);

    private static readonly ReadItem[] Read =
    [
        Code("src/Maf.Lab.Retrieval/Store/TenantScopedSearch.cs", 121, 123),
        Code("src/Maf.Lab.Retrieval/Store/TenantScopedSearch.cs", 138, 140),
        Doc("shared/procedures/missing-fee-schedule.txt", "Procedure: Missing fee schedule > Section 3: Assign a fee schedule > Step 2"),
    ];

    [Fact]
    public void A_path_range_and_the_bare_range_after_it_are_found_in_the_sources_and_masked()
    {
        const string sentence = "(See src/Maf.Lab.Retrieval/Store/TenantScopedSearch.cs:121-123 and the `For` implementation at lines 138-140.)";

        var places = Citations.Find(sentence, Read);

        Assert.Equal(2, places.Count);
        Assert.All(places, p => Assert.True(p.Found));
        Assert.Equal($"(See {Citations.Verified} and the `For` implementation at lines {Citations.Verified}.)", Citations.Mask(sentence, places));
    }

    [Theory]
    [InlineData("(See src/Maf.Lab.Retrieval/Store/TenantScopedSearch.cs:88-95.)")]
    [InlineData("It lives in `src/Maf.Lab.Retrieval/Store/TenantScopedSearch.cs:121-140`.")]
    [InlineData("Източник: `src/Maf.Lab.Api/Agent/ToolDataEnvelope.cs:40-52` – дефиниция на `Open`.")]
    public void A_range_no_source_covers_is_not_found(string sentence)
    {
        var place = Assert.Single(Citations.Find(sentence, Read));

        Assert.False(place.Found);
        Assert.Equal(sentence, Citations.Mask(sentence, [place]));
    }

    [Theory]
    [InlineData("*Source: Procedure “Missing fee schedule” → Section 3 → Step 2.*", true)]
    [InlineData("*(Procedure: Missing fee schedule > Section 3: Assign a fee schedule > Step 2)*", true)]
    [InlineData("*Source: Procedure “Missing fee schedule” → Section 4 → Step 2.*", false)]
    [InlineData("*(Procedure: Missing fee schedule > Section 3: Assign a fee schedule > Step 6)*", false)]
    public void A_section_and_step_are_found_only_when_a_source_names_both(string sentence, bool found) =>
        Assert.Equal(found, Assert.Single(Citations.Find(sentence, Read)).Found);

    [Theory]
    [InlineData("Invoices between $10,000 and $25,000 are released automatically.")]
    [InlineData("The fee for 2025-2026 is unchanged.")]
    [InlineData("Open the failed run and confirm the reason.")]
    public void A_sentence_that_cites_no_place_has_none(string sentence) =>
        Assert.Empty(Citations.Find(sentence, Read));

    [Fact]
    public void A_sentence_citing_a_place_no_source_holds_is_an_unsupported_claim_whatever_jev_answers()
    {
        var input = new GradeInput("q", "It is built in `TenantFilter`. (See src/Maf.Lab.Retrieval/Store/TenantScopedSearch.cs:88-95.)", Read, []);
        var request = JevGradeRequest.Build(input, new JudgeOptions());

        var grade = JevGrade.From(request, request.Questions.Keys.ToDictionary(id => id, _ => 0.95));

        Assert.Equal(0.5, grade.Faithfulness);
        Assert.Equal([true, false], grade.SentenceSupported);
        Assert.Contains("cited but in no source: src/Maf.Lab.Retrieval/Store/TenantScopedSearch.cs:88-95", grade.Reason());
        // Jev was sent the sentence unmasked: nothing in it was found.
        Assert.Contains("88-95", request.State.AnswerSentences[1]);
    }

    [Fact]
    public void A_place_in_a_previous_turns_envelope_is_found()
    {
        var envelope = ReadItem.FromEnvelope(ToolDataEnvelope.Wrap("search_codebase",
            "{\"results\":[{\"path\":\"src/Maf.Lab.Api/Agent/Guardrail.cs\",\"startLine\":153,\"endLine\":195,\"snippet\":\"...\"}]}"));

        var place = Assert.Single(Citations.Find("It is withheld whole (src/Maf.Lab.Api/Agent/Guardrail.cs:153-195).", [envelope]));

        Assert.True(place.Found);
        Assert.False(Assert.Single(Citations.Find("See src/Maf.Lab.Api/Agent/Guardrail.cs:300-310.", [envelope])).Found);
    }

    [Fact]
    public void A_whole_item_that_is_not_json_carries_no_place()
    {
        var item = ReadItem.Whole("search_codebase", "Codebase search is unavailable right now.", Domains.Codebase);

        Assert.False(Assert.Single(Citations.Find("See src/A.cs:1-9.", [item])).Found);
    }
}
