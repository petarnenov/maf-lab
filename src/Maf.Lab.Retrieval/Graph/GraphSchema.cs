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

    // Retrieval subgraph (neo4j-retrieval-spike): chunks copied from Qdrant and their BM25 terms, for measurement only.
    public const string RetrievalChunk = "RetrievalChunk";
    public const string Term = "Term";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Firm, Household, Account, BillingRun, FeeSchedule, Document, Project, File, Type, Method, RetrievalChunk, Term,
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
    /// <summary>A BM25 term occurs in a retrieval chunk, with the term's weight in that chunk as <c>w</c>. Not <c>IN</c>: a Cypher keyword.</summary>
    public const string OccursIn = "OCCURS_IN";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        BelongsTo, InHousehold, Mentions, References, Contains, Declares, Calls, OccursIn,
    };
}

/// <summary>Node properties read back by maintenance (add-graph-drift).</summary>
public static class GraphProperties
{
    /// <summary>
    /// A document node's source content hash: the same value its chunks carry as <c>content_hash</c> in Qdrant. The node's
    /// own <c>content_hash</c> is a hash of its properties, used to skip unchanged writes.
    /// </summary>
    public const string DocHash = "doc_hash";
}

/// <summary>The subgraphs; every node and edge records which build wrote it, so stale ones are removed per source.</summary>
public static class GraphSources
{
    public const string Billing = "billing";
    public const string Code = "code";

    /// <summary>What <c>make graph</c> builds from the sources.</summary>
    public static readonly IReadOnlyList<string> All = [Billing, Code];

    /// <summary>
    /// Copies of a Qdrant collection's chunks (neo4j-retrieval-spike), one source per collection so copying one never
    /// removes the other's. Written by <c>make neo4j-chunks</c>, never by <c>make graph</c>.
    /// </summary>
    public const string RetrievalBilling = "retrieval-billing";
    public const string RetrievalPortfolio = "retrieval-portfolio";

    public static readonly IReadOnlyList<string> Retrieval = [RetrievalBilling, RetrievalPortfolio];

    /// <summary>Every source a write may name.</summary>
    public static readonly IReadOnlySet<string> Writable = new HashSet<string>([.. All, .. Retrieval], StringComparer.Ordinal);
}

/// <summary>The retrieval subgraph's fixed shape (neo4j-retrieval-spike).</summary>
public static class RetrievalGraph
{
    /// <summary>The vector index over <c>RetrievalChunk.dense</c>; its filter properties are the ones the templates filter on.</summary>
    public const string DenseIndex = "retrieval_chunk_dense";
    public const string DenseProperty = "dense";
    public const string CollectionProperty = "collection";
    /// <summary>embeddinggemma's dimension (<c>dense_v3</c>); the copy refuses a vector of another length.</summary>
    public const int DenseDimensions = 768;
    public const string WeightProperty = "w";

    /// <summary>The source a collection's copy is written under.</summary>
    public static string SourceOf(string collection) => collection switch
    {
        "maf_chunks" => GraphSources.RetrievalBilling,
        "maf_portfolio_chunks" => GraphSources.RetrievalPortfolio,
        _ => throw new ArgumentException($"'{collection}' is not a collection the spike copies (maf_chunks, maf_portfolio_chunks).", nameof(collection)),
    };

    public static string ChunkKey(string collection, string chunkId) => $"{collection}:{chunkId}";

    public static string TermKey(string collection, uint index) => $"{collection}:{index}";
}
