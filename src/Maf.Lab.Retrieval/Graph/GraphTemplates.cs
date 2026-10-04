namespace Maf.Lab.Retrieval.Graph;

/// <summary>
/// Every Cypher read the system runs, as constant text. <c>$readable</c> (the principal's firm and shared) and
/// <c>$limit</c> are bound by <see cref="TenantScopedGraph"/> alone. Every node a template matches carries
/// <c>tenant_id IN $readable</c>, and so does every node of a variable-length path; the template guard test proves it.
/// A variable-length pattern cannot take its depth as a parameter, so each allowed depth has its own constant text.
/// </summary>
public static class GraphTemplates
{
    /// <summary>An account, household or fee schedule and what it reaches, never through a firm or a run (hubs).</summary>
    private const string BillingNeighbourhoodShape = """
        MATCH (s)
        WHERE (s:Account OR s:Household OR s:FeeSchedule) AND s.key = $id AND s.tenant_id IN $readable
        OPTIONAL MATCH p = (s)-[*1..{depth}]-(n)
        WHERE n.tenant_id IN $readable
          AND all(x IN nodes(p) WHERE x.tenant_id IN $readable)
          AND none(x IN nodes(p)[1..-1] WHERE x:Firm OR x:BillingRun)
          AND n <> s
        WITH s, n, p ORDER BY length(p)
        WITH s, n, head(collect(p)) AS sp
        RETURN labels(s)[0] AS start_kind, s.key AS start_key, s.name AS start_name,
               labels(n)[0] AS kind, n.key AS key, n.name AS name, n.title AS title,
               n.source_type AS source_type, n.path AS path,
               length(sp) AS hops,
               CASE WHEN length(sp) > 1 THEN nodes(sp)[1].key END AS via,
               type(last(relationships(sp))) AS relation
        ORDER BY hops, kind, key
        LIMIT $limit
        """;

    public static readonly string BillingNeighbourhood1 = BillingNeighbourhoodShape.Replace("{depth}", "1");
    public static readonly string BillingNeighbourhood2 = BillingNeighbourhoodShape.Replace("{depth}", "2");

    /// <summary>The latest runs of the account's firm.</summary>
    public const string FirmRuns = """
        MATCH (s:Account)-[:BELONGS_TO]->(f:Firm)<-[:BELONGS_TO]-(r:BillingRun)
        WHERE s.key = $id AND s.tenant_id IN $readable AND f.tenant_id IN $readable AND r.tenant_id IN $readable
        RETURN r.key AS run_id, r.status AS status, r.period_start AS period_start, r.period_end AS period_end
        ORDER BY r.period_start DESC, r.key DESC
        LIMIT $limit
        """;

    /// <summary>Methods a symbol name can mean: "Type.Member", a full name, a type name or a bare member name.</summary>
    public const string SymbolCandidates = """
        MATCH (m:Method)
        WHERE m.tenant_id IN $readable
          AND (m.display = $name OR m.full_name = $name OR m.type_name = $name OR m.type_full_name = $name OR m.name = $name)
        RETURN m.key AS key, m.display AS symbol, m.type_full_name AS type_full_name, m.path AS path,
               m.start_line AS start_line, m.end_line AS end_line
        ORDER BY type_full_name, path, start_line
        LIMIT $limit
        """;

    private const string CallersShape = """
        MATCH (m:Method)
        WHERE m.key IN $keys AND m.tenant_id IN $readable
        MATCH p = (m)<-[:CALLS*1..{depth}]-(c:Method)
        WHERE c.tenant_id IN $readable AND all(x IN nodes(p) WHERE x.tenant_id IN $readable) AND NOT c.key IN $keys
        WITH c, min(length(p)) AS hops
        RETURN c.key AS key, c.display AS symbol, c.path AS path, c.start_line AS start_line, c.end_line AS end_line,
               c.is_test AS is_test, hops
        ORDER BY hops, symbol, path
        LIMIT $limit
        """;

    private const string CalleesShape = """
        MATCH (m:Method)
        WHERE m.key IN $keys AND m.tenant_id IN $readable
        MATCH p = (m)-[:CALLS*1..{depth}]->(c:Method)
        WHERE c.tenant_id IN $readable AND all(x IN nodes(p) WHERE x.tenant_id IN $readable) AND NOT c.key IN $keys
        WITH c, min(length(p)) AS hops
        RETURN c.key AS key, c.display AS symbol, c.path AS path, c.start_line AS start_line, c.end_line AS end_line,
               c.is_test AS is_test, hops
        ORDER BY hops, symbol, path
        LIMIT $limit
        """;

