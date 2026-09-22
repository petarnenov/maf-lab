using System.Net;
using System.Net.Http.Json;
using Maf.Lab.Domain.Billing;
using Maf.Lab.Domain.History;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Retrieval.Billing;

namespace Maf.Lab.Tests;

/// <summary>
/// A handle this system hands out and expects back is either self-describing — signed, so it cannot be altered —
/// or it lives in the shared store. One that is a key into a replica's memory is a session under another name,
/// and the next request reaches a different replica.
/// </summary>
public class HandleTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly System.Text.Json.JsonSerializerOptions Json = new(System.Text.Json.JsonSerializerDefaults.Web);

    [Fact]
    public async Task A_cursor_carries_its_own_meaning_and_pages_on_wherever_it_is_presented()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel());
        var adam = api.ClientFor("adam", "firm-a", Role.ADVISOR);
        for (var i = 0; i < 3; i++)
        {
            await ApiFactory.ChatAsync(adam, $"what is the procedure when a fee schedule is missing, case {i}");
        }

        var first = await adam.GetFromJsonAsync<ConversationPage>("/api/conversations?limit=2", Json, Ct);
        Assert.Equal(2, first!.Conversations.Count);
        Assert.NotNull(first.NextCursor);

        // A second client — which is what another replica is, from the cursor's point of view — continues it.
        var second = api.ClientFor("adam", "firm-a", Role.ADVISOR);
        var next = await second.GetFromJsonAsync<ConversationPage>(
            $"/api/conversations?limit=2&before={Uri.EscapeDataString(first.NextCursor!)}", Json, Ct);

        Assert.Single(next!.Conversations);
        Assert.DoesNotContain(next.Conversations, c => first.Conversations.Any(f => f.ConversationId == c.ConversationId));
    }

    [Fact]
    public void A_proposal_state_carries_its_own_meaning_and_is_refused_when_altered()
    {
        var time = TimeProvider.System;
        var signer = new ProposalSigner("a-test-signing-key-that-is-long-enough", time);
        var state = signer.Issue(new FeeAdjustmentProposalState(
            "adj_1", "firm-a", "adam", "A-1042", -200m, "USD", "digest", time.GetUtcNow(), time.GetUtcNow().AddMinutes(30)));

        // Read by anyone holding the key, with nothing looked up anywhere.
        Assert.IsType<ProposalCheck.Ok>(signer.Verify(state));

        // And a changed one is refused rather than acted on.
        var altered = state[..^4] + "aaaa";
        Assert.IsNotType<ProposalCheck.Ok>(signer.Verify(altered));
    }

    [Fact]
    public async Task A_run_handle_is_understood_by_a_replica_that_never_saw_the_run()
    {
        // The run id is the other kind: not self-describing, so it lives in the shared store — which is what
        // makes it answerable by a replica that did not serve the run.
        using var api = new ApiFactory(ApiFactory.ProceduralModel());
        var adam = api.ClientFor("adam", "firm-a", Role.ADVISOR);
        await ApiFactory.ChatAsync(adam, "what is the procedure when a fee schedule is missing", runId: "r_handle");

        var elsewhere = api.ClientFor("adam", "firm-a", Role.ADVISOR);
        Assert.Equal(HttpStatusCode.OK, (await elsewhere.GetAsync("/api/chat/r_handle", Ct)).StatusCode);
    }
}
