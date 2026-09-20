using Maf.Lab.Domain.Billing;
using Maf.Lab.Domain.Tenancy;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace Maf.Lab.Retrieval.Billing;

/// <summary>
/// The only writable thing in the MCP server: adjustments that have actually been applied.
/// The seeded fee stays read-only, so an account's current fee is its seed plus this ledger.
///
/// Two replicas share one SQLite file, so the database — not a check in code — enforces "applied once":
/// a UNIQUE index on (firm, adjustment id) refuses the second insert, and that refusal is the answer
/// "already applied" rather than an error.
/// </summary>
public sealed class FeeAdjustmentLedger
{
    private readonly string _connectionString;

    public FeeAdjustmentLedger(IConfiguration configuration)
        : this(configuration["Billing:AdjustmentsConnectionString"] is { Length: > 0 } configured
            ? configured
            : "Data Source=maf-lab-adjustments.db")
    {
    }

    internal FeeAdjustmentLedger(string connectionString)
    {
        _connectionString = connectionString;
        Initialize();
    }

    /// <summary>Creates the schema if it is not there. Safe to run on every start and from every replica.</summary>
    internal void Initialize()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS "FeeAdjustments" (
                "AdjustmentId" TEXT NOT NULL,
                "FirmId" TEXT NOT NULL,
                "AccountId" TEXT NOT NULL,
                "Amount" TEXT NOT NULL,
                "PreviousFee" TEXT NOT NULL,
                "ResultingFee" TEXT NOT NULL,
                "Currency" TEXT NOT NULL,
                "AppliedAt" TEXT NOT NULL,
                "AppliedBy" TEXT NOT NULL,
                PRIMARY KEY ("FirmId", "AdjustmentId")
            );
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_FeeAdjustments_Firm_Adjustment"
                ON "FeeAdjustments" ("FirmId", "AdjustmentId");
            CREATE INDEX IF NOT EXISTS "IX_FeeAdjustments_Firm_Account"
                ON "FeeAdjustments" ("FirmId", "AccountId", "AppliedAt");
            """;
        command.ExecuteNonQuery();
    }

    /// <summary>What every adjustment applied to this account adds up to. The caller's firm, never a parameter's.</summary>
    public decimal AppliedTotal(Principal principal, string accountId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """SELECT "Amount" FROM "FeeAdjustments" WHERE "FirmId" = $firm AND "AccountId" = $account ORDER BY "AppliedAt", "rowid";""";
        command.Parameters.AddWithValue("$firm", principal.FirmId.Value);
        command.Parameters.AddWithValue("$account", accountId);
        using var reader = command.ExecuteReader();
        var total = 0m;
        while (reader.Read())
        {
            total += Money.Parse(reader.GetString(0));
        }
        return total;
    }

    /// <summary>
    /// Applies the adjustment, or reports the first application when it has already been applied.
    /// <paramref name="seededFee"/> is the account's read-only starting point.
    /// </summary>
    public FeeAdjustmentApplied Apply(
        Principal principal,
        string adjustmentId,
        string accountId,
        decimal amount,
        decimal seededFee,
        string currency,
        DateTimeOffset appliedAt)
    {
        using var connection = Open();
        // Immediate, not deferred: the fee is read and written in one go, and a second writer waits rather than racing.
        using var transaction = connection.BeginTransaction(System.Data.IsolationLevel.Serializable, deferred: false);

        if (Read(connection, transaction, principal.FirmId.Value, adjustmentId) is { } existing)
        {
            transaction.Commit();
            return existing with { AlreadyApplied = true };
        }

        var previousFee = seededFee + Total(connection, transaction, principal.FirmId.Value, accountId);
        var resultingFee = previousFee + amount;

        using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText =
                """
                INSERT INTO "FeeAdjustments"
                    ("AdjustmentId", "FirmId", "AccountId", "Amount", "PreviousFee", "ResultingFee", "Currency", "AppliedAt", "AppliedBy")
                VALUES ($id, $firm, $account, $amount, $previous, $resulting, $currency, $at, $by);
                """;
            insert.Parameters.AddWithValue("$id", adjustmentId);
            insert.Parameters.AddWithValue("$firm", principal.FirmId.Value);
            insert.Parameters.AddWithValue("$account", accountId);
            insert.Parameters.AddWithValue("$amount", Money.Format(amount));
            insert.Parameters.AddWithValue("$previous", Money.Format(previousFee));
            insert.Parameters.AddWithValue("$resulting", Money.Format(resultingFee));
            insert.Parameters.AddWithValue("$currency", currency);
            insert.Parameters.AddWithValue("$at", appliedAt.ToUniversalTime().ToString("O"));
            insert.Parameters.AddWithValue("$by", principal.UserId);

            try
            {
                insert.ExecuteNonQuery();
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
            {
                // Another replica got there first between the read above and this insert.
                transaction.Rollback();
                using var after = Open();
                if (Read(after, null, principal.FirmId.Value, adjustmentId) is { } winner)
                {
                    return winner with { AlreadyApplied = true };
                }
                throw;
            }
        }

        transaction.Commit();
        return new FeeAdjustmentApplied(adjustmentId, accountId, previousFee, amount, resultingFee, currency, appliedAt, AlreadyApplied: false);
    }

    private static FeeAdjustmentApplied? Read(SqliteConnection connection, SqliteTransaction? transaction, string firmId, string adjustmentId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT "AccountId", "Amount", "PreviousFee", "ResultingFee", "Currency", "AppliedAt"
            FROM "FeeAdjustments" WHERE "FirmId" = $firm AND "AdjustmentId" = $id;
            """;
        command.Parameters.AddWithValue("$firm", firmId);
        command.Parameters.AddWithValue("$id", adjustmentId);
        using var reader = command.ExecuteReader();
        return reader.Read()
            ? new FeeAdjustmentApplied(
                adjustmentId,
                reader.GetString(0),
                Money.Parse(reader.GetString(2)),
                Money.Parse(reader.GetString(1)),
                Money.Parse(reader.GetString(3)),
                reader.GetString(4),
                DateTimeOffset.Parse(reader.GetString(5), System.Globalization.CultureInfo.InvariantCulture),
                AlreadyApplied: false)
            : null;
    }

    private static decimal Total(SqliteConnection connection, SqliteTransaction transaction, string firmId, string accountId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """SELECT "Amount" FROM "FeeAdjustments" WHERE "FirmId" = $firm AND "AccountId" = $account;""";
        command.Parameters.AddWithValue("$firm", firmId);
        command.Parameters.AddWithValue("$account", accountId);
        using var reader = command.ExecuteReader();
        var total = 0m;
        while (reader.Read())
        {
            total += Money.Parse(reader.GetString(0));
        }
        return total;
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var pragma = connection.CreateCommand();
        // Two replicas share one file, exactly as the API's store does.
        pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000;";
        pragma.ExecuteNonQuery();
        return connection;
    }

    /// <summary>Money is stored as text so no rounding creeps in through a floating-point column.</summary>
    private static class Money
    {
        public static string Format(decimal value) => value.ToString("0.############################", System.Globalization.CultureInfo.InvariantCulture);

        public static decimal Parse(string value) => decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
    }
}
