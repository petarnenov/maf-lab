using Maf.Lab.Domain.Graph;

namespace Maf.Lab.Retrieval.Graph;

/// <summary>One row of a graph result: column name → value (string, long, bool, list or null).</summary>
public interface IGraphRow
{
    object? this[string column] { get; }
}

/// <summary>
/// One of the closed set of graph reads: a name, constant Cypher, typed arguments and a node limit. There is no way to
/// build one from query text, and none carries a tenant; <see cref="TenantScopedGraph"/> binds that from the principal.
/// </summary>
public abstract record GraphQuery<TResult>
{
    /// <summary>The template's name, for spans and logs. Never an argument value.</summary>
    public abstract string Name { get; }

    /// <summary>The most rows the result may hold; one more is fetched to tell whether it was truncated.</summary>
    public abstract int Limit { get; }

    internal abstract string Cypher { get; }

    /// <summary>The template's own parameters. <c>readable</c> and <c>limit</c> are reserved for the read path.</summary>
    internal abstract IReadOnlyDictionary<string, object?> Arguments { get; }

    internal abstract TResult Map(IReadOnlyList<IGraphRow> rows, bool truncated);

    internal static string? Text(IGraphRow row, string column) => row[column] as string;

    internal static int Int(IGraphRow row, string column) => row[column] switch
    {
        long l => (int)l,
        int i => i,
        _ => 0,
    };

    internal static void RequireDepth(int depth, int max)
    {
        if (depth < 1 || depth > max)
        {
            throw new ArgumentOutOfRangeException(nameof(depth), depth, $"Depth must be between 1 and {max}.");
        }
    }
}

/// <summary>What one billing neighbourhood read returns before the tool shapes it.</summary>
public sealed record BillingNeighbourhoodRows(BillingEntity? Start, IReadOnlyList<RelatedBillingEntity> Related, IReadOnlyList<RelatedDocument> Documents, bool Truncated);

/// <summary>An account, household or fee schedule and what it reaches within depth 1 or 2.</summary>
public sealed record BillingNeighbourhood : GraphQuery<BillingNeighbourhoodRows>
{
    public const int MaxDepth = 2;
    public const int DefaultLimit = 50;

    public BillingNeighbourhood(string entityId, int depth, int limit = DefaultLimit)
    {
        RequireDepth(depth, MaxDepth);
        EntityId = entityId;
        Depth = depth;
        Limit = Math.Clamp(limit, 1, 200);
    }

    public string EntityId { get; }
    public int Depth { get; }
    public override string Name => $"billing_neighbourhood_{Depth}";
    public override int Limit { get; }
    internal override string Cypher => Depth == 1 ? GraphTemplates.BillingNeighbourhood1 : GraphTemplates.BillingNeighbourhood2;
    internal override IReadOnlyDictionary<string, object?> Arguments => new Dictionary<string, object?> { ["id"] = EntityId };

    internal override BillingNeighbourhoodRows Map(IReadOnlyList<IGraphRow> rows, bool truncated)
    {
        if (rows.Count == 0)
        {
            return new BillingNeighbourhoodRows(null, [], [], false);
        }
        var first = rows[0];
        var start = new BillingEntity(KindOf(Text(first, "start_kind")), Text(first, "start_key") ?? EntityId, Text(first, "start_name"));
        var related = new List<RelatedBillingEntity>();
        var documents = new List<RelatedDocument>();
        foreach (var row in rows)
        {
            var kind = Text(row, "kind");
            var key = Text(row, "key");
            if (kind is null || key is null)
            {
                continue; // the start alone: nothing reachable
            }
            var hops = Int(row, "hops");
            var via = Text(row, "via");
            if (kind == GraphLabels.Document)
            {
                documents.Add(new RelatedDocument(key, Text(row, "title") ?? key, Text(row, "source_type") ?? "", Text(row, "path") ?? "", hops, via));
            }
            else
            {
                related.Add(new RelatedBillingEntity(KindOf(kind), key, Text(row, "name"), hops, via, Text(row, "relation")));
            }
        }
        return new BillingNeighbourhoodRows(start, related, documents, truncated);
    }

    internal static string KindOf(string? label) => label switch
    {
        GraphLabels.Firm => BillingEntityKinds.Firm,
        GraphLabels.Household => BillingEntityKinds.Household,
        GraphLabels.Account => BillingEntityKinds.Account,
        GraphLabels.BillingRun => BillingEntityKinds.BillingRun,
        GraphLabels.FeeSchedule => BillingEntityKinds.FeeSchedule,
        GraphLabels.Document => BillingEntityKinds.Document,
        _ => "unknown",
    };
}

