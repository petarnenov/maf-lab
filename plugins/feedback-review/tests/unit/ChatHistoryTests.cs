using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Maf.Lab.Api.Storage;
using Maf.Lab.Domain.Feedback;
using Maf.Lab.Domain.History;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Domain.Tracing;
using Maf.Lab.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Maf.Lab.Tests;

public partial class ChatHistoryTests
{
    [Fact]
    public async Task A_deleted_conversation_cannot_be_opened_or_continued_and_stays_in_the_review_queue()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel()) { InstalledPlugins = FeedbackReviewPluginSupport.Installed };
        var adam = api.ClientFor("adam", "firm-a", Role.USER);
        var done = (await ApiFactory.ChatAsync(adam, "explain breakpoint pricing"))[^1].Data;
        var (conversationId, turnId) = (done.GetProperty("threadId").GetString()!, done.GetProperty("runId").GetString()!);
        await adam.PostAsJsonAsync("/api/feedback", new FeedbackRequest(conversationId, turnId, FeedbackKind.WrongAnswer, null), Ct);

        // Deleted through the core's store, as the list plugin does it (decision 5y).
        Assert.True(await api.ConversationsOf("adam", "firm-a").DeleteAsync(conversationId, Ct));

        Assert.Equal(HttpStatusCode.NotFound, (await adam.GetAsync($"/api/conversations/{conversationId}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await adam.PostAsJsonAsync("/api/chat", new
        {
            threadId = conversationId,
            runId = "r_after_delete",
            messages = new[] { new { id = "u1", role = "user", content = "more?" } },
        }, Ct)).StatusCode);

        var queue = await api.ClientFor("alice", "firm-a", Role.TENANT_ADMIN).GetFromJsonAsync<List<ReviewQueueItem>>("/api/admin/feedback/queue", Json, Ct);
        Assert.Contains(queue!, q => q.TurnId == turnId);
    }

}
