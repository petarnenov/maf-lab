using System.Text.RegularExpressions;
using Maf.Lab.Domain.Graph;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Retrieval.Graph;
using Microsoft.Extensions.Options;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Neo4j.Driver;
using NSubstitute;

namespace Maf.Lab.Tests;

/// <summary>
/// The graph read path, proven structurally (graph-store, tenant-isolation): every template guards every node it matches
/// with the readable tenants, only the two tenant-scoped classes open the driver, and the read path alone binds the
/// tenants.
/// </summary>
public class GraphStoreTests
{
    private static readonly Principal FirmA = new("u-a", TenantId.Firm("firm-a"), Role.ADVISOR, []);

    [Fact]
    public void Every_template_guards_every_node_it_matches()
    {
        var templates = GraphTemplates.All().ToList();
        Assert.True(templates.Count >= 12);
        var violations = templates.SelectMany(t => CypherGuard.Violations(t.Cypher).Select(v => $"{t.Name}: {v}")).ToList();
        Assert.True(violations.Count == 0, "Unguarded graph reads:\n" + string.Join("\n", violations));
    }

    [Theory]
    [InlineData("MATCH (a:Account)-[:BELONGS_TO]->(f:Firm) WHERE a.tenant_id IN $readable RETURN f.key", "f")]
    [InlineData("MATCH p = (a:Account)-[*1..2]-(b) WHERE a.tenant_id IN $readable AND b.tenant_id IN $readable RETURN b", "p")]
    [InlineData("MATCH (a:Account)-[*1..2]-(b) WHERE a.tenant_id IN $readable AND b.tenant_id IN $readable RETURN b", "variable-length")]
    [InlineData("MATCH (a:Account)--(:Firm) WHERE a.tenant_id IN $readable RETURN a", "anonymous")]
    // A Cypher 25 vector SEARCH (neo4j-retrieval-spike) whose filter forgot the tenant: the node is caught like any other.
    [InlineData("CYPHER 25 MATCH (c:RetrievalChunk) SEARCH c IN (VECTOR INDEX retrieval_chunk_dense FOR $vector WHERE c.collection = $collection LIMIT $limit) SCORE AS score RETURN c, $readable", "node c is not restricted")]
    public void The_guard_check_catches_an_unguarded_template(string cypher, string expected)
    {
        var violations = CypherGuard.Violations(cypher);
        Assert.Contains(violations, v => v.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void Only_the_tenant_scoped_graph_classes_open_the_driver()
    {
        var calls = FindDriverCalls(ProductAssemblies());

        Assert.NotEmpty(calls);
        var allowed = new[] { typeof(TenantScopedGraph).FullName, typeof(TenantScopedGraphMaintenance).FullName };
        var violations = calls.Where(c => !allowed.Contains(c.Type)).Select(c => $"{c.Type}.{c.Caller} calls {c.Target}").ToList();
        Assert.True(violations.Count == 0, "Unscoped graph paths:\n" + string.Join("\n", violations));
        Assert.Contains(calls, c => c.Type == typeof(TenantScopedGraph).FullName && c.Target.EndsWith("ExecutableQuery"));
    }

    [Fact]
    public void Scanner_detects_a_rogue_graph_path()
    {
        var calls = FindDriverCalls([typeof(RogueGraphFixture).Assembly.Location]);
        Assert.Contains(calls, c => c.Type == typeof(RogueGraphFixture).FullName);
    }

    [Fact]
    public void Tenant_scoped_graph_has_exactly_one_public_read_method_taking_a_principal()
    {
        var methods = typeof(TenantScopedGraph).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly);
        var read = Assert.Single(methods);
        Assert.Equal(typeof(Principal), read.GetParameters()[0].ParameterType);
        foreach (var query in typeof(GraphQuery<>).Assembly.GetTypes().Where(t => t.BaseType is { IsGenericType: true } b && b.GetGenericTypeDefinition() == typeof(GraphQuery<>)))
        {
            Assert.DoesNotContain(query.GetProperties(), p => p.Name.Contains("Tenant", StringComparison.OrdinalIgnoreCase) || p.Name.Contains("Firm", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void The_read_path_binds_the_principals_tenants_and_the_limit_itself()
    {
        var parameters = TenantScopedGraph.Parameters(FirmA, new BillingNeighbourhood("A-1042", 2, limit: 10));

        Assert.Equal(["firm-a", "shared"], (List<string>)parameters["readable"]);
        Assert.Equal(11, parameters["limit"]); // one more than the limit tells whether the result was truncated
        Assert.Equal("A-1042", parameters["id"]);
    }

    [Fact]
    public void A_query_cannot_set_the_reserved_parameters()
    {
        var error = Assert.Throws<InvalidOperationException>(() => TenantScopedGraph.Parameters(FirmA, new SneakyQuery()));
        Assert.Contains("readable", error.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void Billing_depth_outside_1_to_2_is_rejected(int depth) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new BillingNeighbourhood("A-1042", depth));

    [Fact]
    public void Call_trace_depth_is_capped_and_each_depth_has_its_own_constant_text()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CallTrace(["M:x"], TraceDirection.Callers, CallTrace.MaxDepth + 1));
        var texts = Enumerable.Range(1, CallTrace.MaxDepth).Select(d => new CallTrace(["M:x"], TraceDirection.Callers, d).Cypher).ToList();
        Assert.Equal(CallTrace.MaxDepth, texts.Distinct().Count());
        Assert.Contains("*1..3]", texts[2]);
    }

    [Fact]
    public void Neighbourhood_rows_become_entities_and_documents()
    {
        var query = new BillingNeighbourhood("NW-INST-2026-083", 2);
        var rows = new[]
        {
            Row(("start_kind", "FeeSchedule"), ("start_key", "NW-INST-2026-083"), ("kind", "Document"), ("key", "firm-b/docs/a.md"),
                ("title", "A"), ("source_type", "docs"), ("path", "docs/a.md"), ("hops", 1L), ("relation", "MENTIONS")),
            Row(("start_kind", "FeeSchedule"), ("start_key", "NW-INST-2026-083"), ("kind", "FeeSchedule"), ("key", "NW-TIER-2024-108"),
                ("hops", 2L), ("via", "firm-b/docs/a.md"), ("relation", "MENTIONS")),
        };

        var result = query.Map(rows, truncated: true);

        Assert.Equal(new BillingEntity(BillingEntityKinds.FeeSchedule, "NW-INST-2026-083", null), result.Start);
        Assert.Equal("firm-b/docs/a.md", Assert.Single(result.Documents).DocumentId);
        var related = Assert.Single(result.Related);
        Assert.Equal((BillingEntityKinds.FeeSchedule, 2, "firm-b/docs/a.md"), (related.Kind, related.Hops, related.Via));
        Assert.True(result.Truncated);
        Assert.Null(query.Map([], false).Start);
    }

    [Fact]
    public void Options_never_print_the_password()
    {
        var options = new GraphOptions { Uri = "bolt://neo4j:7687", Password = "s3cret-graph" };
        Assert.DoesNotContain("s3cret-graph", options.ToString());
        Assert.Equal("neo4j:7687", options.Authority);
    }

    [Fact]
    public async Task Nodes_without_a_tenant_are_rejected_and_never_written()
    {
        var driver = Substitute.For<IDriver>();
        var maintenance = new TenantScopedGraphMaintenance(driver, Options.Create(new GraphOptions()));
        GraphNode[] nodes =
        [
            new(GraphLabels.Account, default, "A-9", new Dictionary<string, object?>()),
            new(GraphLabels.Account, TenantId.Firm("firm-a"), "", new Dictionary<string, object?>()),
        ];

        var counts = await maintenance.WriteNodesAsync(GraphSources.Billing, "run-1", nodes, TestContext.Current.CancellationToken);

        Assert.Equal(new GraphWriteCounts(0, 0, 2), counts);
        driver.DidNotReceiveWithAnyArgs().ExecutableQuery(default!);
    }

    [Fact]
    public async Task Writes_take_labels_and_sources_from_the_closed_sets_only()
    {
        var maintenance = new TenantScopedGraphMaintenance(Substitute.For<IDriver>(), Options.Create(new GraphOptions()));
        GraphNode[] node = [new("Account) DETACH DELETE (x", TenantId.Firm("firm-a"), "k", new Dictionary<string, object?>())];
        await Assert.ThrowsAsync<ArgumentException>(() => maintenance.WriteNodesAsync(GraphSources.Billing, "r", node, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(() => maintenance.RemoveStaleAsync("everything", "r", TestContext.Current.CancellationToken));
    }

    [Fact]
    public void An_unchanged_node_hashes_the_same_whatever_the_property_order()
    {
        var a = new GraphNode(GraphLabels.Account, TenantId.Firm("firm-a"), "A-1", new Dictionary<string, object?> { ["name"] = "x", ["n"] = 1 });
        var b = new GraphNode(GraphLabels.Account, TenantId.Firm("firm-a"), "A-1", new Dictionary<string, object?> { ["n"] = 1, ["name"] = "x" });
        var c = new GraphNode(GraphLabels.Account, TenantId.Firm("firm-a"), "A-1", new Dictionary<string, object?> { ["n"] = 2, ["name"] = "x" });
        Assert.Equal(a.ContentHash, b.ContentHash);
        Assert.NotEqual(a.ContentHash, c.ContentHash);
    }

    [Fact]
    public void Graph_store_failures_read_as_temporarily_unavailable_without_internals()
    {
        var text = Maf.Lab.Retrieval.Tools.ToolErrors.ForException(new ServiceUnavailableException("Failed to connect to server 'bolt://neo4j:7687/'"), "Graph lookup");
        Assert.Equal("Graph lookup is temporarily unavailable; try again shortly.", text);
    }

    [Fact]
    public void An_unreachable_graph_store_is_named_by_address_without_credentials()
    {
        var line = Maf.Lab.Indexing.UnreachableService.Describe(new ServiceUnavailableException("boom"),
            new Maf.Lab.Retrieval.Configuration.QdrantOptions(), new Maf.Lab.Retrieval.Configuration.ModelOptions(),
            new GraphOptions { Uri = "bolt://localhost:7687", Password = "s3cret-graph" });
        Assert.NotNull(line);
        Assert.Contains("Neo4j is not reachable at localhost:7687", line);
        Assert.DoesNotContain("s3cret", line);
    }

    [Fact]
    public void No_tool_or_agent_code_reaches_a_maintenance_read()
    {
        // add-graph-drift: maintenance reads (counts, stale removal, the drift listing) serve the indexer and the admin
        // drift report only; the request path reads the graph through TenantScopedGraph.ReadAsync alone.
        string[] requestPath = ["Maf.Lab.Retrieval.Tools", "Maf.Lab.CodeSearch.Tools", "Maf.Lab.Api.Agent"];
        string[] maintenance = [typeof(TenantScopedGraphMaintenance).FullName!, "Maf.Lab.Indexing.Pipeline.DriftService"];

        var calls = new List<string>();
        foreach (var path in ProductAssemblies())
        {
            using var assembly = AssemblyDefinition.ReadAssembly(path);
            foreach (var type in assembly.MainModule.GetTypes().Where(t => requestPath.Any(ns => TopLevel(t).Namespace.StartsWith(ns, StringComparison.Ordinal))))
            {
                foreach (var method in type.Methods.Where(m => m.HasBody))
                {
                    calls.AddRange(method.Body.Instructions
                        .Where(i => i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt || i.OpCode == OpCodes.Newobj)
                        .Select(i => i.Operand).OfType<MethodReference>()
                        .Where(t => maintenance.Contains(t.DeclaringType.FullName))
                        .Select(t => $"{Owner(type).FullName}.{method.Name} -> {t.DeclaringType.Name}.{t.Name}"));
                }
            }
        }

        Assert.Empty(calls);
    }

    private static IGraphRow Row(params (string Key, object? Value)[] values) => new DictionaryRow(values.ToDictionary(v => v.Key, v => v.Value));

    private sealed class DictionaryRow(Dictionary<string, object?> values) : IGraphRow
    {
        public object? this[string column] => values.GetValueOrDefault(column);
    }

    private sealed record SneakyQuery : GraphQuery<int>
    {
        public override string Name => "sneaky";
        public override int Limit => 1;
        internal override string Cypher => "RETURN 1";
        internal override IReadOnlyDictionary<string, object?> Arguments => new Dictionary<string, object?> { ["readable"] = new[] { "firm-b" } };
        internal override int Map(IReadOnlyList<IGraphRow> rows, bool truncated) => 0;
    }

    [Fact]
    public void Every_graph_query_is_run_so_that_a_stop_ends_it_in_neo4j()
    {
        // The driver alone runs a cancelled query to its end; GraphStop ends it on the server (stop-anything). So a
        // driver call in the graph classes must sit in the query handed to GraphStop — the terminate statement aside.
        var violations = GraphStopViolations(Path.Combine(AppContext.BaseDirectory, "Maf.Lab.Retrieval.dll"),
            [typeof(TenantScopedGraph).FullName!, typeof(TenantScopedGraphMaintenance).FullName!]);

        Assert.True(violations.Count == 0, "Graph queries a stop would not end in Neo4j:\n" + string.Join("\n", violations));
    }

    /// <summary>
    /// Every driver call in <paramref name="owners"/> that is neither the terminate statement nor inside a lambda whose
    /// enclosing method hands it to <c>GraphStop.RunAsync</c>. A lambda is known by its compiler name (<c>&lt;M&gt;b__…</c>),
    /// an async method's body by its state machine (<c>&lt;M&gt;d__…</c>).
    /// </summary>
    private static List<string> GraphStopViolations(string assemblyPath, string[] owners)
    {
        static string Enclosing(string name) => name.StartsWith('<') && name.IndexOf('>') > 1 ? name[1..name.IndexOf('>')] : name;
        static bool Calls(MethodDefinition method, Func<MethodReference, bool> target) =>
            method.HasBody && method.Body.Instructions.Any(i => (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt)
                && i.Operand is MethodReference m && target(m));
        static bool IsDriverCall(MethodReference m) =>
            m.DeclaringType.FullName == "Neo4j.Driver.IDriver" && m.Name is "ExecutableQuery" or "AsyncSession" or "Session" or "RxSession";
        static bool IsGraphStop(MethodReference m) =>
            m.Name == "RunAsync" && m.DeclaringType.FullName == "Maf.Lab.Retrieval.Graph.GraphStop";

        using var assembly = AssemblyDefinition.ReadAssembly(assemblyPath);
        var violations = new List<string>();
        foreach (var owner in assembly.MainModule.GetTypes().Where(t => owners.Contains(t.FullName)))
        {
            // Every method body of the class: its own, its async state machines' and its closures'.
            var bodies = owner.Methods.Select(m => (Method: m, Name: m.Name))
                .Concat(owner.NestedTypes.SelectMany(n => n.Methods.Select(m => (Method: m, Name: m.Name == "MoveNext" ? n.Name : m.Name))))
                .ToList();
            bool HandsToGraphStop(string method) =>
                bodies.Any(b => Enclosing(b.Name) == method && !b.Name.Contains(">b__") && Calls(b.Method, IsGraphStop));
            foreach (var (method, name) in bodies.Where(b => Calls(b.Method, IsDriverCall)))
            {
                var enclosing = Enclosing(name);
                var inLambda = name.Contains(">b__");
                if (enclosing == "TerminateAsync" && !inLambda)
                {
                    continue;
                }
                if (inLambda && HandsToGraphStop(enclosing))
                {
                    continue;
                }
                violations.Add($"{owner.Name}.{enclosing} calls the driver outside GraphStop");
            }
        }
        return violations;
    }

    private static IEnumerable<string> ProductAssemblies() =>
        new[] { "Maf.Lab.Domain", "Maf.Lab.Retrieval", "Maf.Lab.Api", "Maf.Lab.Indexing", "Maf.Lab.Eval", "Maf.Lab.CodeSearch", "Maf.Lab.Portfolio" }
            .Select(n => Path.Combine(AppContext.BaseDirectory, n + ".dll"));

    /// <summary>Calls that run Cypher: the driver's query and session entry points, and anything on a session or transaction.</summary>
    private static List<(string Type, string Caller, string Target)> FindDriverCalls(IEnumerable<string> assemblies)
    {
        string[] entryPoints = ["ExecutableQuery", "AsyncSession", "Session", "RxSession"];
        string[] runners = ["Neo4j.Driver.IAsyncSession", "Neo4j.Driver.IAsyncQueryRunner", "Neo4j.Driver.IAsyncTransaction"];
        var result = new List<(string, string, string)>();
        foreach (var path in assemblies)
        {
            using var assembly = AssemblyDefinition.ReadAssembly(path);
            foreach (var type in assembly.MainModule.GetTypes())
            {
                foreach (var method in type.Methods.Where(m => m.HasBody))
                {
                    foreach (var ins in method.Body.Instructions.Where(i => i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt))
                    {
                        if (ins.Operand is MethodReference target
                            && ((target.DeclaringType.FullName == "Neo4j.Driver.IDriver" && entryPoints.Contains(target.Name))
                                || runners.Contains(target.DeclaringType.FullName)))
                        {
                            result.Add((Owner(type).FullName.Replace('/', '+'), method.Name, $"{target.DeclaringType.Name}.{target.Name}"));
                        }
                    }
                }
            }
        }
        return result;
    }

    private static TypeDefinition TopLevel(TypeDefinition type) => type.DeclaringType is null ? type : TopLevel(type.DeclaringType);

    private static TypeDefinition Owner(TypeDefinition type)
    {
        while (type.DeclaringType is not null && type.Name.Contains('<'))
        {
            type = type.DeclaringType;
        }
        return type;
    }
}

/// <summary>
/// A conservative check of a read template's Cypher: every named node in a pattern (and every node passed to a function)
/// must be guarded by <c>v.tenant_id IN $readable</c>; every path must be guarded node by node; a variable-length
/// pattern must be bound to a path; anonymous nodes are not allowed, because they cannot be guarded.
/// </summary>
internal static partial class CypherGuard
{
    public static IReadOnlyList<string> Violations(string cypher)
    {
        var violations = new List<string>();
        if (!cypher.Contains("$readable", StringComparison.Ordinal))
        {
            violations.Add("never uses $readable");
        }
        var paths = PathAssignment().Matches(cypher).Select(m => m.Groups[1].Value).ToHashSet();
        var aliases = Alias().Matches(cypher).Select(m => m.Groups[1].Value).ToHashSet();
        foreach (Match node in NodePattern().Matches(cypher))
        {
            var name = node.Groups[1].Value;
            if (name.Length == 0)
            {
                violations.Add($"anonymous node {node.Value}");
                continue;
            }
            if (paths.Contains(name) || aliases.Contains(name))
            {
                continue;
            }
            if (!Regex.IsMatch(cypher, $@"\b{Regex.Escape(name)}\.tenant_id\s+IN\s+\$readable\b"))
            {
                violations.Add($"node {name} is not restricted to $readable");
            }
        }
        foreach (var path in paths)
        {
            if (!Regex.IsMatch(cypher, $@"all\(\s*(\w+)\s+IN\s+nodes\(\s*{Regex.Escape(path)}\s*\)\s+WHERE\s+\1\.tenant_id\s+IN\s+\$readable\s*\)"))
            {
                violations.Add($"path {path} is not restricted to $readable node by node");
            }
        }
        foreach (var line in cypher.Split('\n').Where(l => VariableLength().IsMatch(l)))
        {
            if (!PathAssignment().IsMatch(line))
            {
                violations.Add($"variable-length pattern not bound to a path: {line.Trim()}");
            }
        }
        return violations;
    }

    [GeneratedRegex(@"\(\s*(\w*)\s*((?::\w+)*)\s*(\{[^}]*\})?\s*\)")]
    private static partial Regex NodePattern();

    [GeneratedRegex(@"\b(\w+)\s*=\s*\(")]
    private static partial Regex PathAssignment();

    [GeneratedRegex(@"\bAS\s+(\w+)")]
    private static partial Regex Alias();

    [GeneratedRegex(@"\[[^\]]*\*")]
    private static partial Regex VariableLength();
}

/// <summary>Deliberately unscoped graph path, used only to prove the scanner catches one.</summary>
public sealed class RogueGraphFixture(IDriver driver)
{
    public Task Leak() => driver.ExecutableQuery("MATCH (n) RETURN n").ExecuteAsync();
}
