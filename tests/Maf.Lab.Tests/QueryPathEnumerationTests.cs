using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Maf.Lab.Tests;

/// <summary>
/// Proves structurally that no code path reads or writes chunk points except through the two tenant-scoped classes.
/// Scans the IL of every Maf.Lab.* assembly for calls to QdrantClient data-plane methods.
/// </summary>
public class QueryPathEnumerationTests
{
    private static readonly HashSet<string> DataPlane =
    [
        "QueryAsync", "QueryBatchAsync", "QueryGroupsAsync", "SearchAsync", "SearchBatchAsync", "SearchGroupsAsync",
        "ScrollAsync", "RecommendAsync", "DiscoverAsync", "CountAsync", "FacetAsync", "RetrieveAsync",
        "UpsertAsync", "DeleteAsync", "UpdateVectorsAsync", "DeleteVectorsAsync", "SetPayloadAsync", "OverwritePayloadAsync",
        "DeletePayloadAsync", "ClearPayloadAsync", "UpdateBatchAsync", "SearchMatrixPairsAsync", "SearchMatrixOffsetsAsync",
    ];

    /// <summary>Type → data-plane methods it may call. Bm25Store touches only the meta collection (no chunk content).</summary>
    private static readonly Dictionary<string, string[]> Allowed = new()
    {
        ["Maf.Lab.Retrieval.Store.TenantScopedSearch"] = ["QueryAsync"],
        ["Maf.Lab.Retrieval.Store.TenantScopedMaintenance"] = ["UpsertAsync", "CountAsync", "DeleteAsync", "ScrollAsync", "FacetAsync", "UpdateVectorsAsync", "SetPayloadAsync"],
        ["Maf.Lab.Retrieval.Sparse.Bm25Store"] = ["RetrieveAsync", "UpsertAsync"],
    };

    [Fact]
    public void Only_tenant_scoped_classes_touch_chunk_points()
    {
        var calls = FindQdrantDataPlaneCalls(ProductAssemblies());

        Assert.NotEmpty(calls);
        var violations = calls
            .Where(c => !Allowed.TryGetValue(c.Type, out var methods) || !methods.Contains(c.Method))
            .Select(c => $"{c.Type}.{c.Caller} calls QdrantClient.{c.Method}")
            .ToList();
        Assert.True(violations.Count == 0, "Unscoped query paths:\n" + string.Join("\n", violations));
        Assert.Contains(calls, c => c.Type.EndsWith("TenantScopedSearch") && c.Method == "QueryAsync");
    }

    [Fact]
    public void Tenant_scoped_search_has_exactly_one_public_query_method_taking_a_principal()
    {
        var type = typeof(Maf.Lab.Retrieval.Store.TenantScopedSearch);
        var methods = type.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly);
        var query = Assert.Single(methods);
        Assert.Equal(typeof(Maf.Lab.Domain.Tenancy.Principal), query.GetParameters()[0].ParameterType);
        Assert.DoesNotContain(typeof(Maf.Lab.Retrieval.Store.SearchRequest).GetProperties(), p => p.Name.Contains("Tenant", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Scanner_detects_a_rogue_query_path()
    {
        var fixture = typeof(RogueQueryFixture).Assembly.Location;
        var calls = FindQdrantDataPlaneCalls([fixture]);
        Assert.Contains(calls, c => c.Type == typeof(RogueQueryFixture).FullName && c.Method == "ScrollAsync");
    }

    private static IEnumerable<string> ProductAssemblies() =>
        new[] { "Maf.Lab.Domain", "Maf.Lab.Retrieval", "Maf.Lab.Api", "Maf.Lab.Indexing", "Maf.Lab.Eval" }
            .Select(n => Path.Combine(AppContext.BaseDirectory, n + ".dll"));

    private static List<(string Type, string Caller, string Method)> FindQdrantDataPlaneCalls(IEnumerable<string> assemblies)
    {
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
                            && target.DeclaringType.FullName == "Qdrant.Client.QdrantClient"
                            && DataPlane.Contains(target.Name))
                        {
                            result.Add((OwnerType(type).FullName.Replace('/', '+'), method.Name, target.Name));
                        }
                    }
                }
            }
        }
        return result;
    }

    /// <summary>Async state machines and lambdas are nested types; attribute them to the declaring type.</summary>
    private static TypeDefinition OwnerType(TypeDefinition type)
    {
        while (type.DeclaringType is not null && (type.Name.Contains('<') || type.Name.StartsWith("<")))
        {
            type = type.DeclaringType;
        }
        return type;
    }
}

/// <summary>Deliberately unscoped query path used only to prove the scanner catches one.</summary>
public sealed class RogueQueryFixture(Qdrant.Client.QdrantClient client)
{
    public Task Leak() => client.ScrollAsync("maf_chunks");
}
