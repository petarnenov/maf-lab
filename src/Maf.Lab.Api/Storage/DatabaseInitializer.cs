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
        // First: a column the model renamed must be renamed in place before the additive pass, or that pass would add an
        // empty column beside it (rename-firm-to-tenant).
        await RenameLegacyColumnsAsync(db, ct);
        await DropRetiredTablesAsync(db, ct);
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
    /// Tables the model no longer has and nothing else owns, dropped so their content does not outlive every retention:
    /// <c>TurnTraces</c>, the full traces kept before the monitor became a plugin (introduce-plugins 5.3), which are at or
    /// past its seven days anyway — the monitor keeps its own, in its own table. Idempotent and safe across replicas.
    /// </summary>
    internal static readonly IReadOnlyList<string> RetiredTables = ["TurnTraces"];

    internal static async Task DropRetiredTablesAsync(MafDbContext db, CancellationToken ct)
    {
        foreach (var table in RetiredTables)
        {
            // The name comes from the list above, never from a request.
#pragma warning disable EF1002
            await db.Database.ExecuteSqlRawAsync($"DROP TABLE IF EXISTS \"{table}\"", ct);
#pragma warning restore EF1002
        }
    }

    /// <summary>Columns renamed by the model, old name → new name. A table is touched only when it has the old one.</summary>
    internal static readonly IReadOnlyList<(string From, string To)> RenamedColumns = [("FirmId", "TenantId")];

    /// <summary>
    /// Renames, in every existing table the model maps, each column of <see cref="RenamedColumns"/> that still has its
    /// old name and lacks its new one, and drops the indexes named after the old column, so the index pass creates
    /// their successors. Each table's check and rename run in one <c>BEGIN IMMEDIATE</c> transaction, so a second replica
    /// starting at the same moment waits, then sees the new name and skips it; a lost race is tolerated as the additive
    /// pass tolerates one. Idempotent: a renamed table is left alone.
    /// </summary>
    internal static async Task RenameLegacyColumnsAsync(MafDbContext db, CancellationToken ct)
    {
        var connection = (Microsoft.Data.Sqlite.SqliteConnection)db.Database.GetDbConnection();
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            foreach (var table in db.Model.GetEntityTypes().Select(e => e.GetTableName()).OfType<string>().Distinct())
            {
                foreach (var (from, to) in RenamedColumns)
                {
                    await RenameAsync(connection, table, from, to, ct);
                }
            }
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    private static async Task RenameAsync(Microsoft.Data.Sqlite.SqliteConnection connection, string table, string from, string to, CancellationToken ct)
    {
        // Not deferred = BEGIN IMMEDIATE: the write lock is taken before the schema is read, so check and rename are one step.
        await using var transaction = connection.BeginTransaction(deferred: false);
        try
        {
            var columns = await ColumnsAsync(connection, transaction, table, ct);
            if (!columns.Contains(from) || columns.Contains(to))
            {
                await transaction.CommitAsync(ct);
                return;
            }
            // Table and column names come from the EF model and RenamedColumns, never from a request.
            var indexes = new List<string>();
            await using (var list = connection.CreateCommand())
            {
                list.Transaction = transaction;
                list.CommandText = "SELECT name FROM sqlite_master WHERE type = 'index' AND tbl_name = $table AND name LIKE $pattern ESCAPE '\\'";
                list.Parameters.AddWithValue("$table", table);
                list.Parameters.AddWithValue("$pattern", $"%\\_{from}%");
                await using var reader = await list.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    indexes.Add(reader.GetString(0));
                }
            }
            foreach (var index in indexes)
            {
                await ExecuteAsync(connection, transaction, $"DROP INDEX IF EXISTS \"{index}\"", ct);
            }
            await ExecuteAsync(connection, transaction, $"ALTER TABLE \"{table}\" RENAME COLUMN \"{from}\" TO \"{to}\"", ct);
            await transaction.CommitAsync(ct);
        }
        catch (Microsoft.Data.Sqlite.SqliteException ex) when (ex.Message.Contains("no such column", StringComparison.OrdinalIgnoreCase)
                                                              || ex.Message.Contains("duplicate column", StringComparison.OrdinalIgnoreCase))
        {
            // Another replica renamed it first.
            await transaction.RollbackAsync(CancellationToken.None);
        }
    }

    private static async Task<HashSet<string>> ColumnsAsync(Microsoft.Data.Sqlite.SqliteConnection connection,
        Microsoft.Data.Sqlite.SqliteTransaction transaction, string table, CancellationToken ct)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"PRAGMA table_info(\"{table}\")";
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            columns.Add(reader.GetString(1));
        }
        return columns;
    }

    private static async Task ExecuteAsync(Microsoft.Data.Sqlite.SqliteConnection connection, Microsoft.Data.Sqlite.SqliteTransaction transaction,
        string sql, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(ct);
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
        // A run whose budget stopped an attempt used to be stored one attempt short (keep-attempt-on-budget-stop).
        await db.Database.ExecuteSqlRawAsync("""
            UPDATE "TestGenRuns"
            SET "Attempt" = (SELECT MAX(a."Attempt") FROM "TestGenRunActivity" a WHERE a."RunId" = "TestGenRuns"."Id")
            WHERE "Attempt" < (SELECT MAX(a."Attempt") FROM "TestGenRunActivity" a WHERE a."RunId" = "TestGenRuns"."Id")
            """, ct);
        // A run stored before its end was recorded (show-test-run-duration): its first update in a state that is not
        // running, else its last change. Running runs have no end; a row that has one is left alone.
        await db.Database.ExecuteSqlRawAsync("""
            UPDATE "TestGenRuns"
            SET "FinishedAt" = COALESCE(
                (SELECT MIN(e."At") FROM "TestGenRunEvents" e
                 WHERE e."RunId" = "TestGenRuns"."Id"
                   AND json_extract(e."Json", '$.state') NOT IN ('submitted', 'working', 'verifying')),
                "UpdatedAt")
            WHERE "FinishedAt" IS NULL AND "State" NOT IN ('submitted', 'working', 'verifying')
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
