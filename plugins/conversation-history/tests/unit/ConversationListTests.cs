using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Maf.Lab.Api.Compliance;
using Maf.Lab.Api.Storage;
using Maf.Lab.Domain.History;
using Maf.Lab.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Maf.Lab.Tests;

/// <summary>The conversation list's routes (introduce-plugins 5.4): the caller's own conversations, listed, renamed and deleted.</summary>
public class ConversationListTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task List_is_owner_only_searchable_hides_empty_and_pages()
    {
        using var api = ConversationHistoryPluginSupport.Api(ApiFactory.ProceduralModel("Answer about credits and schedules."));
        var adam = api.ClientFor("adam", "firm-a", Role.USER);
        foreach (var q in new[] { "How do I issue a billing credit?", "explain breakpoint pricing", "what is proration" })
        {
            await ApiFactory.ChatAsync(adam, q);
        }
        await ApiFactory.ChatAsync(api.ClientFor("rita", "firm-a", Role.USER), "rita's question about credit");
        await ApiFactory.ChatAsync(api.ClientFor("bianca", "firm-b", Role.USER), "bianca asks about credit");
        await adam.PostAsync("/api/conversations", null, Ct); // empty conversation

        var all = await adam.GetFromJsonAsync<ConversationPage>("/api/conversations", Json, Ct);
        Assert.Equal(["what is proration", "explain breakpoint pricing", "How do I issue a billing credit?"], all!.Conversations.Select(c => c.Title));
        Assert.All(all.Conversations, c => Assert.Equal(1, c.TurnCount));

        // Case-insensitive; matches the title and, through the scripted answer ("credits"), every one of Adam's conversations,
        // but never Rita's or Bianca's.
        var search = await adam.GetFromJsonAsync<ConversationPage>("/api/conversations?search=CREDIT", Json, Ct);
        Assert.Equal(3, search!.Conversations.Count);
        var byQuestion = await adam.GetFromJsonAsync<ConversationPage>("/api/conversations?search=breakpoint", Json, Ct);
        Assert.Equal(["explain breakpoint pricing"], byQuestion!.Conversations.Select(c => c.Title));
        var nothing = await adam.GetFromJsonAsync<ConversationPage>("/api/conversations?search=bianca", Json, Ct);
        Assert.Empty(nothing!.Conversations);

        var first = await adam.GetFromJsonAsync<ConversationPage>("/api/conversations?limit=2", Json, Ct);
        Assert.Equal(2, first!.Conversations.Count);
        Assert.NotNull(first.NextCursor);
        // A second client — which is what another replica is, from the cursor's point of view — continues it.
        var second = await api.ClientFor("adam", "firm-a", Role.USER).GetFromJsonAsync<ConversationPage>(
            $"/api/conversations?limit=2&before={Uri.EscapeDataString(first.NextCursor!)}", Json, Ct);
        Assert.Equal(["How do I issue a billing credit?"], second!.Conversations.Select(c => c.Title));
        Assert.Null(second.NextCursor);
    }

    [Fact]
    public async Task An_untitled_conversation_is_listed_by_its_first_question()
    {
        using var api = ConversationHistoryPluginSupport.Api(ApiFactory.ProceduralModel());
        var adam = api.ClientFor("adam", "firm-a", Role.USER);
        await using (var ctx = ChatApiTests.Db(api))
        {
            // A conversation stored before titles existed: Title is null although it already has a turn.
            ctx.Conversations.Add(new ConversationRow { Id = "c_untitled", UserId = "adam", TenantId = "firm-a", CreatedAt = DateTime.UtcNow.AddHours(-2), LastActivityAt = DateTime.UtcNow.AddHours(-2) });
            ctx.Turns.Add(new TurnRow { Id = "t_first", ConversationId = "c_untitled", UserId = "adam", TenantId = "firm-a", Question = "the original question", Answer = "a", CreatedAt = DateTime.UtcNow.AddHours(-2) });
            await ctx.SaveChangesAsync(Ct);
        }

        await ApiFactory.ChatAsync(adam, "a much later follow-up question", "c_untitled");

        var list = await adam.GetFromJsonAsync<ConversationPage>("/api/conversations", Json, Ct);
        var conversation = Assert.Single(list!.Conversations);
        Assert.Equal("the original question", conversation.Title);
        Assert.Equal(2, conversation.TurnCount);
    }

    [Fact]
    public async Task Rename_validates_and_delete_hides_it_from_the_list_and_is_recorded_once()
    {
        using var api = ConversationHistoryPluginSupport.Api(ApiFactory.ProceduralModel());
        var adam = api.ClientFor("adam", "firm-a", Role.USER);
        var rita = api.ClientFor("rita", "firm-a", Role.USER);
        var conversationId = ApiFactory.ThreadOf(await ApiFactory.ChatAsync(adam, "explain breakpoint pricing"));
        var url = $"/api/conversations/{conversationId}";

        var blank = await adam.PatchAsJsonAsync(url, new RenameConversationRequest("   "), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);
        Assert.Contains($"title must be 1–{ConversationTitles.MaxChars} characters.", await blank.Content.ReadAsStringAsync(Ct));
        Assert.Equal(HttpStatusCode.BadRequest, (await adam.PatchAsJsonAsync(url, new RenameConversationRequest(new string('x', 121)), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await rita.PatchAsJsonAsync(url, new RenameConversationRequest("x"), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await adam.PatchAsJsonAsync(url, new RenameConversationRequest("  Breakpoints for Smith  "), Ct)).StatusCode);
        Assert.Equal("Breakpoints for Smith", (await adam.GetFromJsonAsync<ConversationDetail>(url, Json, Ct))!.Title);
        Assert.Equal("Breakpoints for Smith", (await adam.GetFromJsonAsync<ConversationPage>("/api/conversations", Json, Ct))!.Conversations.Single().Title);

        // Not Adam's conversation: refused, and attributed to nobody.
        Assert.Equal(HttpStatusCode.NotFound, (await rita.DeleteAsync(url, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await adam.DeleteAsync(url, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await adam.DeleteAsync(url, Ct)).StatusCode);

        Assert.Empty((await adam.GetFromJsonAsync<ConversationPage>("/api/conversations", Json, Ct))!.Conversations);
        await using var db = ChatApiTests.Db(api);
        var deletion = Assert.Single(await db.Audit.AsNoTracking().Where(a => a.Kind == AuditKinds.ConversationDelete).ToListAsync(Ct));
        Assert.Equal(("adam", conversationId), (deletion.PrincipalId, deletion.ConversationId));
    }

    [Fact]
    public async Task Nobody_signed_in_reaches_the_list()
    {
        using var api = ConversationHistoryPluginSupport.Api(ApiFactory.ProceduralModel());
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.CreateClient().GetAsync("/api/conversations", Ct)).StatusCode);
    }
}