/// <summary>The latest billing runs of an account's firm.</summary>
public sealed record FirmRuns(string AccountId, int Count = 5) : GraphQuery<IReadOnlyList<RelatedBillingRun>>
{
    public override string Name => "firm_runs";
    public override int Limit => Math.Clamp(Count, 1, 20);
    internal override string Cypher => GraphTemplates.FirmRuns;
    internal override IReadOnlyDictionary<string, object?> Arguments => new Dictionary<string, object?> { ["id"] = AccountId };

    internal override IReadOnlyList<RelatedBillingRun> Map(IReadOnlyList<IGraphRow> rows, bool truncated) =>
        [.. rows.Select(r => new RelatedBillingRun(Text(r, "run_id") ?? "", Text(r, "status") ?? "", Text(r, "period_start") ?? "", Text(r, "period_end") ?? ""))];
}

/// <summary>A method that a symbol name can mean.</summary>
public sealed record SymbolCandidate(string Key, string Symbol, string TypeFullName, string Path, int StartLine, int EndLine);

/// <summary>The methods a symbol name can mean, grouped later by declaring type.</summary>
public sealed record SymbolCandidates(string SymbolName) : GraphQuery<IReadOnlyList<SymbolCandidate>>
{
    public override string Name => "symbol_candidates";
    public override int Limit => 100;
    internal override string Cypher => GraphTemplates.SymbolCandidates;
    internal override IReadOnlyDictionary<string, object?> Arguments => new Dictionary<string, object?> { ["name"] = SymbolName };

    internal override IReadOnlyList<SymbolCandidate> Map(IReadOnlyList<IGraphRow> rows, bool truncated) =>
        [.. rows.Select(r => new SymbolCandidate(Text(r, "key") ?? "", Text(r, "symbol") ?? "", Text(r, "type_full_name") ?? "",
            Text(r, "path") ?? "", Int(r, "start_line"), Int(r, "end_line")))];
}

public enum TraceDirection
{
    Callers,
    Callees,
}

public sealed record CallTraceRows(IReadOnlyList<CodeTraceHit> Hits, bool Truncated);

/// <summary>Methods that call (or are called by) a set of methods, through up to <see cref="MaxDepth"/> calls.</summary>
public sealed record CallTrace : GraphQuery<CallTraceRows>
{
    public const int MaxDepth = 4;
    public const int DefaultLimit = 100;

    public CallTrace(IReadOnlyList<string> methodKeys, TraceDirection direction, int depth, int limit = DefaultLimit)
    {
        RequireDepth(depth, MaxDepth);
        MethodKeys = methodKeys;
        Direction = direction;
        Depth = depth;
        Limit = Math.Clamp(limit, 1, 300);
    }

    public IReadOnlyList<string> MethodKeys { get; }
    public TraceDirection Direction { get; }
    public int Depth { get; }
    public override string Name => $"{(Direction == TraceDirection.Callers ? "callers" : "callees")}_{Depth}";
    public override int Limit { get; }
    internal override string Cypher => (Direction == TraceDirection.Callers ? GraphTemplates.Callers : GraphTemplates.Callees)[Depth - 1];
    internal override IReadOnlyDictionary<string, object?> Arguments => new Dictionary<string, object?> { ["keys"] = MethodKeys.ToList() };

    internal override CallTraceRows Map(IReadOnlyList<IGraphRow> rows, bool truncated) =>
        new([.. rows.Select(r => new CodeTraceHit(Text(r, "symbol") ?? "", Text(r, "path") ?? "", Int(r, "start_line"), Int(r, "end_line"),
            Int(r, "hops"), r["is_test"] is true))], truncated);
}

public sealed record FileMethodsRows(bool FileFound, IReadOnlyList<SymbolCandidate> Methods, bool Truncated);

/// <summary>Whether a file is in the code graph, and the methods it declares.</summary>
public sealed record FileMethods(string RepositoryPath) : GraphQuery<FileMethodsRows>
{
    public override string Name => "file_methods";
    public override int Limit => 300;
    internal override string Cypher => GraphTemplates.FileMethods;
    internal override IReadOnlyDictionary<string, object?> Arguments => new Dictionary<string, object?> { ["path"] = RepositoryPath };

    internal override FileMethodsRows Map(IReadOnlyList<IGraphRow> rows, bool truncated) =>
        new(rows.Count > 0,
            [.. rows.Where(r => Text(r, "key") is not null).Select(r => new SymbolCandidate(Text(r, "key")!, Text(r, "symbol") ?? "", "",
                Text(r, "path") ?? "", Int(r, "start_line"), Int(r, "end_line")))],
            truncated);
}
