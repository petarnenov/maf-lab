using System.Xml.Linq;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Retrieval.Graph;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Maf.Lab.Indexing.Graph;

/// <summary>
/// Builds the code subgraph from the repository's C# files with Roslyn's semantic model, so a call edge names the method
/// the compiler would bind, not a name that merely matches. All files go into one compilation, referencing the
/// runtime's own assemblies only: calls between the repository's projects resolve, calls into NuGet packages do not and
/// are counted as unresolved. Every node is shared — the codebase belongs to no firm.
/// </summary>
public static class CodeGraphBuilder
{
    private static readonly TenantId Shared = TenantId.Shared;

    /// <summary>What the SDKs import implicitly in this repository's projects (console, web and test), plus xUnit.</summary>
    private const string ImplicitUsings = """
        global using System;
        global using System.Collections.Generic;
        global using System.IO;
        global using System.Linq;
        global using System.Net.Http;
        global using System.Net.Http.Json;
        global using System.Threading;
        global using System.Threading.Tasks;
        global using Microsoft.AspNetCore.Builder;
        global using Microsoft.AspNetCore.Hosting;
        global using Microsoft.AspNetCore.Http;
        global using Microsoft.AspNetCore.Routing;
        global using Microsoft.Extensions.Configuration;
        global using Microsoft.Extensions.DependencyInjection;
        global using Microsoft.Extensions.Hosting;
        global using Microsoft.Extensions.Logging;
        global using Xunit;
        """;

    private static readonly HashSet<string> TestAttributes = new(StringComparer.Ordinal)
    {
        "Fact", "Theory", "FactAttribute", "TheoryAttribute", "Test", "TestMethod", "TestCase",
    };

