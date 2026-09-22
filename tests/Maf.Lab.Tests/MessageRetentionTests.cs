using Maf.Lab.Api.Agent.Tracing;
using Maf.Lab.Api.Storage;
using Maf.Lab.Domain.Tenancy;
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
        var adam = api.ClientFor("adam", "firm-a", Role.ADVISOR);
        var old = await ApiFactory.ChatAsync(adam, "what is the procedure when a fee schedule is missing");
        var fresh = await ApiFactory.ChatAsync(adam, "and what about a failed run");
        var oldId = ApiFactory.ThreadOf(old);
        var freshId = ApiFactory.ThreadOf(fresh);

        await using (var ctx = ChatApiTests.Db(api))
        {
            await ctx.Conversations.Where(c => c.Id == oldId)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.LastActivityAt, DateTime.UtcNow.AddDays(-91)), Ct);
        }

        var removed = await api.Services.GetRequiredService<MessageRetentionService>().PurgeAsync(Ct);

        Assert.Equal(1, removed);
        await using var check = ChatApiTests.Db(api);
        Assert.False(await check.Conversations.AnyAsync(c => c.Id == oldId, Ct));
        Assert.False(await check.Messages.AnyAsync(m => m.ConversationId == oldId, Ct));
        Assert.False(await check.Turns.AnyAsync(t => t.ConversationId == oldId, Ct));
        // The one still inside its retention is untouched, messages and turns and all.
        Assert.True(await check.Conversations.AnyAsync(c => c.Id == freshId, Ct));
        Assert.True(await check.Turns.AnyAsync(t => t.ConversationId == freshId, Ct));
    }

    [Fact]
    public void The_two_retentions_are_two_settings_and_moving_one_leaves_the_other()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel())
        {
            ExtraSettings = new Dictionary<string, string?> { ["Tracing:RetentionDays"] = "3" },
        };

        var traces = api.Services.GetRequiredService<IOptions<TracingOptions>>().Value;
        var messages = api.Services.GetRequiredService<IOptions<MessageRetentionOptions>>().Value;

        Assert.Equal(3, traces.RetentionDays);
        // Unmoved by the other: what was said and how it was worked out are kept for their own reasons.
        Assert.Equal(90, messages.RetentionDays);
    }

    [Fact]
    public async Task Deleting_a_conversation_does_not_wait_for_any_retention()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel());
        var adam = api.ClientFor("adam", "firm-a", Role.ADVISOR);
        var id = ApiFactory.ThreadOf(await ApiFactory.ChatAsync(adam, "what is the procedure when a fee schedule is missing"));

        Assert.Equal(System.Net.HttpStatusCode.NoContent, (await adam.DeleteAsync($"/api/conversations/{id}", Ct)).StatusCode);

        // Gone from the list and from reading, long before its retention has anything to say about it.
        Assert.Equal(System.Net.HttpStatusCode.NotFound, (await adam.GetAsync($"/api/conversations/{id}", Ct)).StatusCode);
    }
}
