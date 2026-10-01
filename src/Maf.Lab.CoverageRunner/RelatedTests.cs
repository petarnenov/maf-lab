using Maf.Lab.TestGen;
using Maf.Lab.TestGen.Coverage;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Maf.Lab.CoverageRunner;

/// <summary>
/// What a toolchain is told to run. <see cref="Filters"/> is empty for the whole suite; for related tests it is the
/// .NET test classes (fully qualified) or the Vitest file arguments (relative to <c>web/</c>).
/// </summary>
public sealed record TestPlan(TestSelection Selection, IReadOnlyList<string> Filters)
{
    public static readonly TestPlan Whole = new(TestSelection.Whole, []);

    public bool Related => Selection.Scope == TestScope.Related;

    public static TestPlan FellBack(string reason) => new(TestSelection.FellBack(reason), []);
}

/// <summary>
/// The related tests of a target in a workspace with the diff applied: the test files the diff adds or changes, and
/// the test files that use the target or one of those. Anything the rule cannot follow runs the whole suite instead,
/// with the reason.
/// </summary>
public static class RelatedTests
{
    public const string NothingSelected = "no test uses the target or a changed test file";

    public static TestPlan Plan(string toolchain, string workspace, RunnerOptions options, string target, IReadOnlyList<string> changed) =>
        toolchain == Toolchains.Vitest
            ? Vitest(workspace, target, changed, options.VitestSetupFile)
            : Dotnet(workspace, options.DotnetTestProject, target, changed);

    /// <summary>
    /// .NET: a test file of the unit test project uses a file when one of its identifiers is a type that file declares.
    /// The seeds are the target and every changed test file, so the users of a changed helper run too. The plan names
    /// every class declared in a selected file.
    /// </summary>
    public static TestPlan Dotnet(string workspace, string testProject, string target, IReadOnlyList<string> changed)
    {
        var project = testProject.Trim('/') + "/";
        foreach (var path in changed)
        {
            if (!path.StartsWith(project, StringComparison.Ordinal) || !path.EndsWith(".cs", StringComparison.Ordinal))
            {
                return TestPlan.FellBack($"the diff changes {path}, which the test selection does not follow");
            }
        }
        var changedNow = changed.Where(p => File.Exists(Path.Combine(workspace, p))).ToHashSet(StringComparer.Ordinal);

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var seed in changedNow.Append(target))
        {
            if (Parse(workspace, seed) is { } tree)
            {
                names.UnionWith(DeclaredTypes(tree).Select(t => t.Identifier.ValueText));
            }
        }

        var root = Path.Combine(workspace, testProject);
        var files = new List<string>();
        var classes = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var full in Directory.Exists(root) ? Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories) : [])
        {
            var rel = Path.GetRelativePath(workspace, full).Replace('\\', '/');
            if (rel.Contains("/bin/", StringComparison.Ordinal) || rel.Contains("/obj/", StringComparison.Ordinal))
            {
                continue;
            }
            var tree = Parse(workspace, rel)!;
            var uses = changedNow.Contains(rel) || tree.GetRoot().DescendantTokens()
                .Any(t => t.IsKind(SyntaxKind.IdentifierToken) && names.Contains(t.ValueText));
            if (!uses)
            {
                continue;
            }
            files.Add(rel);
            classes.UnionWith(DeclaredTypes(tree).OfType<TypeDeclarationSyntax>()
                .Where(t => t is ClassDeclarationSyntax or RecordDeclarationSyntax or StructDeclarationSyntax)
                .Select(MetadataName));
        }
        if (classes.Count == 0)
        {
            return TestPlan.FellBack(NothingSelected);
        }
        files.Sort(StringComparer.Ordinal);
        return new TestPlan(new TestSelection(TestScope.Related, files.Take(TestSelection.MaxFiles).ToList()), [.. classes]);
    }

    /// <summary>
    /// Vitest: its own module graph decides (<c>vitest related</c>). The setup file is loaded by every test and imported
    /// by none, so a change to it, or to anything outside <c>web/src/</c>, runs the whole suite.
    /// </summary>
    public static TestPlan Vitest(string workspace, string target, IReadOnlyList<string> changed, string setupFile)
    {
        const string web = "web/";
        if (!target.StartsWith(web, StringComparison.Ordinal))
        {
            return TestPlan.FellBack($"the target {target} is not under {web}");
        }
        foreach (var path in changed)
        {
            if (!path.StartsWith("web/src/", StringComparison.Ordinal) || path == setupFile)
            {
                return TestPlan.FellBack($"the diff changes {path}, which the test selection does not follow");
            }
        }
        var args = changed.Where(p => File.Exists(Path.Combine(workspace, p))).Prepend(target)
            .Select(p => p[web.Length..]).Distinct(StringComparer.Ordinal).ToList();
        return new TestPlan(new TestSelection(TestScope.Related, []), args);
    }

    /// <summary>A type's name as the test platform filters it: namespace, <c>+</c> for nesting, <c>`N</c> for generics.</summary>
    public static string MetadataName(BaseTypeDeclarationSyntax type)
    {
        static string Own(BaseTypeDeclarationSyntax t) =>
            t is TypeDeclarationSyntax { TypeParameterList.Parameters.Count: > 0 and var n } ? $"{t.Identifier.ValueText}`{n}" : t.Identifier.ValueText;

        var name = string.Join("+", type.AncestorsAndSelf().OfType<BaseTypeDeclarationSyntax>().Reverse().Select(Own));
        var ns = string.Join(".", type.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().Reverse().Select(n => n.Name.ToString()));
        return ns.Length > 0 ? $"{ns}.{name}" : name;
    }

    private static IEnumerable<BaseTypeDeclarationSyntax> DeclaredTypes(SyntaxTree tree) =>
        tree.GetRoot().DescendantNodes().OfType<BaseTypeDeclarationSyntax>();

    private static SyntaxTree? Parse(string workspace, string path)
    {
        var full = Path.Combine(workspace, path);
        return path.EndsWith(".cs", StringComparison.Ordinal) && File.Exists(full)
            ? CSharpSyntaxTree.ParseText(File.ReadAllText(full), path: path)
            : null;
    }
}
