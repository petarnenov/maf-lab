using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Maf.Lab.Domain.Feedback;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maf.Lab.Tests;

public partial class FeedbackApiTests
{
    [Fact]
    public async Task Wrong_document_feedback_flags_the_turn_and_a_label_appends_one_retrieval_row()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel()) { InstalledPlugins = FeedbackReviewPluginSupport.Installed };
        var export = new RecordingDataset();
        api.ConfigureTestServices = services => services.AddSingleton<Maf.Lab.Plugins.Abstractions.IAppendEvalDataset>(export);
        var adam = api.ClientFor("adam", "firm-a", Role.USER);
        var done = (await ApiFactory.ChatAsync(adam, "what is the procedure when a fee schedule is missing"))[^1].Data;
        var (conversationId, turnId) = (done.GetProperty("threadId").GetString()!, done.GetProperty("runId").GetString()!);

        var feedback = await adam.PostAsJsonAsync("/api/feedback", new FeedbackRequest(conversationId!, turnId, FeedbackKind.WrongDocument, null), Ct);
        Assert.Equal(HttpStatusCode.Accepted, feedback.StatusCode);

        var alice = api.ClientFor("alice", "firm-a", Role.TENANT_ADMIN);
        var queue = await alice.GetFromJsonAsync<List<ReviewQueueItem>>("/api/admin/feedback/queue", JsonOptions, Ct);
        var item = Assert.Single(queue!);
        Assert.Equal(turnId, item.TurnId);
        Assert.Contains(TurnSignal.NegativeFeedback, item.Signals);
        Assert.Contains(FeedbackKind.WrongDocument, item.FeedbackKinds);
        Assert.Equal("search_documents", item.ToolCalls[0].ToolName);

        var label = new LabelRequest(EvalDataset.Retrieval, null, ["shared/procedures/missing-fee-schedule.txt#section-2-fix-step-1"], null, null);
        Assert.Equal(HttpStatusCode.NoContent, (await alice.PostAsJsonAsync($"/api/admin/feedback/{turnId}/label", label, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await alice.PostAsJsonAsync($"/api/admin/feedback/{turnId}/label", label, Ct)).StatusCode);

        var row = JsonDocument.Parse(Assert.Single(export.Rows.Values)).RootElement;
        Assert.Equal($"fb-retrieval-{turnId}", row.GetProperty("id").GetString());
        Assert.Equal("what is the procedure when a fee schedule is missing", row.GetProperty("query").GetString());
        Assert.Equal("firm-a", row.GetProperty("tenantId").GetString());
        Assert.Equal("shared/procedures/missing-fee-schedule.txt#section-2-fix-step-1", row.GetProperty("relevantChunkIds")[0].GetString());
    }

}

internal sealed class RecordingDataset : Maf.Lab.Plugins.Abstractions.IAppendEvalDataset
{
    public Dictionary<string, string> Rows { get; } = [];
    public Task<bool> AppendAsync(string dataset, System.Text.Json.Nodes.JsonObject row, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(Rows.TryAdd(row["id"]!.GetValue<string>(), row.ToJsonString()));
    }
}
