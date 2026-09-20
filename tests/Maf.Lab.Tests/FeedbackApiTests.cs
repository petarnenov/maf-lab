using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Maf.Lab.Domain.Feedback;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Maf.Lab.Tests;

public class FeedbackApiTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Wrong_document_feedback_flags_the_turn_and_a_label_appends_one_retrieval_row()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel());
        var adam = api.ClientFor("adam", "firm-a", Role.ADVISOR);
        var done = (await ApiFactory.ChatAsync(adam, "what is the procedure when a fee schedule is missing"))[^1].Data;
        var (conversationId, turnId) = (done.GetProperty("threadId").GetString()!, done.GetProperty("result").GetProperty("turnId").GetString()!);

        var feedback = await adam.PostAsJsonAsync("/api/feedback", new FeedbackRequest(conversationId!, turnId, FeedbackKind.WrongDocument, null), Ct);
        Assert.Equal(HttpStatusCode.Accepted, feedback.StatusCode);

        var alice = api.ClientFor("alice", "firm-a", Role.FIRM_ADMIN);
        var queue = await alice.GetFromJsonAsync<List<ReviewQueueItem>>("/api/admin/feedback/queue", JsonOptions, Ct);
        var item = Assert.Single(queue!);
        Assert.Equal(turnId, item.TurnId);
        Assert.Contains(TurnSignal.NegativeFeedback, item.Signals);
        Assert.Contains(FeedbackKind.WrongDocument, item.FeedbackKinds);
        Assert.Equal("search_documents", item.ToolCalls[0].ToolName);

        var label = new LabelRequest(EvalDataset.Retrieval, null, ["shared/procedures/missing-fee-schedule.txt#section-2-fix-step-1"], null, null);
        Assert.Equal(HttpStatusCode.NoContent, (await alice.PostAsJsonAsync($"/api/admin/feedback/{turnId}/label", label, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await alice.PostAsJsonAsync($"/api/admin/feedback/{turnId}/label", label, Ct)).StatusCode);

        var rows = File.ReadAllLines(Path.Combine(api.DataDir, "evals", "retrieval.jsonl"));
        var row = JsonDocument.Parse(Assert.Single(rows)).RootElement;
        Assert.Equal($"fb-retrieval-{turnId}", row.GetProperty("id").GetString());
        Assert.Equal("what is the procedure when a fee schedule is missing", row.GetProperty("query").GetString());
        Assert.Equal("firm-a", row.GetProperty("firmId").GetString());
        Assert.Equal("shared/procedures/missing-fee-schedule.txt#section-2-fix-step-1", row.GetProperty("relevantChunkIds")[0].GetString());
    }

    [Fact]
    public async Task Production_signals_put_turns_in_the_queue()
    {
        var tools = new FakeToolSource { SearchPayloadJson = """{"results":[],"totalMatches":0,"truncated":false,"refineHint":"No matching documentation."}""" };
        var chat = new ScriptedChatClient((messages, options, n) =>
        {
            var last = messages.Last(m => m.Role == Microsoft.Extensions.AI.ChatRole.User).Text;
            if (last.Contains("zero") && !ScriptedChatClient.HasResult(messages, "search_documents"))
            {
                return ScriptedChatClient.Call("search_documents", new() { ["query"] = last });
            }
            return ScriptedChatClient.Text(last.Contains("long") ? string.Join(" ", Enumerable.Repeat("word", 300)) : "short answer");
        });
        // Forcing emulation off: models that ignore tool_choice are exactly what no_tool_on_how_why catches.
        using var api = new ApiFactory(chat, tools, emulateForcing: false);
        var adam = api.ClientFor("adam", "firm-a", Role.ADVISOR);

        await ApiFactory.ChatAsync(adam, "how does proration zero work");        // forced but returns zero results
        await ApiFactory.ChatAsync(adam, "tell me something long");              // long answer, no sources
        var c = ApiFactory.ThreadOf(await ApiFactory.ChatAsync(adam, "explain breakpoint pricing")); // how/why, model ignored tools
        await ApiFactory.ChatAsync(adam, "explain breakpoint pricing please", c); // rephrase of the previous question

        var queue = await api.ClientFor("alice", "firm-a", Role.FIRM_ADMIN).GetFromJsonAsync<List<ReviewQueueItem>>("/api/admin/feedback/queue", JsonOptions, Ct);
        var signals = queue!.SelectMany(q => q.Signals).ToHashSet();
        Assert.Contains(TurnSignal.ZeroRetrievalResults, signals);
        Assert.Contains(TurnSignal.LongAnswerWithoutSources, signals);
        Assert.Contains(TurnSignal.NoToolOnHowWhy, signals);
        Assert.Contains(TurnSignal.Rephrased, signals);

        var bobQueue = await api.ClientFor("bob", "firm-b", Role.FIRM_ADMIN).GetFromJsonAsync<List<ReviewQueueItem>>("/api/admin/feedback/queue", JsonOptions, Ct);
        Assert.Empty(bobQueue!);
    }

    [Theory]
    [InlineData("/api/admin/feedback/queue")]
    [InlineData("/api/admin/index/status")]
    [InlineData("/api/admin/index/drift")]
    public async Task Admin_endpoints_are_firm_admin_only(string path)
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel());
        foreach (var role in new[] { Role.ADVISOR, Role.OPS, Role.READ_ONLY })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await api.ClientFor("x", "firm-a", role).GetAsync(path, Ct)).StatusCode);
        }
    }

    [Fact]
    public async Task Feedback_for_someone_elses_turn_is_not_found()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel());
        var done = (await ApiFactory.ChatAsync(api.ClientFor("adam", "firm-a", Role.ADVISOR), "hello"))[^1].Data;
        var request = new FeedbackRequest(done.GetProperty("threadId").GetString()!, done.GetProperty("result").GetProperty("turnId").GetString()!, FeedbackKind.WrongAnswer, null);

        var response = await api.ClientFor("bianca", "firm-b", Role.ADVISOR).PostAsJsonAsync("/api/feedback", request, Ct);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}

/// <summary>The fourth kind, which has to be known in the domain, stored, and read back by the queue.</summary>
public class ConfirmationFeedbackTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void The_domain_knows_it()
    {
        Assert.True(FeedbackKind.IsKnown(FeedbackKind.WrongConfirmation));
        Assert.False(FeedbackKind.IsKnown("wrong_everything"));
    }

    [Fact]
    public async Task It_is_stored_and_comes_back_in_the_queue()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel());
        var adam = api.ClientFor("adam", "firm-a", Role.ADVISOR);
        var done = (await ApiFactory.ChatAsync(adam, "what is the procedure when a fee schedule is missing"))[^1].Data;
        var conversationId = done.GetProperty("threadId").GetString()!;
        var turnId = done.GetProperty("result").GetProperty("turnId").GetString()!;

        var response = await adam.PostAsJsonAsync(
            "/api/feedback",
            new FeedbackRequest(conversationId, turnId, FeedbackKind.WrongConfirmation, null),
            Ct);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var queue = await api.ClientFor("alice", "firm-a", Role.FIRM_ADMIN)
            .GetFromJsonAsync<List<ReviewQueueItem>>("/api/admin/feedback/queue", FeedbackApiTests.JsonOptions, Ct);

        Assert.Contains(queue!, item => item.TurnId == turnId && item.FeedbackKinds.Contains(FeedbackKind.WrongConfirmation));
    }
}
