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

    private static Task<List<string>> Names(MafDbContext ctx, string type, string name) =>
        ctx.Database.SqlQueryRaw<string>("""SELECT name AS "Value" FROM sqlite_master WHERE type = {0} AND name = {1}""", type, name)
            .ToListAsync(Ct);

    private static IDbContextFactory<MafDbContext> Factory(string path) => new PooledDbContextFactory<MafDbContext>(
        new DbContextOptionsBuilder<MafDbContext>().UseSqlite($"Data Source={path}").AddInterceptors(new SqlitePragmaInterceptor()).Options);
}