    /// <summary>Callers and callees at depth 1 to <see cref="CallTrace.MaxDepth"/>, index = depth - 1.</summary>
    public static readonly IReadOnlyList<string> Callers = [.. Enumerable.Range(1, CallTrace.MaxDepth).Select(d => CallersShape.Replace("{depth}", d.ToString()))];
    public static readonly IReadOnlyList<string> Callees = [.. Enumerable.Range(1, CallTrace.MaxDepth).Select(d => CalleesShape.Replace("{depth}", d.ToString()))];

    /// <summary>A file of the code graph and the methods it declares.</summary>
    public const string FileMethods = """
        MATCH (f:File)
        WHERE f.key = $path AND f.tenant_id IN $readable
        OPTIONAL MATCH (f)-[:DECLARES]->(m:Method)
        WHERE m.tenant_id IN $readable
        RETURN f.key AS file, m.key AS key, m.display AS symbol, m.path AS path, m.start_line AS start_line, m.end_line AS end_line
        ORDER BY start_line
        LIMIT $limit
        """;

    // ---- Retrieval spike (neo4j-retrieval-spike): eval-only chunk search over chunks copied from Qdrant ----------------------

    /// <summary>The chunk fields a search returns: everything but the vector, so a result is a chunk as Qdrant returns it.</summary>
    private const string ChunkProjection =
        "c { .tenant_id, .doc_id, .chunk_id, .source_type, .source_path, .section_path, .symbol, .updated_at, .model_version, " +
        ".text, .context, .content_hash, .start_line, .end_line } AS chunk";

    /// <summary>
    /// Nearest chunks by the dense vector, with the tenant applied inside the vector index (Cypher 25 SEARCH … WHERE) so a
    /// small tenant is not shortchanged by a large one. The score is Neo4j's normalised cosine, (1 + cos) / 2.
    /// </summary>
    private const string RetrievalDenseShape = """
        CYPHER 25
        MATCH (c:RetrievalChunk)
          SEARCH c IN (VECTOR INDEX retrieval_chunk_dense FOR $vector
            WHERE c.collection = $collection AND c.tenant_id IN $readable{types}
            LIMIT $limit) SCORE AS score
        RETURN {projection}, score
        ORDER BY score DESC, c.key
        """;

    public static readonly string RetrievalDense = RetrievalDenseShape.Replace("{types}", "").Replace("{projection}", ChunkProjection);
    public static readonly string RetrievalDenseTyped = RetrievalDenseShape.Replace("{types}", " AND c.source_type IN $types").Replace("{projection}", ChunkProjection);

    /// <summary>
    /// Chunks by BM25, from our own inverted index: the query's terms and weights against each chunk's, summed — the same
    /// dot product Qdrant computes on the sparse vector. The tenant is part of the match, so it is exact.
    /// </summary>
    private const string RetrievalSparseShape = """
        UNWIND $terms AS term
        MATCH (t:Term {key: term.key})-[r:OCCURS_IN]->(c:RetrievalChunk)
        WHERE t.tenant_id IN $readable AND c.tenant_id IN $readable AND c.collection = $collection{types}
        WITH c, sum(r.w * term.w) AS score
        RETURN {projection}, score
        ORDER BY score DESC, c.key
        LIMIT $limit
        """;

    public static readonly string RetrievalSparse = RetrievalSparseShape.Replace("{types}", "").Replace("{projection}", ChunkProjection);
    public static readonly string RetrievalSparseTyped = RetrievalSparseShape.Replace("{types}", " AND c.source_type IN $types").Replace("{projection}", ChunkProjection);

    /// <summary>Every template, by name: what the guard test checks.</summary>
    public static IEnumerable<(string Name, string Cypher)> All()
    {
        yield return ("billing_neighbourhood_1", BillingNeighbourhood1);
        yield return ("billing_neighbourhood_2", BillingNeighbourhood2);
        yield return ("firm_runs", FirmRuns);
        yield return ("symbol_candidates", SymbolCandidates);
        for (var d = 1; d <= CallTrace.MaxDepth; d++)
        {
            yield return ($"callers_{d}", Callers[d - 1]);
            yield return ($"callees_{d}", Callees[d - 1]);
        }
        yield return ("file_methods", FileMethods);
        yield return ("retrieval_dense", RetrievalDense);
        yield return ("retrieval_dense_typed", RetrievalDenseTyped);
        yield return ("retrieval_sparse", RetrievalSparse);
        yield return ("retrieval_sparse_typed", RetrievalSparseTyped);
    }
}
