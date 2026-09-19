using System.Data.Common;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Maf.Lab.Api.Storage;

/// <summary>
/// Every connection runs in WAL mode with a busy timeout, so two api replicas sharing the SQLite file wait for each
/// other's writes instead of failing with SQLITE_BUSY.
/// </summary>
public sealed class SqlitePragmaInterceptor : DbConnectionInterceptor
{
    private const string Pragmas = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000;";

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var command = connection.CreateCommand();
        command.CommandText = Pragmas;
        command.ExecuteNonQuery();
    }

    public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = Pragmas;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

/// <summary>
/// Idempotent schema creation: runs the model's create script with IF NOT EXISTS on every table and index. Safe when
/// several replicas start at once, and adds tables introduced later (e.g. AdminJobs) to an existing database.
/// </summary>
public static partial class DatabaseInitializer
{
    public static async Task InitializeAsync(MafDbContext db, CancellationToken ct = default)
    {
        var script = db.Database.GenerateCreateScript();
        foreach (var statement in Statements(script).Where(s => s.StartsWith("CREATE TABLE", StringComparison.OrdinalIgnoreCase)))
        {
            await db.Database.ExecuteSqlRawAsync(statement, ct);
        }
        await AddMissingColumnsAsync(db, ct);
        // Indexes last: they may reference columns the additive pass just created.
        foreach (var statement in Statements(script).Where(s => !s.StartsWith("CREATE TABLE", StringComparison.OrdinalIgnoreCase)))
        {
            await db.Database.ExecuteSqlRawAsync(statement, ct);
        }
        await BackfillAsync(db, ct);
    }

    /// <summary>
    /// Adds columns that the model maps but an existing table lacks (SQLite ALTER TABLE ADD COLUMN). NOT NULL columns
    /// get a typed default. A concurrent replica adding the same column first is tolerated.
    /// </summary>
    internal static async Task AddMissingColumnsAsync(MafDbContext db, CancellationToken ct)
    {
        var connection = db.Database.GetDbConnection();
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            foreach (var entity in db.Model.GetEntityTypes())
            {
                var table = entity.GetTableName();
                if (table is null)
                {
                    continue;
                }
                var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                await using (var command = connection.CreateCommand())
                {
                    command.CommandText = $"PRAGMA table_info(\"{table}\")";
                    await using var reader = await command.ExecuteReaderAsync(ct);
                    while (await reader.ReadAsync(ct))
                    {
                        existing.Add(reader.GetString(1));
                    }
                }
                var store = Microsoft.EntityFrameworkCore.Metadata.StoreObjectIdentifier.Table(table, entity.GetSchema());
                foreach (var property in entity.GetProperties())
                {
                    var column = property.GetColumnName(store);
                    if (column is null || existing.Contains(column))
                    {
                        continue;
                    }
                    var type = property.GetColumnType();
                    var definition = property.IsNullable ? $"{type} NULL" : $"{type} NOT NULL DEFAULT {DefaultFor(type)}";
                    try
                    {
                        // Table, column and type come from the EF model, never from a request; DDL cannot take
                        // parameters anyway, so the command goes through the same connection as the PRAGMA above.
                        await using var alter = connection.CreateCommand();
                        alter.CommandText = $"ALTER TABLE \"{table}\" ADD COLUMN \"{column}\" {definition}";
                        await alter.ExecuteNonQueryAsync(ct);
                    }
                    catch (Microsoft.Data.Sqlite.SqliteException ex) when (ex.Message.Contains("duplicate column", StringComparison.OrdinalIgnoreCase))
                    {
                        // Another replica added it first.
                    }
                }
            }
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    private static string DefaultFor(string type) => type.ToUpperInvariant() switch
    {
        "INTEGER" => "0",
        "REAL" => "0",
        _ => "''",
    };

    /// <summary>Idempotent data backfills for columns added later.</summary>
    private static async Task BackfillAsync(MafDbContext db, CancellationToken ct)
    {
        await db.Database.ExecuteSqlRawAsync("""
            UPDATE "Conversations"
            SET "LastActivityAt" = COALESCE((SELECT MAX(t."CreatedAt") FROM "Turns" t WHERE t."ConversationId" = "Conversations"."Id"), "CreatedAt")
            WHERE "LastActivityAt" IS NULL OR "LastActivityAt" = '' OR "LastActivityAt" LIKE '0001-01-01%'
            """, ct);
    }

    internal static IEnumerable<string> Statements(string script) =>
        script.Split(";", StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => s.StartsWith("CREATE", StringComparison.OrdinalIgnoreCase))
            .Select(s => CreateTable().Replace(CreateIndex().Replace(s, "CREATE $1INDEX IF NOT EXISTS "), "CREATE TABLE IF NOT EXISTS "));

    [GeneratedRegex(@"^CREATE\s+TABLE\s+(?!IF NOT EXISTS)", RegexOptions.IgnoreCase)]
    private static partial Regex CreateTable();

    [GeneratedRegex(@"^CREATE\s+(UNIQUE\s+)?INDEX\s+(?!IF NOT EXISTS)", RegexOptions.IgnoreCase)]
    private static partial Regex CreateIndex();
}
