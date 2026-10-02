namespace Maf.Lab.Retrieval.Graph;

/// <summary>
/// The labels and relationship types the graph holds. Cypher cannot take a label as a parameter, so writes pick theirs
/// from these constants and nothing else.
/// </summary>
public static class GraphLabels
{
    // Billing subgraph
    public const string Firm = "Firm";
    public const string Household = "Household";
    public const string Account = "Account";
    public const string BillingRun = "BillingRun";
    public const string FeeSchedule = "FeeSchedule";
    public const string Document = "Document";

    // Code subgraph
    public const string Project = "Project";
    public const string File = "File";
    public const string Type = "Type";
    public const string Method = "Method";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Firm, Household, Account, BillingRun, FeeSchedule, Document, Project, File, Type, Method,
    };
}

public static class GraphRelations
{
    public const string BelongsTo = "BELONGS_TO";
    public const string InHousehold = "IN_HOUSEHOLD";
    public const string Mentions = "MENTIONS";
    public const string References = "REFERENCES";
    public const string Contains = "CONTAINS";
    public const string Declares = "DECLARES";
    public const string Calls = "CALLS";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        BelongsTo, InHousehold, Mentions, References, Contains, Declares, Calls,
    };
}

/// <summary>The two subgraphs; every node and edge records which build wrote it, so stale ones are removed per source.</summary>
public static class GraphSources
{
    public const string Billing = "billing";
    public const string Code = "code";

    public static readonly IReadOnlyList<string> All = [Billing, Code];
}
