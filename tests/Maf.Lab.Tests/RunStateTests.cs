using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Maf.Lab.Domain.Billing;
using Maf.Lab.Domain.SharedState;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.TestSupport;
using Microsoft.Extensions.AI;

namespace Maf.Lab.Tests;

/// <summary>Closing the tab is not losing the turn: a run can be rejoined, from whichever replica answers.</summary>
public class RunStateTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task A_finished_run_says_what_it_said_and_which_tools_it_called()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel("Assign the schedule and re-run."));
        var adam = api.ClientFor("adam", "firm-a", Role.ADVISOR);

        await ApiFactory.ChatAsync(adam, "what is the procedure when a fee schedule is missing", runId: "r_rejoin");

        var state = await adam.GetFromJsonAsync<RunState>("/api/chat/r_rejoin", Json, Ct);
        Assert.Equal(RunOutcomes.Answered, state!.Outcome);
        Assert.Contains("Assign the schedule", state.Answer);
        var call = Assert.Single(state.ToolCalls);
        Assert.Equal("search_documents", call.ToolName);
        Assert.True(call.Finished);
        Assert.False(call.IsError);
        // The summary is the one the client was given: identifiers and counts, never a document's text.
        Assert.DoesNotContain("FS-REQUIRED", call.ResultSummary ?? "");
        Assert.NotNull(state.TurnId);
    }

    [Fact]
    public async Task A_run_that_stopped_for_a_person_says_so_and_names_what_is_waiting()
    {
        var chat = new ScriptedChatClient((messages, _, _) =>
            messages.LastOrDefault(m => m.Role == ChatRole.User)?.Text?.Contains("adjust", StringComparison.OrdinalIgnoreCase) == true
            && !ScriptedChatClient.HasResult(messages, FeeAdjustmentTool.Name)
                ? ScriptedChatClient.Call(FeeAdjustmentTool.Name, new()
                {
                    ["accountId"] = "A-1042", ["amount"] = -200m, ["reason"] = "overcharged in Q2",
                })
                : ScriptedChatClient.Text("Done."));
        using var api = new ApiFactory(chat, new FakeToolSource())
        {
            ExtraSettings = new Dictionary<string, string?> { ["Compliance:BaseUrl"] = "" },
        };
        var adam = api.ClientFor("adam", "firm-a", Role.ADVISOR);

        var events = await ApiFactory.ChatAsync(adam, "adjust the fee on A-1042 down by 200", runId: "r_waiting");
        var interrupt = ApiFactory.InterruptOf(events);
        Assert.NotNull(interrupt);

        var state = await adam.GetFromJsonAsync<RunState>("/api/chat/r_waiting", Json, Ct);
        Assert.Equal(RunOutcomes.AwaitingPerson, state!.Outcome);
        Assert.Equal(interrupt.Value.GetProperty("id").GetString(), state.AwaitingId);
    }

    [Fact]
    public async Task A_run_of_another_principal_and_a_run_nobody_kept_are_both_not_found()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel());
        var adam = api.ClientFor("adam", "firm-a", Role.ADVISOR);
        await ApiFactory.ChatAsync(adam, "what is the procedure when a fee schedule is missing", runId: "r_mine");

        // Another user of the same firm, and another firm entirely.
        Assert.Equal(HttpStatusCode.NotFound,
            (await api.ClientFor("rita", "firm-a", Role.ADVISOR).GetAsync("/api/chat/r_mine", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await api.ClientFor("bob", "firm-b", Role.FIRM_ADMIN).GetAsync("/api/chat/r_mine", Ct)).StatusCode);

        // And a run whose state is no longer kept is not an empty run; it is not there.
        api.Runs.Forget("r_mine");
        Assert.Equal(HttpStatusCode.NotFound, (await adam.GetAsync("/api/chat/r_mine", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await adam.GetAsync("/api/chat/r_never_ran", Ct)).StatusCode);
    }

    [Fact]
    public async Task A_run_that_failed_says_so_with_the_words_the_client_was_given()
    {
        var chat = new ScriptedChatClient((_, _, _) => throw new InvalidOperationException("the model is down"));
        using var api = new ApiFactory(chat);
        var adam = api.ClientFor("adam", "firm-a", Role.ADVISOR);

        await ApiFactory.ChatAsync(adam, "anything at all", runId: "r_failed");

        var state = await adam.GetFromJsonAsync<RunState>("/api/chat/r_failed", Json, Ct);
        Assert.Equal(RunOutcomes.Failed, state!.Outcome);
        Assert.NotNull(state.Error);
        // What the person was told, not what went wrong inside.
        Assert.DoesNotContain("InvalidOperationException", state.Error);
    }
}
