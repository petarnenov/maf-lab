using Maf.Lab.Api.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Maf.Lab.Tests;

/// <summary>
/// A table the model renames keeps its rows (generalize-write-confirmation): renamed in place by the initializer, once,
/// however many replicas start together.
/// </summary>
public class TableRenameTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly IReadOnlyList<(string From, string To)> Renames = [("OldThings", "NewThings")];

    [Fact]
    public async Task A_renamed_table_keeps_its_rows_and_is_renamed_once_by_replicas_starting_together()
    {
        var factory = Factory(Path.Combine(Directory.CreateTempSubdirectory("maf-rename-").FullName, "maf.db"));
        await using (var ctx = await factory.CreateDbContextAsync(Ct))
        {
            await ctx.Database.ExecuteSqlRawAsync("""
                CREATE TABLE "OldThings" ("Id" TEXT NOT NULL PRIMARY KEY, "Name" TEXT NOT NULL);
                CREATE INDEX "IX_OldThings_Name" ON "OldThings" ("Name");
                INSERT INTO "OldThings" VALUES ('a', 'first'), ('b', 'second');
                """, Ct);
        }

        var renamed = await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
        {
            await using var ctx = await factory.CreateDbContextAsync(Ct);
            return await DatabaseInitializer.RenameLegacyTablesAsync(ctx, Renames, Ct);
        }));

        Assert.Equal(1, renamed.Sum());
        await using var check = await factory.CreateDbContextAsync(Ct);
        Assert.Equal(["a", "b"], await check.Database.SqlQueryRaw<string>("""SELECT "Id" AS "Value" FROM "NewThings" ORDER BY "Id" """).ToListAsync(Ct));
        Assert.Empty(await Names(check, "table", "OldThings"));
        // The old table's indexes are gone, for the index pass to create their successors under the new name.
        Assert.Empty(await Names(check, "index", "IX_OldThings_Name"));
    }

    [Fact]
    public async Task Renaming_again_changes_nothing()
    {
        var factory = Factory(Path.Combine(Directory.CreateTempSubdirectory("maf-rename-").FullName, "maf.db"));
        await using var ctx = await factory.CreateDbContextAsync(Ct);
        await ctx.Database.ExecuteSqlRawAsync("""CREATE TABLE "NewThings" ("Id" TEXT NOT NULL PRIMARY KEY); INSERT INTO "NewThings" VALUES ('a');""", Ct);

        Assert.Equal(0, await DatabaseInitializer.RenameLegacyTablesAsync(ctx, Renames, Ct));
        Assert.Equal(0, await DatabaseInitializer.RenameLegacyTablesAsync(ctx, [], Ct));
        Assert.Single(await ctx.Database.SqlQueryRaw<string>("""SELECT "Id" AS "Value" FROM "NewThings" """).ToListAsync(Ct));
    }

    [Fact]
    public async Task Pending_adjustments_become_pending_writes_with_their_rows_and_their_review()
    {
        // A database from before generalize-write-confirmation: the old table, its indexes and real rows.
        var factory = Factory(Path.Combine(Directory.CreateTempSubdirectory("maf-pending-").FullName, "maf.db"));
        await using (var ctx = await factory.CreateDbContextAsync(Ct))
        {
            await ctx.Database.ExecuteSqlRawAsync("""
                CREATE TABLE "PendingAdjustments" (
                    "Id" TEXT NOT NULL PRIMARY KEY, "TenantId" TEXT NOT NULL, "UserId" TEXT NOT NULL,
                    "ConversationId" TEXT NOT NULL, "TurnId" TEXT NOT NULL, "ToolName" TEXT NOT NULL, "State" TEXT NOT NULL,
                    "Summary" TEXT NOT NULL, "Question" TEXT NULL, "ExpiresAt" TEXT NULL, "Status" TEXT NOT NULL,
                    "ReviewTaskId" TEXT NULL, "Questions" INTEGER NOT NULL, "CreatedAt" TEXT NOT NULL, "UpdatedAt" TEXT NOT NULL);
                CREATE INDEX "IX_PendingAdjustments_ConversationId_UpdatedAt" ON "PendingAdjustments" ("ConversationId", "UpdatedAt");
                CREATE INDEX "IX_PendingAdjustments_TenantId_UserId_UpdatedAt" ON "PendingAdjustments" ("TenantId", "UserId", "UpdatedAt");
                """, Ct);
            var later = DateTime.UtcNow.AddHours(1).ToString("yyyy-MM-dd HH:mm:ss.fffffff");
            foreach (var (id, status, review, questions) in new[]
            {
                ("p_wait", "awaiting_confirmation", (string?)null, 0),
                ("p_why", "awaiting_justification", "t-review", 1),
                ("p_done", "applied", "t-done", 2),
            })
            {
                await ctx.Database.ExecuteSqlRawAsync("""
                    INSERT INTO "PendingAdjustments" VALUES ({0}, 'firm-a', 'adam', 'c-1', {1}, 'propose_fee_adjustment',
                        'opaque', {6}, 'Apply it?', {2}, {3}, {4}, {5}, {2}, {2})
                    """, [id, $"t-{id}", later, status, review!, questions, """{"amount":-200}"""], Ct);
            }
        }

        // Three replicas starting together on it.
        await Task.WhenAll(Enumerable.Range(0, 3).Select(async _ =>
        {
            await using var ctx = await factory.CreateDbContextAsync(Ct);
            await DatabaseInitializer.InitializeAsync(ctx, Ct);
        }));

        await using var db = await factory.CreateDbContextAsync(Ct);
        Assert.Empty(await Names(db, "table", "PendingAdjustments"));
        Assert.Empty(await Names(db, "index", "IX_PendingAdjustments_ConversationId_UpdatedAt"));
        Assert.Single(await Names(db, "index", "IX_PendingWrites_ConversationId_UpdatedAt"));
        var rows = await db.PendingWrites.AsNoTracking().OrderBy(p => p.Id).ToListAsync(Ct);
        Assert.Equal(
        [
            ("p_done", PendingWriteStatus.Applied, """{"reviewTaskId":"t-done","questions":2}"""),
            ("p_wait", PendingWriteStatus.AwaitingConfirmation, (string?)null),
            ("p_why", PendingWriteStatus.AwaitingInput, """{"reviewTaskId":"t-review","questions":1}"""),
        ], rows.Select(r => (r.Id, r.Status, r.FlowJson)));
        // The waiting one is still answerable: its state, summary, question and expiry came through.
        var waiting = rows.Single(r => r.Id == "p_wait");
        Assert.Equal(("opaque", """{"amount":-200}""", "Apply it?"), (waiting.State, waiting.Summary, waiting.Question));
        Assert.True(waiting.ExpiresAt > DateTime.UtcNow);
    }

    [Fact]
    public async Task The_pending_writes_backfill_is_a_no_op_without_the_table()
    {
        var factory = Factory(Path.Combine(Directory.CreateTempSubdirectory("maf-pending-").FullName, "maf.db"));
        await using var ctx = await factory.CreateDbContextAsync(Ct);

        await DatabaseInitializer.BackfillPendingWritesAsync(ctx, Ct);

        Assert.Empty(await Names(ctx, "table", "PendingWrites"));
    }

    private static Task<List<string>> Names(MafDbContext ctx, string type, string name) =>
        ctx.Database.SqlQueryRaw<string>("""SELECT name AS "Value" FROM sqlite_master WHERE type = {0} AND name = {1}""", type, name)
            .ToListAsync(Ct);

    private static IDbContextFactory<MafDbContext> Factory(string path) => new PooledDbContextFactory<MafDbContext>(
        new DbContextOptionsBuilder<MafDbContext>().UseSqlite($"Data Source={path}").AddInterceptors(new SqlitePragmaInterceptor()).Options);
}
