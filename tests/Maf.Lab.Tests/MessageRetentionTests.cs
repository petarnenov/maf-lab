using Maf.Lab.Api.Agent.Tracing;
using Maf.Lab.Api.Storage;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Api.DataLifecycle;
using Maf.Lab.Plugins.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Maf.Lab.Tests;

/// <summary>
/// Message content has one retention wherever it is kept, and it is not the trace's. They answer different
/// questions — how long what was said is kept, and how long the working of a turn is kept — and move apart.
/// </summary>
public class MessageRetentionTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_conversation_past_its_retention_goes_with_its_messages_and_its_turns()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel());
        var adam = api.ClientFor("adam", "firm-a", Role.USER);
        var old = await ApiFactory.ChatAsync(adam, "what is the procedure when a fee schedule is missing");
        var fresh = await ApiFactory.ChatAsync(adam, "and what about a failed run");
        var oldId = ApiFactory.ThreadOf(old);
        var freshId = ApiFactory.ThreadOf(fresh);

        await using (var ctx = ChatApiTests.Db(api))
        {
            var oldTurn = await ctx.Turns.SingleAsync(t => t.ConversationId == oldId, Ct);
            var freshTurn = await ctx.Turns.SingleAsync(t => t.ConversationId == freshId, Ct);
            ctx.Labels.AddRange(
                new LabelRow { Id = "retention-old-label", TurnId = oldTurn.Id, TenantId = "firm-a", ReviewerId = "bianca",
                    Dataset = "fixture", RowJson = "{\"question\":\"private old question\"}", CreatedAt = DateTime.UtcNow },
                new LabelRow { Id = "retention-fresh-label", TurnId = freshTurn.Id, TenantId = "firm-a", ReviewerId = "bianca",
                    Dataset = "fixture", RowJson = "{\"question\":\"private fresh question\"}", CreatedAt = DateTime.UtcNow });
            ctx.Feedback.Add(new FeedbackRow { Id = "retention-feedback", TurnId = oldTurn.Id, ConversationId = oldId,
                TenantId = "firm-a", UserId = "adam", Kind = "negative", Comment = "private feedback", CreatedAt = DateTime.UtcNow });
            await ctx.SaveChangesAsync(Ct);
            await ctx.Conversations.Where(c => c.Id == oldId)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.LastActivityAt, DateTime.UtcNow.AddDays(-91)), Ct);
        }

        var removed = await api.Services.GetRequiredService<MessageRetentionService>().PurgeAsync(Ct);

        Assert.Equal(1, removed);
        await using var check = ChatApiTests.Db(api);
        Assert.False(await check.Conversations.AnyAsync(c => c.Id == oldId, Ct));
        Assert.False(await check.Messages.AnyAsync(m => m.ConversationId == oldId, Ct));
        Assert.False(await check.Turns.AnyAsync(t => t.ConversationId == oldId, Ct));
        Assert.False(await check.Labels.AnyAsync(l => l.Id == "retention-old-label", Ct));
        Assert.False(await check.Feedback.AnyAsync(f => f.Id == "retention-feedback", Ct));
        Assert.True(await check.Labels.AnyAsync(l => l.Id == "retention-fresh-label", Ct));
        // The one still inside its retention is untouched, messages and turns and all.
        Assert.True(await check.Conversations.AnyAsync(c => c.Id == freshId, Ct));
        Assert.True(await check.Turns.AnyAsync(t => t.ConversationId == freshId, Ct));
        // A different reviewer's copied question does not survive retention and escape the user's later erasure.
        var lifecycle = api.Services.GetRequiredService<CoreDataLifecycle>();
        var scope = new DataLifecycleScope(TenantId.Firm("firm-a"), "adam");
        var exported = new List<DataExportRecord>();
        await foreach (var record in lifecycle.ExportAsync(scope, Ct)) exported.Add(record);
        Assert.DoesNotContain(exported, record => record.Data.GetRawText().Contains("private old question", StringComparison.Ordinal));
        await lifecycle.DeleteAsync(scope, Ct);
        Assert.False(await check.Labels.AnyAsync(l => l.TenantId == "firm-a", Ct));
    }


    [Fact]
    public async Task Deleting_a_conversation_does_not_wait_for_any_retention()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel());
        var adam = api.ClientFor("adam", "firm-a", Role.USER);
        var id = ApiFactory.ThreadOf(await ApiFactory.ChatAsync(adam, "what is the procedure when a fee schedule is missing"));

        Assert.True(await api.ConversationsOf("adam", "firm-a").DeleteAsync(id, Ct));

        // Gone from reading, long before its retention has anything to say about it.
        Assert.Equal(System.Net.HttpStatusCode.NotFound, (await adam.GetAsync($"/api/conversations/{id}", Ct)).StatusCode);
    }
}
