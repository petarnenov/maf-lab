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

public class ChatHistoryTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Old_database_gains_new_columns_concurrently_and_last_activity_is_backfilled()
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory("maf-hist-").FullName, "old.db");
        var factory = new PooledDbContextFactory<MafDbContext>(new DbContextOptionsBuilder<MafDbContext>()
            .UseSqlite($"Data Source={path}").AddInterceptors(new SqlitePragmaInterceptor()).Options);
        await using (var ctx = await factory.CreateDbContextAsync(Ct))
        {
            // The pre-history schema: Conversations without Title/LastActivityAt/DeletedAt, and one turn.
            await ctx.Database.ExecuteSqlRawAsync("""CREATE TABLE "Conversations" ("Id" TEXT NOT NULL CONSTRAINT "PK_Conversations" PRIMARY KEY, "UserId" TEXT NOT NULL, "TenantId" TEXT NOT NULL, "CreatedAt" TEXT NOT NULL)""", Ct);
            await ctx.Database.ExecuteSqlRawAsync("""INSERT INTO "Conversations" VALUES ('c_old', 'adam', 'firm-a', '2026-09-01 10:00:00')""", Ct);
        }
        await Task.WhenAll(Enumerable.Range(0, 3).Select(async _ =>
        {
            await using var ctx = await factory.CreateDbContextAsync(Ct);
            await DatabaseInitializer.InitializeAsync(ctx, Ct);
        }));
        await using (var ctx = await factory.CreateDbContextAsync(Ct))
        {
            ctx.Turns.Add(new TurnRow { Id = "t1", ConversationId = "c_old", UserId = "adam", TenantId = "firm-a", Question = "q", CreatedAt = new DateTime(2026, 9, 3, 8, 0, 0, DateTimeKind.Utc) });
            await ctx.SaveChangesAsync(Ct);
            await ctx.Database.ExecuteSqlRawAsync("""UPDATE "Conversations" SET "LastActivityAt" = '' WHERE "Id" = 'c_old'""", Ct);
            await DatabaseInitializer.InitializeAsync(ctx, Ct);
        }
        await using var check = await factory.CreateDbContextAsync(Ct);
        var row = await check.Conversations.SingleAsync(Ct);
        Assert.Null(row.Title);
        Assert.Null(row.DeletedAt);
        Assert.Equal(new DateTime(2026, 9, 3, 8, 0, 0), row.LastActivityAt);
    }

    [Theory]
    [InlineData("How do I issue a billing credit?", "How do I issue a billing credit?")]
    [InlineData("  How   do I\nissue  a credit? ", "How do I issue a credit?")]
    public void Default_title_is_the_first_question_normalised(string question, string expected) =>
        Assert.Equal(expected, ConversationTitles.FromQuestion(question));

    [Fact]
    public void Long_default_title_is_cut_at_a_word_boundary()
    {
        var question = "What is the exact procedure to follow when a household fee schedule is missing for several accounts in the June billing run?";
        var title = ConversationTitles.FromQuestion(question);
        Assert.True(title.Length <= ConversationTitles.DefaultMaxChars);
        Assert.EndsWith("…", title);
        Assert.StartsWith(title[..^1], question);
        Assert.Equal(' ', question[title.Length - 1]);
    }

    [Fact]
    public async Task Opening_restores_tools_sources_feedback_and_trace_flag_and_is_owner_only()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel("Assign the schedule and re-run."));
        var adam = api.ClientFor("adam", "firm-a", Role.USER);
        var done = (await ApiFactory.ChatAsync(adam, "what is the procedure when a fee schedule is missing"))[^1].Data;
        var (conversationId, turnId) = (done.GetProperty("threadId").GetString()!, done.GetProperty("runId").GetString()!);
        await adam.PostAsJsonAsync("/api/feedback", new FeedbackRequest(conversationId, turnId, FeedbackKind.WrongDocument, null), Ct);

        var detail = await adam.GetFromJsonAsync<ConversationDetail>($"/api/conversations/{conversationId}", Json, Ct);
        var turn = Assert.Single(detail!.Turns);
        Assert.Equal("Assign the schedule and re-run.", turn.Answer);
        var tool = Assert.Single(turn.ToolCalls);
        Assert.Equal("search_documents", tool.ToolName);
        Assert.Equal("2 snippet(s)", tool.ResultSummary);
        Assert.False(string.IsNullOrEmpty(tool.CallId));
        Assert.Equal(2, turn.Sources.Count);
        Assert.Equal("procedures/missing-fee-schedule.txt", turn.Sources[0].SourcePath);
        Assert.Contains("FS-REQUIRED", turn.Sources[0].Snippet);
        Assert.Equal([FeedbackKind.WrongDocument], turn.FeedbackKinds);
        // A model that does not reason leaves the turn without reasoning (introduce-plugins 5.3).
        Assert.Null(turn.Reasoning);

        Assert.Equal(HttpStatusCode.NotFound, (await api.ClientFor("rita", "firm-a", Role.USER).GetAsync($"/api/conversations/{conversationId}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.ClientFor("adam", "firm-b", Role.USER).GetAsync($"/api/conversations/{conversationId}", Ct)).StatusCode);
    }

    [Fact]
    public async Task Turns_stored_before_history_still_open()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel());
        var adam = api.ClientFor("adam", "firm-a", Role.USER);
        await using (var ctx = ChatApiTests.Db(api))
        {
            ctx.Conversations.Add(new ConversationRow { Id = "c_legacy", UserId = "adam", TenantId = "firm-a", CreatedAt = DateTime.UtcNow, LastActivityAt = DateTime.UtcNow });
            ctx.Turns.Add(new TurnRow
            {
                Id = "t_legacy", ConversationId = "c_legacy", UserId = "adam", TenantId = "firm-a", Question = "old question", Answer = "old answer",
                ToolCallsJson = """[{"toolName":"search_documents","argumentSummary":"","outcome":"ok","sourceCount":1,"docIds":["shared/docs/a.md"],"chunkIds":[]}]""",
                SourcesJson = """[{"docId":"shared/docs/a.md","sectionPath":"A > B"}]""",
                CreatedAt = DateTime.UtcNow,
            });
            await ctx.SaveChangesAsync(Ct);
        }
        var detail = await adam.GetFromJsonAsync<ConversationDetail>("/api/conversations/c_legacy", Json, Ct);
        var turn = Assert.Single(detail!.Turns);
        Assert.Equal("old question", detail.Title);
        Assert.Null(turn.ToolCalls[0].ResultSummary);
        Assert.Null(turn.ToolCalls[0].CallId);
        Assert.Equal(("shared/docs/a.md", "A > B", "", ""), (turn.Sources[0].DocId, turn.Sources[0].SectionPath, turn.Sources[0].SourcePath, turn.Sources[0].Snippet));
        Assert.Null(turn.Reasoning);
    }

    [Fact]
    public async Task Continuing_an_untitled_conversation_keeps_the_first_question_as_its_title()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel());
        var adam = api.ClientFor("adam", "firm-a", Role.USER);
        await using (var ctx = ChatApiTests.Db(api))
        {
            // A conversation stored before titles existed: Title is null although it already has a turn.
            ctx.Conversations.Add(new ConversationRow { Id = "c_untitled", UserId = "adam", TenantId = "firm-a", CreatedAt = DateTime.UtcNow.AddHours(-2), LastActivityAt = DateTime.UtcNow.AddHours(-2) });
            ctx.Turns.Add(new TurnRow { Id = "t_first", ConversationId = "c_untitled", UserId = "adam", TenantId = "firm-a", Question = "the original question", Answer = "a", CreatedAt = DateTime.UtcNow.AddHours(-2) });
            await ctx.SaveChangesAsync(Ct);
        }

        await ApiFactory.ChatAsync(adam, "a much later follow-up question", "c_untitled");

        Assert.Equal("the original question", (await adam.GetFromJsonAsync<ConversationDetail>("/api/conversations/c_untitled", Json, Ct))!.Title);
    }

    [Fact]
    public async Task A_deleted_conversation_cannot_be_opened_or_continued_and_stays_in_the_review_queue()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel());
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

    [Fact]
    public async Task Without_the_list_plugin_the_list_rename_and_delete_routes_are_absent()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel());
        var adam = api.ClientFor("adam", "firm-a", Role.USER);
        var id = ApiFactory.ThreadOf(await ApiFactory.ChatAsync(adam, "explain breakpoint pricing"));

        // Not served: the paths stay the core's for their other verbs (POST creates, GET /{id} reopens), so 405.
        // Each 405 names the methods the core does serve there (RFC 9110 §15.5.6).
        var list = await adam.GetAsync("/api/conversations", Ct);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, list.StatusCode);
        Assert.Equal(["POST"], list.Content.Headers.Allow);
        foreach (var other in new[]
        {
            await adam.PatchAsJsonAsync($"/api/conversations/{id}", new RenameConversationRequest("x"), Ct),
            await adam.DeleteAsync($"/api/conversations/{id}", Ct),
        })
        {
            Assert.Equal(HttpStatusCode.MethodNotAllowed, other.StatusCode);
            Assert.Equal(["GET"], other.Content.Headers.Allow);
        }
        // Reopening by its URL is the core's, whatever the screen.
        Assert.Equal(HttpStatusCode.OK, (await adam.GetAsync($"/api/conversations/{id}", Ct)).StatusCode);
    }

    [Fact]
    public async Task Continuing_uses_the_earlier_turns_and_moves_the_conversation_to_the_top()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel("First answer."));
        var adam = api.ClientFor("adam", "firm-a", Role.USER);
        var first = ApiFactory.ThreadOf(await ApiFactory.ChatAsync(adam, "explain breakpoint pricing"));
        await ApiFactory.ChatAsync(adam, "what is proration");

        // "Reload": a fresh client continues the first conversation by id.
        var events = await ApiFactory.ChatAsync(api.ClientFor("adam", "firm-a", Role.USER), "and for new accounts?", first);
        var history = ApiFactory.TracesOf(events).Select(t => t.Deserialize<TraceEvent>(Json)!).Single(t => t.Kind == TraceKinds.History);
        Assert.Contains("explain breakpoint pricing", history.Data.GetProperty("included").GetRawText());

        // The conversation moves to the top of the caller's own (the core's store, as the list plugin reads it).
        var list = await api.ConversationsOf("adam", "firm-a").PageAsync(null, 30, null, Ct);
        Assert.Equal(first, list.Conversations[0].ConversationId);
        Assert.Equal(2, list.Conversations[0].TurnCount);
        // The title stays the first question, even when the conversation had no stored title before being continued.
        Assert.Equal("explain breakpoint pricing", list.Conversations[0].Title);
    }
}
