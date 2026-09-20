using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Maf.Lab.Api.History;
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
            await ctx.Database.ExecuteSqlRawAsync("""CREATE TABLE "Conversations" ("Id" TEXT NOT NULL CONSTRAINT "PK_Conversations" PRIMARY KEY, "UserId" TEXT NOT NULL, "FirmId" TEXT NOT NULL, "CreatedAt" TEXT NOT NULL)""", Ct);
            await ctx.Database.ExecuteSqlRawAsync("""INSERT INTO "Conversations" VALUES ('c_old', 'adam', 'firm-a', '2026-09-01 10:00:00')""", Ct);
        }
        await Task.WhenAll(Enumerable.Range(0, 3).Select(async _ =>
        {
            await using var ctx = await factory.CreateDbContextAsync(Ct);
            await DatabaseInitializer.InitializeAsync(ctx, Ct);
        }));
        await using (var ctx = await factory.CreateDbContextAsync(Ct))
        {
            ctx.Turns.Add(new TurnRow { Id = "t1", ConversationId = "c_old", UserId = "adam", FirmId = "firm-a", Question = "q", CreatedAt = new DateTime(2026, 9, 3, 8, 0, 0, DateTimeKind.Utc) });
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
    public async Task List_is_owner_only_searchable_hides_empty_and_pages()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel("Answer about credits and schedules."));
        var adam = api.ClientFor("adam", "firm-a", Role.ADVISOR);
        foreach (var q in new[] { "How do I issue a billing credit?", "explain breakpoint pricing", "what is proration" })
        {
            await ApiFactory.ChatAsync(adam, q);
        }
        await ApiFactory.ChatAsync(api.ClientFor("rita", "firm-a", Role.ADVISOR), "rita's question about credit");
        await ApiFactory.ChatAsync(api.ClientFor("bianca", "firm-b", Role.ADVISOR), "bianca asks about credit");
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
        var second = await adam.GetFromJsonAsync<ConversationPage>($"/api/conversations?limit=2&before={Uri.EscapeDataString(first.NextCursor!)}", Json, Ct);
        Assert.Equal(["How do I issue a billing credit?"], second!.Conversations.Select(c => c.Title));
        Assert.Null(second.NextCursor);
    }

    [Fact]
    public async Task Opening_restores_tools_sources_feedback_and_trace_flag_and_is_owner_only()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel("Assign the schedule and re-run."));
        var adam = api.ClientFor("adam", "firm-a", Role.ADVISOR);
        var done = (await ApiFactory.ChatAsync(adam, "what is the procedure when a fee schedule is missing"))[^1].Data;
        var (conversationId, turnId) = (done.GetProperty("threadId").GetString()!, done.GetProperty("result").GetProperty("turnId").GetString()!);
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
        Assert.True(turn.TraceAvailable);

        Assert.Equal(HttpStatusCode.NotFound, (await api.ClientFor("rita", "firm-a", Role.ADVISOR).GetAsync($"/api/conversations/{conversationId}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.ClientFor("adam", "firm-b", Role.ADVISOR).GetAsync($"/api/conversations/{conversationId}", Ct)).StatusCode);
    }

    [Fact]
    public async Task Turns_stored_before_history_still_open()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel());
        var adam = api.ClientFor("adam", "firm-a", Role.ADVISOR);
        await using (var ctx = ChatApiTests.Db(api))
        {
            ctx.Conversations.Add(new ConversationRow { Id = "c_legacy", UserId = "adam", FirmId = "firm-a", CreatedAt = DateTime.UtcNow, LastActivityAt = DateTime.UtcNow });
            ctx.Turns.Add(new TurnRow
            {
                Id = "t_legacy", ConversationId = "c_legacy", UserId = "adam", FirmId = "firm-a", Question = "old question", Answer = "old answer",
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
        Assert.False(turn.TraceAvailable);
    }

    [Fact]
    public async Task Continuing_an_untitled_conversation_keeps_the_first_question_as_its_title()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel());
        var adam = api.ClientFor("adam", "firm-a", Role.ADVISOR);
        await using (var ctx = ChatApiTests.Db(api))
        {
            // A conversation stored before titles existed: Title is null although it already has a turn.
            ctx.Conversations.Add(new ConversationRow { Id = "c_untitled", UserId = "adam", FirmId = "firm-a", CreatedAt = DateTime.UtcNow.AddHours(-2), LastActivityAt = DateTime.UtcNow.AddHours(-2) });
            ctx.Turns.Add(new TurnRow { Id = "t_first", ConversationId = "c_untitled", UserId = "adam", FirmId = "firm-a", Question = "the original question", Answer = "a", CreatedAt = DateTime.UtcNow.AddHours(-2) });
            await ctx.SaveChangesAsync(Ct);
        }

        await ApiFactory.ChatAsync(adam, "a much later follow-up question", "c_untitled");

        var list = await adam.GetFromJsonAsync<ConversationPage>("/api/conversations", Json, Ct);
        var conversation = Assert.Single(list!.Conversations);
        Assert.Equal("the original question", conversation.Title);
        Assert.Equal(2, conversation.TurnCount);
        Assert.Equal("the original question", (await adam.GetFromJsonAsync<ConversationDetail>("/api/conversations/c_untitled", Json, Ct))!.Title);
    }

    [Fact]
    public async Task Rename_validates_and_delete_hides_blocks_chat_and_keeps_the_review_queue()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel());
        var adam = api.ClientFor("adam", "firm-a", Role.ADVISOR);
        var done = (await ApiFactory.ChatAsync(adam, "explain breakpoint pricing"))[^1].Data;
        var (conversationId, turnId) = (done.GetProperty("threadId").GetString()!, done.GetProperty("result").GetProperty("turnId").GetString()!);
        var url = $"/api/conversations/{conversationId}";

        Assert.Equal(HttpStatusCode.BadRequest, (await adam.PatchAsJsonAsync(url, new RenameConversationRequest("   "), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await adam.PatchAsJsonAsync(url, new RenameConversationRequest(new string('x', 121)), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.ClientFor("rita", "firm-a", Role.ADVISOR).PatchAsJsonAsync(url, new RenameConversationRequest("x"), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await adam.PatchAsJsonAsync(url, new RenameConversationRequest("  Breakpoints for Smith  "), Ct)).StatusCode);
        Assert.Equal("Breakpoints for Smith", (await adam.GetFromJsonAsync<ConversationDetail>(url, Json, Ct))!.Title);

        await adam.PostAsJsonAsync("/api/feedback", new FeedbackRequest(conversationId, turnId, FeedbackKind.WrongAnswer, null), Ct);
        Assert.Equal(HttpStatusCode.NotFound, (await api.ClientFor("rita", "firm-a", Role.ADVISOR).DeleteAsync(url, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await adam.DeleteAsync(url, Ct)).StatusCode);

        Assert.Empty((await adam.GetFromJsonAsync<ConversationPage>("/api/conversations", Json, Ct))!.Conversations);
        Assert.Equal(HttpStatusCode.NotFound, (await adam.GetAsync(url, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await adam.PostAsJsonAsync("/api/chat", new
        {
            threadId = conversationId,
            runId = "r_after_delete",
            messages = new[] { new { id = "u1", role = "user", content = "more?" } },
        }, Ct)).StatusCode);

        var queue = await api.ClientFor("alice", "firm-a", Role.FIRM_ADMIN).GetFromJsonAsync<List<ReviewQueueItem>>("/api/admin/feedback/queue", Json, Ct);
        Assert.Contains(queue!, q => q.TurnId == turnId);
    }

    [Fact]
    public async Task Continuing_uses_the_earlier_turns_and_moves_the_conversation_to_the_top()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel("First answer."));
        var adam = api.ClientFor("adam", "firm-a", Role.ADVISOR);
        var first = ApiFactory.ThreadOf(await ApiFactory.ChatAsync(adam, "explain breakpoint pricing"));
        await ApiFactory.ChatAsync(adam, "what is proration");

        // "Reload": a fresh client continues the first conversation by id.
        var events = await ApiFactory.ChatAsync(api.ClientFor("adam", "firm-a", Role.ADVISOR), "and for new accounts?", first);
        var history = ApiFactory.TracesOf(events).Select(t => t.Deserialize<TraceEvent>(Json)!).Single(t => t.Kind == TraceKinds.History);
        Assert.Contains("explain breakpoint pricing", history.Data.GetProperty("included").GetRawText());

        var list = await adam.GetFromJsonAsync<ConversationPage>("/api/conversations", Json, Ct);
        Assert.Equal(first, list!.Conversations[0].ConversationId);
        Assert.Equal(2, list.Conversations[0].TurnCount);
        // The title stays the first question, even when the conversation had no stored title before being continued.
        Assert.Equal("explain breakpoint pricing", list.Conversations[0].Title);
    }
}
