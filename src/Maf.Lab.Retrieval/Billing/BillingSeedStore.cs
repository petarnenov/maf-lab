using System.Text.Json.Serialization;
using System.Text.Json;
using Maf.Lab.Domain.Billing;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Retrieval.Configuration;
using Microsoft.Extensions.Configuration;

namespace Maf.Lab.Retrieval.Billing;

// names a domain until the extract-evals-plugin follow-up moves it (introduce-plugins 8.1)
// The billing host, which stays here until the eval stops hosting it in-process.
/// <summary>Seed record as stored. Has a free-text Note that must never leave this class.</summary>
internal sealed record BillingRunRecord(
    string FirmId,
    string RunId,
    string Status,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    int AccountCount,
    string? FailureReason,
    DateTimeOffset UpdatedAt,
    string? Note);

/// <summary>Read-only billing runs backed by seed JSON, always scoped to the caller's firm.</summary>
public sealed class BillingSeedStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly IReadOnlyList<BillingRunRecord> _runs;

    public BillingSeedStore(IConfiguration configuration)
        : this(SeedPaths.Read(configuration, "Billing:SeedPath", "billing-runs.json"))
    {
    }

    internal BillingSeedStore(string json) =>
        _runs = JsonSerializer.Deserialize<List<BillingRunRecord>>(json, Json) ?? [];

    public BillingRunStatus? GetStatus(Principal principal, string runId)
    {
        var id = NormalizeRunId(runId);
        var run = _runs.FirstOrDefault(r => r.FirmId == principal.TenantId.Value && r.RunId == id);
        return run is null
            ? null
            : new BillingRunStatus(run.RunId, run.Status, run.PeriodStart, run.PeriodEnd, run.AccountCount, run.FailureReason, run.UpdatedAt);
    }

    public SearchBillingRunsResult Search(Principal principal, string? status, DateOnly? periodFrom, DateOnly? periodTo, int limit)
    {
        var matches = _runs
            .Where(r => r.FirmId == principal.TenantId.Value)
            .Where(r => status is null || string.Equals(r.Status, status, StringComparison.OrdinalIgnoreCase))
            .Where(r => periodFrom is null || r.PeriodEnd >= periodFrom)
            .Where(r => periodTo is null || r.PeriodStart <= periodTo)
            .OrderByDescending(r => r.PeriodStart)
            .ToList();
        var page = matches.Take(limit).Select(r => new BillingRunSummary(r.RunId, r.Status, r.PeriodStart, r.PeriodEnd, r.AccountCount)).ToList();
        return new SearchBillingRunsResult(page, matches.Count, matches.Count > page.Count);
    }

    /// <summary>"run #4417", "#4417" and "4417" all mean run 4417.</summary>
    public static string NormalizeRunId(string runId) =>
        new(System.Text.RegularExpressions.Regex.Replace(runId, @"^\s*run\b", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            .Where(char.IsLetterOrDigit).ToArray());

    /// <summary>
    /// Billing:SeedPath: set by billing.env in compose, by the billing plugin's plugin.mk under make, and by the
    /// fixture in tests; the repository fallback of <see cref="SeedPaths"/> no longer holds billing's seeds.
    /// </summary>
    internal static string ResolvePath(IConfiguration configuration) =>
        SeedPaths.Resolve(configuration, "Billing:SeedPath", "billing-runs.json");
}