    public static GraphBuild Build(IReadOnlyList<CodeProject> projects, IReadOnlyList<CodeFile> files, IProgress<string>? progress = null)
    {
        var nodes = new Dictionary<(string Label, string Key), GraphNode>();
        var edges = new HashSet<GraphEdge>();
        var rejected = new List<string>();

        void Node(string label, string key, Dictionary<string, object?> props) => nodes.TryAdd((label, key), new GraphNode(label, Shared, key, props));
        void Edge(string fromLabel, string fromKey, string type, string toLabel, string toKey) =>
            edges.Add(new GraphEdge(fromLabel, Shared, fromKey, type, toLabel, Shared, toKey));

        foreach (var project in projects)
        {
            Node(GraphLabels.Project, project.Name, new() { ["name"] = project.Name, ["path"] = project.Directory });
        }
        var projectNames = projects.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var project in projects)
        {
            foreach (var reference in project.References.Where(projectNames.Contains))
            {
                Edge(GraphLabels.Project, project.Name, GraphRelations.References, GraphLabels.Project, reference);
            }
        }

        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
        var trees = files.Select(f => CSharpSyntaxTree.ParseText(f.Text, parseOptions, path: f.Path)).ToList();
        trees.Add(CSharpSyntaxTree.ParseText(ImplicitUsings, parseOptions, path: "<implicit-usings>"));
        var compilation = CSharpCompilation.Create("maf-lab-code-graph", trees, PlatformReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var unresolved = 0;
        foreach (var tree in trees.Where(t => t.FilePath != "<implicit-usings>"))
        {
            var path = tree.FilePath;
            progress?.Report(path);
            Node(GraphLabels.File, path, new() { ["path"] = path });
            if (OwningProject(projects, path) is { } owner)
            {
                Edge(GraphLabels.Project, owner.Name, GraphRelations.Contains, GraphLabels.File, path);
            }

            var model = compilation.GetSemanticModel(tree, ignoreAccessibility: true);
            var root = tree.GetRoot();
            foreach (var typeSyntax in root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
            {
                if (model.GetDeclaredSymbol(typeSyntax) is not INamedTypeSymbol type || type.GetDocumentationCommentId() is not { } typeKey)
                {
                    continue;
                }
                var (typeStart, typeEnd) = Lines(typeSyntax);
                Node(GraphLabels.Type, typeKey, new()
                {
                    ["name"] = TypeName(type), ["full_name"] = FullName(type), ["path"] = path, ["start_line"] = typeStart, ["end_line"] = typeEnd,
                });
                Edge(GraphLabels.File, path, GraphRelations.Declares, GraphLabels.Type, typeKey);
            }

            foreach (var member in root.DescendantNodes().OfType<BaseMethodDeclarationSyntax>())
            {
                if (member is not (MethodDeclarationSyntax or ConstructorDeclarationSyntax)
                    || model.GetDeclaredSymbol(member) is not IMethodSymbol method
                    || Key(method) is not { } methodKey)
                {
                    continue;
                }
                var (start, end) = Lines(member);
                Node(GraphLabels.Method, methodKey, MethodProperties(method, path, start, end, IsTest(member)));
                Edge(GraphLabels.File, path, GraphRelations.Declares, GraphLabels.Method, methodKey);
                if (Key(method.ContainingType) is { } containingKey)
                {
                    Edge(GraphLabels.Type, containingKey, GraphRelations.Declares, GraphLabels.Method, methodKey);
                }

                foreach (var call in member.DescendantNodes())
                {
                    if (call is not (InvocationExpressionSyntax or BaseObjectCreationExpressionSyntax or ConstructorInitializerSyntax))
                    {
                        continue;
                    }
                    var info = model.GetSymbolInfo(call);
                    var target = (info.Symbol ?? info.CandidateSymbols.FirstOrDefault()) as IMethodSymbol;
                    target = target?.ReducedFrom ?? target;
                    target = target?.OriginalDefinition;
                    if (target is null || !InRepository(target) || Key(target) is not { } targetKey)
                    {
                        unresolved++;
                        continue;
                    }
                    if (targetKey != methodKey)
                    {
                        Edge(GraphLabels.Method, methodKey, GraphRelations.Calls, GraphLabels.Method, targetKey);
                    }
                }
            }
        }

        // A call can bind to a method whose declaration was not walked (a record's synthesized members, a partial
        // declaration without a body); the edge would point at nothing, so it is dropped.
        var methodKeys = nodes.Keys.Where(k => k.Label == GraphLabels.Method).Select(k => k.Key).ToHashSet(StringComparer.Ordinal);
        var dangling = edges.Where(e => e.Type == GraphRelations.Calls && !methodKeys.Contains(e.ToKey)).ToList();
        foreach (var edge in dangling)
        {
            edges.Remove(edge);
        }
        unresolved += dangling.Count;

        return new GraphBuild(GraphSources.Code, [.. nodes.Values], [.. edges], rejected, unresolved);
    }

    /// <summary>The repository's projects under the given folders, with their project references by name.</summary>
    public static IReadOnlyList<CodeProject> FindProjects(string root, IEnumerable<string> folders)
    {
        var projects = new List<CodeProject>();
        foreach (var folder in folders)
        {
            var full = Path.Combine(root, folder);
            if (!Directory.Exists(full))
            {
                continue;
            }
            foreach (var csproj in Directory.EnumerateFiles(full, "*.csproj", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            {
                var relative = Path.GetRelativePath(root, Path.GetDirectoryName(csproj)!).Replace('\\', '/');
                if (relative.Split('/').Any(s => s is "bin" or "obj" or "node_modules"))
                {
                    continue;
                }
                var references = XDocument.Load(csproj).Descendants("ProjectReference")
                    .Select(r => (string?)r.Attribute("Include"))
                    .Where(i => i is not null)
                    .Select(i => Path.GetFileNameWithoutExtension(i!.Replace('\\', '/')))
                    .ToList();
                projects.Add(new CodeProject(relative, Path.GetFileNameWithoutExtension(csproj), references));
            }
        }
        return projects;
    }

    internal static CodeProject? OwningProject(IReadOnlyList<CodeProject> projects, string path) =>
        projects.Where(p => path.StartsWith(p.Directory + "/", StringComparison.Ordinal))
            .OrderByDescending(p => p.Directory.Length)
            .FirstOrDefault();

    private static Dictionary<string, object?> MethodProperties(IMethodSymbol method, string path, int start, int end, bool isTest)
    {
        var type = method.ContainingType;
        var name = method.MethodKind == MethodKind.Constructor ? type.Name : method.Name;
        return new()
        {
            ["name"] = name,
            ["display"] = $"{TypeName(type)}.{name}",
            ["full_name"] = $"{FullName(type)}.{name}",
            ["type_name"] = TypeName(type),
            ["type_full_name"] = FullName(type),
            ["path"] = path,
            ["start_line"] = start,
            ["end_line"] = end,
            ["is_test"] = isTest,
        };
    }

    /// <summary>"Outer.Inner" for nested types; the simple name otherwise.</summary>
    private static string TypeName(INamedTypeSymbol type) =>
        type.ContainingType is { } outer ? $"{TypeName(outer)}.{type.Name}" : type.Name;

    private static string FullName(INamedTypeSymbol type) =>
        type.ContainingNamespace is { IsGlobalNamespace: false } ns ? $"{ns.ToDisplayString()}.{TypeName(type)}" : TypeName(type);

    private static string? Key(ISymbol symbol) => symbol.OriginalDefinition.GetDocumentationCommentId();

    private static bool InRepository(ISymbol symbol) =>
        symbol.DeclaringSyntaxReferences.Any(r => r.SyntaxTree.FilePath is { Length: > 0 } p && p != "<implicit-usings>");

    private static bool IsTest(BaseMethodDeclarationSyntax member) =>
        member.AttributeLists.SelectMany(l => l.Attributes)
            .Any(a => TestAttributes.Contains(LastSegment(a.Name.ToString())));

    private static string LastSegment(string name) => name[(name.LastIndexOf('.') + 1)..];

    /// <summary>1-based first and last line of the declaration, attributes included, leading comments not.</summary>
    private static (int Start, int End) Lines(SyntaxNode node)
    {
        var span = node.GetLocation().GetLineSpan();
        return (span.StartLinePosition.Line + 1, span.EndLinePosition.Line + 1);
    }

    /// <summary>The assemblies of the runtime the indexer runs on: enough to bind the repository's own calls.</summary>
    private static IEnumerable<MetadataReference> PlatformReferences() =>
        ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            // The indexer's own copies of the repository's assemblies would compete with the sources being read.
            .Where(p => !Path.GetFileName(p).StartsWith("Maf.Lab.", StringComparison.Ordinal))
            .Select(p => MetadataReference.CreateFromFile(p));
}
