using System.Text.Json;
using System.Text.RegularExpressions;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Indexing.Corpus;
using Maf.Lab.Retrieval.Graph;

namespace Maf.Lab.Indexing.Graph;

/// <summary>
/// Builds the billing subgraph from the seed records and the billing corpus, deterministically and without a model.
/// Seed notes are free text (some carry injection and canary text on purpose) and are never read: the records below
/// have no field for them. Documents keep the corpus's tenant and <c>doc_id</c>, so a graph hit leads to the same
/// passages search_documents returns.
/// </summary>
public static partial class BillingGraphBuilder
{
    private sealed record AccountSeed(string FirmId, string AccountId, string? Name);
    private sealed record HouseholdSeed(string FirmId, string AccountId, string? HouseholdId);
    private sealed record RunSeed(string FirmId, string RunId, string? Status, string? PeriodStart, string? PeriodEnd);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// A fee schedule code: letters, one to three dash-separated parts, then a year and a three-digit serial
    /// (NW-INST-2026-083). Internal reference codes (NW-CANARY-7731-HH0005, ACME-CANARY-4410) do not end that way.
    /// </summary>
    [GeneratedRegex(@"(?<![A-Za-z0-9-])[A-Z]{2,6}(?:-[A-Z0-9]{2,6}){1,3}-20\d{2}-\d{3}(?![A-Za-z0-9-])")]
    internal static partial Regex FeeScheduleCode();

    public static GraphBuild Build(string accountsJson, string householdsJson, string runsJson, IReadOnlyList<SourceDocument> documents)
    {
        var nodes = new Dictionary<(string Label, string Tenant, string Key), GraphNode>();
        var edges = new HashSet<GraphEdge>();
        var rejected = new List<string>();

        void Node(string label, TenantId tenant, string key, Dictionary<string, object?> props) =>
            nodes.TryAdd((label, tenant.Value, key), new GraphNode(label, tenant, key, props));

        TenantId? Firm(string? firmId, string record)
        {
            if (TenantId.TryParse(firmId, out var tenant) && !tenant.IsShared)
            {
                Node(GraphLabels.Firm, tenant, tenant.Value, new() { ["name"] = tenant.Value });
                return tenant;
            }
            rejected.Add($"{record}: no valid firm id, so no tenant");
            return null;
        }

        var accounts = new List<(TenantId Tenant, string Id)>();
        foreach (var a in Deserialize<AccountSeed>(accountsJson))
        {
            if (Firm(a.FirmId, $"account {a.AccountId}") is not { } tenant || string.IsNullOrWhiteSpace(a.AccountId))
            {
                continue;
            }
            Node(GraphLabels.Account, tenant, a.AccountId, new() { ["name"] = a.Name });
            edges.Add(Edge(GraphLabels.Account, tenant, a.AccountId, GraphRelations.BelongsTo, GraphLabels.Firm, tenant, tenant.Value));
            accounts.Add((tenant, a.AccountId));
        }

        var households = new List<(TenantId Tenant, string Id)>();
        foreach (var h in Deserialize<HouseholdSeed>(householdsJson))
        {
            if (Firm(h.FirmId, $"household of {h.AccountId}") is not { } tenant || string.IsNullOrWhiteSpace(h.HouseholdId))
            {
                continue;
            }
            Node(GraphLabels.Household, tenant, h.HouseholdId, new() { ["name"] = null });
            edges.Add(Edge(GraphLabels.Household, tenant, h.HouseholdId, GraphRelations.BelongsTo, GraphLabels.Firm, tenant, tenant.Value));
            if (!households.Contains((tenant, h.HouseholdId)))
            {
                households.Add((tenant, h.HouseholdId));
            }
            if (accounts.Contains((tenant, h.AccountId)))
            {
                edges.Add(Edge(GraphLabels.Account, tenant, h.AccountId, GraphRelations.InHousehold, GraphLabels.Household, tenant, h.HouseholdId));
            }
        }

        foreach (var r in Deserialize<RunSeed>(runsJson))
        {
            if (Firm(r.FirmId, $"run {r.RunId}") is not { } tenant || string.IsNullOrWhiteSpace(r.RunId))
            {
                continue;
            }
            Node(GraphLabels.BillingRun, tenant, r.RunId, new()
            {
                ["status"] = r.Status, ["period_start"] = r.PeriodStart, ["period_end"] = r.PeriodEnd,
            });
            edges.Add(Edge(GraphLabels.BillingRun, tenant, r.RunId, GraphRelations.BelongsTo, GraphLabels.Firm, tenant, tenant.Value));
        }

        var known = accounts.Select(a => (Label: GraphLabels.Account, a.Tenant, a.Id))
            .Concat(households.Select(h => (Label: GraphLabels.Household, h.Tenant, h.Id)))
            .ToList();
        var knownPattern = known.Count == 0
            ? null
            : new Regex(@"(?<![A-Za-z0-9-])(?:" + string.Join("|", known.Select(k => Regex.Escape(k.Id)).Distinct()) + @")(?![A-Za-z0-9-])");

        foreach (var doc in documents)
        {
            Node(GraphLabels.Document, doc.Tenant, doc.DocId, new()
            {
                ["title"] = Title(doc), ["source_type"] = doc.SourceType, ["path"] = doc.SourcePath,
                [GraphProperties.DocHash] = doc.ContentHash,
            });
            if (knownPattern is not null)
            {
                foreach (var id in knownPattern.Matches(doc.Content).Select(m => m.Value).Distinct())
                {
                    // A document links to an entity of its own firm, or of any firm when the document is shared.
                    foreach (var entity in known.Where(k => k.Id == id && (doc.Tenant.IsShared || k.Tenant == doc.Tenant)))
                    {
                        edges.Add(Edge(GraphLabels.Document, doc.Tenant, doc.DocId, GraphRelations.Mentions, entity.Label, entity.Tenant, entity.Id));
                    }
                }
            }
            foreach (var code in FeeScheduleCode().Matches(doc.Content).Select(m => m.Value).Distinct())
            {
                Node(GraphLabels.FeeSchedule, doc.Tenant, code, new() { ["name"] = code });
                edges.Add(Edge(GraphLabels.Document, doc.Tenant, doc.DocId, GraphRelations.Mentions, GraphLabels.FeeSchedule, doc.Tenant, code));
            }
        }

        return new GraphBuild(GraphSources.Billing, [.. nodes.Values], [.. edges], rejected);
    }

    /// <summary>The first Markdown heading, else the file name without extension.</summary>
    internal static string Title(SourceDocument doc)
    {
        foreach (var line in doc.Content.AsSpan().EnumerateLines())
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("# "))
            {
                return trimmed[2..].Trim().ToString();
            }
            if (!trimmed.IsEmpty && !trimmed.StartsWith("#"))
            {
                break;
            }
        }
        return Path.GetFileNameWithoutExtension(doc.RelativePath);
    }

    private static GraphEdge Edge(string fromLabel, TenantId fromTenant, string fromKey, string type, string toLabel, TenantId toTenant, string toKey) =>
        new(fromLabel, fromTenant, fromKey, type, toLabel, toTenant, toKey);

    private static List<T> Deserialize<T>(string json) => JsonSerializer.Deserialize<List<T>>(json, Json) ?? [];
}
