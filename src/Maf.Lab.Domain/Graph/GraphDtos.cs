namespace Maf.Lab.Domain.Graph;

/// <summary>Wire names of the graph tools.</summary>
public static class GraphTools
{
    public const string TraceBilling = "trace_billing_relationships";
    public const string TraceCodeSymbol = "trace_code_symbol";
    public const string ChangeImpact = "change_impact";
}

/// <summary>Kinds of billing entities, as the tools name them.</summary>
public static class BillingEntityKinds
{
    public const string Firm = "firm";
    public const string Household = "household";
    public const string Account = "account";
    public const string BillingRun = "billing_run";
    public const string FeeSchedule = "fee_schedule";
    public const string Document = "document";
}

/// <summary>The entity a trace started from.</summary>
/// <param name="Kind">account, household or fee_schedule.</param>
/// <param name="Id">Its id, e.g. "A-1042", "HH-RIDGELINE" or "NW-INST-2026-083".</param>
/// <param name="Name">Its display name, when it has one.</param>
public sealed record BillingEntity(string Kind, string Id, string? Name);

/// <summary>A billing entity related to the start, other than a document.</summary>
/// <param name="Hops">1 when directly linked to the start, 2 when linked through one other entity.</param>
/// <param name="Via">At 2 hops, the id of the entity in between.</param>
/// <param name="Relation">The link that reaches it, e.g. IN_HOUSEHOLD, BELONGS_TO, MENTIONS.</param>
public sealed record RelatedBillingEntity(string Kind, string Id, string? Name, int Hops, string? Via, string? Relation);

/// <summary>A document linked to the start, to read with search_documents.</summary>
/// <param name="DocumentId">The same id the document's passages carry in search_documents.</param>
public sealed record RelatedDocument(string DocumentId, string Title, string SourceType, string Path, int Hops, string? Via);

/// <summary>A recent billing run of the account's firm. Fees and notes are never included.</summary>
public sealed record RelatedBillingRun(string RunId, string Status, string PeriodStart, string PeriodEnd);

/// <summary>The bounded neighbourhood of an account, a household or a fee schedule within the caller's firm.</summary>
/// <param name="Truncated">True when more entities were reachable than the limit allowed.</param>
public sealed record BillingRelationships(
    BillingEntity Entity,
    int Depth,
    IReadOnlyList<RelatedBillingEntity> Related,
    IReadOnlyList<RelatedDocument> Documents,
    IReadOnlyList<RelatedBillingRun> RecentRuns,
    bool Truncated);

/// <summary>A method or constructor of the repository, located like a codebase search hit.</summary>
/// <param name="Symbol">Type and member, e.g. "TenantScopedSearch.QueryAsync".</param>
public sealed record CodeSymbol(string Symbol, string Path, int StartLine, int EndLine);

/// <summary>A method reached from the traced symbol.</summary>
/// <param name="Hops">1 for a direct call, more through intermediate methods.</param>
/// <param name="IsTest">True for a test method.</param>
public sealed record CodeTraceHit(string Symbol, string Path, int StartLine, int EndLine, int Hops, bool IsTest);

/// <summary>Who calls a symbol (callers) or what it calls (callees).</summary>
/// <param name="Matched">The symbol's methods (overloads included) that were traced.</param>
/// <param name="Candidates">When the name was ambiguous: one entry per matching type, and nothing was traced.</param>
/// <param name="Note">Why nothing was traced, when nothing was.</param>
public sealed record CodeTrace(
    string Symbol,
    string Direction,
    int Depth,
    IReadOnlyList<CodeSymbol> Matched,
    IReadOnlyList<CodeSymbol> Candidates,
    IReadOnlyList<CodeTraceHit> Reached,
    bool Truncated,
    string? Note);

/// <summary>The tests of one test file that reach the changed file's code.</summary>
public sealed record TestFileImpact(string Path, IReadOnlyList<CodeTraceHit> Tests);

/// <summary>What a change to one file can affect: its methods, the methods that reach them, and the tests among those.</summary>
public sealed record ChangeImpact(
    string Path,
    IReadOnlyList<CodeSymbol> Declared,
    IReadOnlyList<CodeTraceHit> ReachedFrom,
    IReadOnlyList<TestFileImpact> Tests,
    bool Truncated);
