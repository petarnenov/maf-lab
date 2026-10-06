using System.Text.RegularExpressions;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Maf.Lab.Tests;

/// <summary>
/// The plugin boundary as fitness functions (introduce-plugins task 3.3): the core never references a plugin; a plugin
/// never reaches Qdrant, Neo4j or AG-UI types, never takes a tenant parameter, and never contributes a tool in
/// process. Each rule is run against the real plugin assemblies and, to prove the scanner itself, against planted
/// violations in this test assembly.
/// </summary>
public sealed class PluginArchitectureTests
{
    /// <summary>Every in-process plugin's assembly next to the tests (Directory.Build.targets references them all).</summary>
    public static IEnumerable<string> PluginAssemblies() =>
        Directory.EnumerateFiles(AppContext.BaseDirectory, "Maf.Lab.Plugins.*.dll")
            .Where(p => Path.GetFileNameWithoutExtension(p) != "Maf.Lab.Plugins.Abstractions");

    private static readonly string[] CoreAssemblies =
    [
        "Maf.Lab.Domain", "Maf.Lab.Hosting", "Maf.Lab.Retrieval", "Maf.Lab.Api", "Maf.Lab.Indexing", "Maf.Lab.A2A", "Maf.Lab.TestGen",
        "Maf.Lab.Plugins.Abstractions",
    ];

    private static readonly string[] Forbidden = ["Qdrant.Client", "Neo4j.Driver", "AGUI.Abstractions", "AGUI.Server"];
    private static readonly Regex TenantParameter = new("^(tenant|tenantid|firm|firmid)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    [Fact]
    public void The_core_references_no_plugin()
    {
        var violations = CoreAssemblies
            .Select(n => Path.Combine(AppContext.BaseDirectory, n + ".dll"))
            .SelectMany(path =>
            {
                using var assembly = AssemblyDefinition.ReadAssembly(path);
                return assembly.MainModule.AssemblyReferences
                    .Where(r => r.Name.StartsWith("Maf.Lab.Plugins.", StringComparison.Ordinal) && r.Name != "Maf.Lab.Plugins.Abstractions")
                    .Select(r => $"{assembly.Name.Name} references {r.Name}")
                    .ToList();
            })
            .ToList();
        Assert.True(violations.Count == 0, string.Join("\n", violations));
    }

    /// <summary>
    /// The other direction: a plugin reaches the core only through the abstractions and the shared libraries (Domain,
    /// Retrieval) — never the api or the other services' assemblies, whose types are the core's to change.
    /// </summary>
    [Fact]
    public void No_plugin_references_the_core()
    {
        string[] core = ["Maf.Lab.Api", "Maf.Lab.Hosting", "Maf.Lab.Indexing", "Maf.Lab.A2A", "Maf.Lab.TestGen"];
        var plugins = PluginAssemblies().ToList();
        Assert.NotEmpty(plugins);
        AssertNone(plugins.SelectMany(path =>
        {
            using var assembly = AssemblyDefinition.ReadAssembly(path);
            return assembly.MainModule.AssemblyReferences.Where(r => core.Contains(r.Name))
                .Select(r => $"{assembly.Name.Name} references {r.Name}").ToList();
        }), "references the core");
    }

    [Fact]
    public void No_plugin_reaches_Qdrant_Neo4j_or_AGUI() =>
        AssertNone(PluginAssemblies().SelectMany(ForbiddenReferences), "references a store or AG-UI directly");

    [Fact]
    public void No_plugin_takes_a_tenant_parameter() =>
        AssertNone(PluginAssemblies().SelectMany(TenantParameters), "takes a tenant parameter");

    [Fact]
    public void No_plugin_contributes_a_tool_in_process() =>
        AssertNone(PluginAssemblies().SelectMany(InProcessTools), "contributes a tool in process (tools come only through MCP)");

    [Fact]
    public void The_scanners_catch_planted_violations()
    {
        var planted = typeof(RoguePluginFixture).Assembly.Location;
        Assert.Contains(TenantParameters(planted), v => v.Contains(nameof(RoguePluginFixture.Lookup)));
        Assert.Contains(InProcessTools(planted), v => v.Contains(nameof(RoguePluginFixture)));
        // This test assembly references the stores on purpose; the scanner names them.
        Assert.Contains(ForbiddenReferences(planted), v => v.Contains("Qdrant.Client"));
    }

    private static IEnumerable<string> ForbiddenReferences(string path)
    {
        using var assembly = AssemblyDefinition.ReadAssembly(path);
        return assembly.MainModule.AssemblyReferences.Where(r => Forbidden.Contains(r.Name))
            .Select(r => $"{assembly.Name.Name} → {r.Name}").ToList();
    }

    private static IEnumerable<string> TenantParameters(string path)
    {
        using var assembly = AssemblyDefinition.ReadAssembly(path);
        return assembly.MainModule.GetTypes()
            .Where(t => path != typeof(RoguePluginFixture).Assembly.Location || t.FullName.StartsWith(typeof(RoguePluginFixture).FullName!, StringComparison.Ordinal))
            .SelectMany(t => t.Methods.SelectMany(m => m.Parameters.Where(p => TenantParameter.IsMatch(p.Name))
                .Select(p => $"{t.FullName}.{m.Name}({p.Name})")))
            .ToList();
    }

    private static IEnumerable<string> InProcessTools(string path)
    {
        using var assembly = AssemblyDefinition.ReadAssembly(path);
        var types = assembly.MainModule.GetTypes()
            .Where(t => path != typeof(RoguePluginFixture).Assembly.Location || t.FullName.StartsWith(typeof(RoguePluginFixture).FullName!, StringComparison.Ordinal))
            .ToList();
        var implements = types.Where(t => Ancestry(t).Any(n => n is "Microsoft.Extensions.AI.AITool" or "Microsoft.Extensions.AI.AIFunction"))
            .Select(t => $"{t.FullName} is a tool");
        var creates = types.SelectMany(t => t.Methods.Where(m => m.HasBody).SelectMany(m => m.Body.Instructions
            .Where(i => (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt) && i.Operand is MethodReference r
                && r.DeclaringType.FullName == "Microsoft.Extensions.AI.AIFunctionFactory")
            .Select(_ => $"{t.FullName}.{m.Name} calls AIFunctionFactory")));
        return implements.Concat(creates).Distinct().ToList();
    }

    private static IEnumerable<string> Ancestry(TypeDefinition type)
    {
        for (var b = type.BaseType; b is not null;)
        {
            yield return b.FullName;
            try
            {
                b = b.Resolve()?.BaseType;
            }
            catch (AssemblyResolutionException)
            {
                yield break;
            }
        }
    }

    private static void AssertNone(IEnumerable<string> violations, string what)
    {
        var list = violations.ToList();
        Assert.True(list.Count == 0, $"{list.Count} plugin violation(s): {what}:\n  " + string.Join("\n  ", list));
    }
}

/// <summary>Planted violations for the scanners above: never loaded as a plugin, only read as IL.</summary>
public sealed class RoguePluginFixture
{
    public static string Lookup(string tenantId) => tenantId;

    public static object MakeTool() => Microsoft.Extensions.AI.AIFunctionFactory.Create(() => "rogue");
}
