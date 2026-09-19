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
        foreach (var statement in Statements(db.Database.GenerateCreateScript()))
        {
            await db.Database.ExecuteSqlRawAsync(statement, ct);
        }
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
