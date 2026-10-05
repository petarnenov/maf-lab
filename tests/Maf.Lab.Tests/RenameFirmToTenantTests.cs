using System.Net.Http.Json;
using System.Text.Json;
using Maf.Lab.Api.Endpoints;
using Maf.Lab.Api.Storage;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Hosting.Stores;
using Maf.Lab.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Maf.Lab.Tests;

/// <summary>
/// rename-firm-to-tenant: a database, a run state and a token from before the rename keep their tenant, and the core's
/// principal, `/api/me` and the dev personas carry only core concepts.
/// </summary>
public class RenameFirmToTenantTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_database_from_before_the_rename_keeps_every_tenant_and_gets_its_indexes_back()
    {
        var factory = await OldDatabaseAsync();
        await using (var ctx = await factory.CreateDbContextAsync(Ct))
        {
            await DatabaseInitializer.InitializeAsync(ctx, Ct);
        }

        await using var check = await factory.CreateDbContextAsync(Ct);
        Assert.Equal("firm-a", (await check.Conversations.SingleAsync(Ct)).TenantId);
        Assert.Equal("firm-b", (await check.Turns.SingleAsync(Ct)).TenantId);
        Assert.Equal("firm-c", (await check.AdminJobs.SingleAsync(Ct)).TenantId);
        Assert.DoesNotContain(await TablesWithColumnAsync(check, "FirmId"), _ => true);
        Assert.DoesNotContain(await IndexSqlAsync(check), sql => sql.Contains("FirmId", StringComparison.Ordinal));

        var adminJobs = (await IndexSqlAsync(check)).Single(sql => sql.Contains("\"AdminJobs\"") && sql.Contains("TenantId"));
        Assert.StartsWith("CREATE UNIQUE INDEX", adminJobs);
        Assert.Contains("WHERE", adminJobs); // filtered: one running job of a kind per tenant
    }

    [Fact]
    public async Task A_second_start_changes_nothing()
    {
        var factory = await OldDatabaseAsync();
        await using (var ctx = await factory.CreateDbContextAsync(Ct))
        {
            await DatabaseInitializer.InitializeAsync(ctx, Ct);
        }
        var before = await SchemaAsync(factory);

        await using (var ctx = await factory.CreateDbContextAsync(Ct))
        {
            await DatabaseInitializer.InitializeAsync(ctx, Ct);
        }

        Assert.Equal(before, await SchemaAsync(factory));
    }

    [Fact]
    public async Task Initialisers_started_at_once_on_an_old_database_rename_it_once()
    {
        var factory = await OldDatabaseAsync();

        await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
        {
            await using var ctx = await factory.CreateDbContextAsync(Ct);
            await DatabaseInitializer.InitializeAsync(ctx, Ct);
        }));

        await using var check = await factory.CreateDbContextAsync(Ct);
        Assert.Equal("firm-a", (await check.Conversations.SingleAsync(Ct)).TenantId);
        Assert.Equal("firm-b", (await check.Turns.SingleAsync(Ct)).TenantId);
        Assert.Empty(await TablesWithColumnAsync(check, "FirmId"));
    }

    [Fact]
    public void A_run_state_stored_before_the_rename_reads_with_its_tenant()
    {
        const string legacy = """
            {"runId":"r1","conversationId":"c1","userId":"adam","firmId":"firm-a","answer":"","toolCalls":[],
             "outcome":"running","awaitingId":null,"turnId":null,"error":null,"updatedAt":"2026-10-01T00:00:00Z"}
            """;
        var state = RedisRunStateStore.Read(legacy);
        Assert.Equal("firm-a", state!.TenantId);

        var current = RedisRunStateStore.Read(legacy.Replace("\"firmId\"", "\"tenantId\""));
        Assert.Equal("firm-a", current!.TenantId);
    }

    [Fact]
    public async Task Me_carries_only_core_concepts()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel());
        var me = await api.ClientFor("adam", "firm-a", Role.USER).GetFromJsonAsync<JsonElement>("/api/me", Ct);

        Assert.Equal(["userId", "tenantId", "role"], me.EnumerateObject().Select(p => p.Name));
        Assert.Equal("firm-a", me.GetProperty("tenantId").GetString());
        Assert.Equal("USER", me.GetProperty("role").GetString());
    }

    [Fact]
    public void Dev_personas_keep_their_billing_roles_as_domain_claims()
    {
        var personas = DevIssuerEndpoints.Personas.ToDictionary(p => p.UserId);

        Assert.All(personas.Values, p => Assert.True(PrincipalClaims.TryParseRole(p.Role, out _), p.UserId));
        Assert.Equal(("TENANT_ADMIN", ""), Shape(personas["alice"]));
        Assert.Equal(("USER", "billing:advisor"), Shape(personas["adam"]));
        Assert.Equal(["adv-a-1", "adv-a-2"], personas["adam"].AdvisorIds);
        Assert.Equal(("USER", "billing:ops"), Shape(personas["olga"]));
        Assert.Equal(("READ_ONLY", ""), Shape(personas["rita"]));
        Assert.Equal(("USER", "billing:advisor"), Shape(personas["chris"]));

        static (string, string) Shape(DevIssuerEndpoints.DevUser p) => (p.Role, string.Join(",", p.DomainRoles));
    }

    /// <summary>
    /// A database as it was before the rename: today's schema and rows, with every TenantId column and every index on it
    /// named FirmId again.
    /// </summary>
    private static async Task<PooledDbContextFactory<MafDbContext>> OldDatabaseAsync()
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory("maf-rename-").FullName, "old.db");
        var factory = new PooledDbContextFactory<MafDbContext>(new DbContextOptionsBuilder<MafDbContext>()
            .UseSqlite($"Data Source={path}").AddInterceptors(new SqlitePragmaInterceptor()).Options);
        await using var ctx = await factory.CreateDbContextAsync(Ct);
        await ctx.Database.EnsureCreatedAsync(Ct);
        var at = new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc);
        ctx.Conversations.Add(new ConversationRow { Id = "c1", UserId = "adam", TenantId = "firm-a", CreatedAt = at, LastActivityAt = at });
        ctx.Turns.Add(new TurnRow { Id = "t1", ConversationId = "c1", UserId = "bianca", TenantId = "firm-b", Question = "q", CreatedAt = at });
        ctx.AdminJobs.Add(new AdminJobRow { Id = "j1", TenantId = "firm-c", Kind = "index", State = "done", OwnerInstance = "api-1", StartedAt = at, HeartbeatAt = at });
        await ctx.SaveChangesAsync(Ct);

        var indexes = await IndexSqlAsync(ctx);
        foreach (var table in await TablesWithColumnAsync(ctx, "TenantId"))
        {
            var onTable = indexes.Where(sql => sql.Contains($"\"{table}\"") && sql.Contains("TenantId")).ToList();
            foreach (var sql in onTable)
            {
                var drop = $"DROP INDEX \"{sql.Split('"')[1]}\""; // names from sqlite_master, not from input
                await ctx.Database.ExecuteSqlRawAsync(drop, Ct);
            }
            var rename = $"ALTER TABLE \"{table}\" RENAME COLUMN \"TenantId\" TO \"FirmId\"";
            await ctx.Database.ExecuteSqlRawAsync(rename, Ct);
            foreach (var sql in onTable)
            {
                await ctx.Database.ExecuteSqlRawAsync(sql.Replace("TenantId", "FirmId", StringComparison.Ordinal), Ct);
            }
        }
        Assert.Empty(await TablesWithColumnAsync(ctx, "TenantId"));
        return factory;
    }

    private static async Task<List<string>> TablesWithColumnAsync(MafDbContext ctx, string column) =>
        await ctx.Database.SqlQueryRaw<string>(
            "SELECT m.name AS Value FROM sqlite_master m JOIN pragma_table_info(m.name) c WHERE m.type = 'table' AND c.name = {0}", column)
            .ToListAsync(Ct);

    private static async Task<List<string>> IndexSqlAsync(MafDbContext ctx) =>
        await ctx.Database.SqlQueryRaw<string>("SELECT sql AS Value FROM sqlite_master WHERE type = 'index' AND sql IS NOT NULL").ToListAsync(Ct);

    private static async Task<string> SchemaAsync(PooledDbContextFactory<MafDbContext> factory)
    {
        await using var ctx = await factory.CreateDbContextAsync(Ct);
        var rows = await ctx.Database.SqlQueryRaw<string>("SELECT type || ' ' || name || ' ' || COALESCE(sql, '') AS Value FROM sqlite_master ORDER BY type, name").ToListAsync(Ct);
        return string.Join("\n", rows);
    }
}
