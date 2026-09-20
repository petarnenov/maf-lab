using System.Text.Json;
using System.Text.Json.Serialization;
using Maf.Lab.Domain.Billing;
using Maf.Lab.Domain.Tenancy;
using Microsoft.Extensions.Configuration;

namespace Maf.Lab.Retrieval.Billing;

/// <summary>Seed record as stored. Has a free-text Note that must never leave this class.</summary>
internal sealed record BillingAccountRecord(
    string FirmId,
    string AccountId,
    string Name,
    decimal Fee,
    string Currency,
    DateOnly NextPeriodStart,
    DateOnly NextPeriodEnd,
    string? Note);

/// <summary>Read-only accounts backed by seed JSON, always scoped to the caller's firm. The seeded fee is where an account starts, not where it is.</summary>
public sealed class BillingAccountStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly IReadOnlyList<BillingAccountRecord> _accounts;

    public BillingAccountStore(IConfiguration configuration)
        : this(File.ReadAllText(ResolvePath(configuration)))
    {
    }

    internal BillingAccountStore(string json) =>
        _accounts = JsonSerializer.Deserialize<List<BillingAccountRecord>>(json, Json) ?? [];

    /// <summary>The account as seeded, or null when it is not this firm's — which is the same answer as "there is no such account".</summary>
    public BillingAccount? Find(Principal principal, string accountId)
    {
        var id = Normalize(accountId);
        var account = _accounts.FirstOrDefault(a => a.FirmId == principal.FirmId.Value && Normalize(a.AccountId) == id);
        return account is null
            ? null
            : new BillingAccount(account.AccountId, account.Name, account.Fee, account.Currency, account.NextPeriodStart, account.NextPeriodEnd);
    }

    /// <summary>"account A-1042", "a-1042" and "A-1042" all mean the same account.</summary>
    private static string Normalize(string accountId) =>
        new(System.Text.RegularExpressions.Regex.Replace(accountId, @"^\s*account\b", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            .Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    /// <summary>Billing:AccountsSeedPath if set; otherwise compose/seed/billing-accounts.json of the repository containing the working directory.</summary>
    internal static string ResolvePath(IConfiguration configuration) =>
        SeedPaths.Resolve(configuration, "Billing:AccountsSeedPath", "billing-accounts.json");
}
