using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Maf.Lab.TestGen;

/// <summary>One broken rule, in words the model can act on: where, and what.</summary>
public sealed record GuardrailViolation(string Path, string Test, string Rule)
{
    public override string ToString() => $"{Path}: {Test}: {Rule}";
}

/// <summary>
/// What a generated test may not be: skipped or focused, free of assertions, or passing because it catches the very
/// exception it should verify. Checked in code, deterministically, in the agent after every attempt and again in the
/// api before verification — never left to the model's word (and not to Jev: there is nothing to judge).
/// C# is read as syntax (Roslyn); TypeScript by a small scanner that blanks comments and strings first, because
/// neither the agent nor the api carries node.
/// </summary>
public static partial class TestGuardrails
{
    public const string Skipped = "is skipped without being reported as a suspected bug";
    public const string TooManyBugs = "is one suspected bug too many: a run reports at most 3";
    public const string ModifiesProduction = "writes, moves or deletes production code: a test never changes the code it tests";
    public const string Focused = "is focused (.only / fit): it would silence every other test";
    public const string NoAssertion = "asserts nothing";
    public const string SwallowsException = "catches an exception without asserting on it: use Assert.Throws / expect(...).toThrow";

    /// <summary>
    /// The violations in <paramref name="files"/>. A skip is allowed only for a test in <paramref name="suspectedBugs"/>
    /// whose skip carries the suspected-bug marker, and only for the first <see cref="SuspectedBug.MaxPerRun"/>.
    /// </summary>
    public static IReadOnlyList<GuardrailViolation> Check(IReadOnlyDictionary<string, string> files,
        IReadOnlyList<SuspectedBug>? suspectedBugs = null)
    {
        var bugs = suspectedBugs ?? [];
        var allowed = bugs.Take(SuspectedBug.MaxPerRun).ToList();
        var violations = files.SelectMany(f => f.Key.EndsWith(".cs", StringComparison.Ordinal) ? CheckCSharp(f.Key, f.Value, allowed)
            : f.Key.EndsWith(".ts", StringComparison.Ordinal) || f.Key.EndsWith(".tsx", StringComparison.Ordinal) ? CheckTypeScript(f.Key, f.Value, allowed)
            : []).ToList();
        violations.AddRange(bugs.Skip(SuspectedBug.MaxPerRun).Select(b => new GuardrailViolation(b.TestFile, b.Test, TooManyBugs)));
        return violations;
    }

    /// <summary>Whether a skipped test is an allowed suspected bug: marked as one, and reported as one.</summary>
    private static bool AllowedSkip(string path, string test, bool marked, IReadOnlyList<SuspectedBug> allowed) =>
        marked && allowed.Any(b => b.TestFile == path && (b.Test == test || test.EndsWith("." + b.Test, StringComparison.Ordinal)));

    private static readonly HashSet<string> FileWrites =
    [
        "WriteAllText", "WriteAllTextAsync", "WriteAllBytes", "WriteAllBytesAsync", "WriteAllLines", "WriteAllLinesAsync",
        "AppendAllText", "AppendAllTextAsync", "AppendAllLines", "AppendAllLinesAsync", "AppendText", "Delete", "Move",
        "Copy", "Replace", "Create", "CreateText", "OpenWrite", "CreateDirectory", "SetLastWriteTime",
    ];

    /// <summary>
    /// A path argument that names production source: src/… or web/src/…, with either separator — and not a copy of it
    /// under a temporary directory, which is how a test builds its own fixture tree.
    /// </summary>
    private static bool NamesProduction(string argument) =>
        ProductionPath().IsMatch(argument.Replace("\\\\", "/").Replace('\\', '/'))
        && !argument.Contains("temp", StringComparison.OrdinalIgnoreCase)
        && !argument.Contains("tmp", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"(^|[""'`/\s(])(web/)?src(/|[""'`]\s*[,)])")]
    private static partial Regex ProductionPath();

    // ---- C# ----

    private static readonly string[] TestAttributes = ["Fact", "Theory"];

    public static IReadOnlyList<GuardrailViolation> CheckCSharp(string path, string source, IReadOnlyList<SuspectedBug>? allowed = null)
    {
        allowed ??= [];
        var root = CSharpSyntaxTree.ParseText(source).GetCompilationUnitRoot();
        var violations = new List<GuardrailViolation>();
        foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            var attributes = method.AttributeLists.SelectMany(l => l.Attributes).Where(a => IsTestAttribute(a.Name)).ToList();
            if (attributes.Count == 0)
            {
                continue;
            }
            var name = $"{(method.Parent as TypeDeclarationSyntax)?.Identifier.Text}.{method.Identifier.Text}";
            var skips = attributes.SelectMany(a => a.ArgumentList?.Arguments ?? []).Where(arg =>
                arg.NameEquals?.Name.Identifier.Text is "Skip" or "SkipUnless" or "SkipWhen"
                || (arg.NameEquals?.Name.Identifier.Text == "Explicit" && arg.Expression.IsKind(SyntaxKind.TrueLiteralExpression))).ToList();
            if (skips.Count > 0)
            {
                var marked = skips.All(arg => arg.NameEquals?.Name.Identifier.Text == "Skip"
                    && arg.Expression is LiteralExpressionSyntax { Token.ValueText: var reason }
                    && reason.StartsWith(SuspectedBug.Marker, StringComparison.Ordinal));
                if (!AllowedSkip(path, name, marked, allowed))
                {
                    violations.Add(new(path, name, Skipped));
                }
            }
            SyntaxNode? body = (SyntaxNode?)method.Body ?? method.ExpressionBody;
            if (body is null)
            {
                continue;
            }
            if (!body.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>().Any(IsAssertion))
            {
                violations.Add(new(path, name, NoAssertion));
            }
            if (body.DescendantNodes().OfType<InvocationExpressionSyntax>().Any(WritesProduction))
            {
                violations.Add(new(path, name, ModifiesProduction));
            }
            foreach (var catchClause in body.DescendantNodes().OfType<CatchClauseSyntax>())
            {
                var asserts = catchClause.Block.DescendantNodes().OfType<InvocationExpressionSyntax>().Any(IsAssertion);
                var rethrows = catchClause.Block.DescendantNodes().Any(n => n is ThrowStatementSyntax or ThrowExpressionSyntax);
                if (!asserts && !rethrows)
                {
                    violations.Add(new(path, name, SwallowsException));
                }
            }
        }
        return violations;
    }

    /// <summary>File.WriteAllText("src/…"), Directory.Delete("web/src/…") and the like.</summary>
    private static bool WritesProduction(InvocationExpressionSyntax call) =>
        call.Expression is MemberAccessExpressionSyntax { Expression: var target, Name.Identifier.Text: var method }
        && target.ToString() is "File" or "Directory" or "System.IO.File" or "System.IO.Directory"
        && FileWrites.Contains(method)
        && call.ArgumentList.Arguments.Any(a => NamesProduction(a.ToString()));

    private static bool IsTestAttribute(NameSyntax name)
    {
        var text = name switch
        {
            QualifiedNameSyntax q => q.Right.Identifier.Text,
            IdentifierNameSyntax i => i.Identifier.Text,
            GenericNameSyntax g => g.Identifier.Text,
            _ => name.ToString(),
        };
        return TestAttributes.Any(t => text == t || text == t + "Attribute");
    }

    /// <summary>
    /// A call that checks something: Assert.* and Record.Exception, FluentAssertions' Should(), a mock's Verify, or
    /// a helper whose name says it asserts (AssertTree, ExpectFailure).
    /// </summary>
    private static bool IsAssertion(InvocationExpressionSyntax call)
    {
        var name = call.Expression switch
        {
            MemberAccessExpressionSyntax m => m.Name.Identifier.Text,
            IdentifierNameSyntax i => i.Identifier.Text,
            GenericNameSyntax g => g.Identifier.Text,
            _ => "",
        };
        var target = (call.Expression as MemberAccessExpressionSyntax)?.Expression.ToString() ?? "";
        return target is "Assert" or "Xunit.Assert"
            || name.StartsWith("Assert", StringComparison.Ordinal)
            || name.StartsWith("Should", StringComparison.Ordinal)
            || name.StartsWith("Verify", StringComparison.Ordinal)
            || name.StartsWith("Expect", StringComparison.Ordinal);
    }

    // ---- TypeScript ----

    public static IReadOnlyList<GuardrailViolation> CheckTypeScript(string path, string source, IReadOnlyList<SuspectedBug>? allowed = null)
    {
        allowed ??= [];
        var code = Blank(source);
        var violations = new List<GuardrailViolation>();

        foreach (Match m in Modified().Matches(code))
        {
            var name = TestName(source, code, m.Index + m.Length);
            if (m.Groups["mod"].Value == "only")
            {
                violations.Add(new(path, name, Focused));
            }
            else if (!(m.Groups["mod"].Value == "skip" && m.Groups["fn"].Value is "it" or "test"
                       && AllowedSkip(path, name, MarkedNear(source, m.Index), allowed)))
            {
                violations.Add(new(path, name, Skipped));
            }
        }
        foreach (Match m in Prefixed().Matches(code))
        {
            var focused = m.Groups["fn"].Value is "fit" or "fdescribe";
            violations.Add(new(path, TestName(source, code, m.Index + m.Length), focused ? Focused : Skipped));
        }

        foreach (Match m in FsWrite().Matches(code))
        {
            var open = m.Index + m.Length - 1;
            var close = Matching(code, open, '(', ')');
            if (close > 0 && NamesProduction(source[open..close]))
            {
                violations.Add(new(path, EnclosingTest(source, code, m.Index), ModifiesProduction));
            }
        }

        foreach (Match m in TestCall().Matches(code))
        {
            // The call's own parenthesis ends the match; for it.each(table)(…) it is the second one.
            var open = m.Index + m.Length - 1;
            var close = Matching(code, open, '(', ')');
            if (open < 0 || close < 0)
            {
                continue;
            }
            var body = code[open..close];
            var name = TestName(source, code, open + 1);
            if (!Asserts().IsMatch(body))
            {
                violations.Add(new(path, name, NoAssertion));
            }
            foreach (Match c in Catch().Matches(body))
            {
                var braceOpen = body.IndexOf('{', c.Index);
                var braceClose = Matching(body, braceOpen, '{', '}');
                if (braceOpen < 0 || braceClose < 0)
                {
                    continue;
                }
                var block = body[braceOpen..braceClose];
                if (!Asserts().IsMatch(block) && !block.Contains("throw", StringComparison.Ordinal))
                {
                    violations.Add(new(path, name, SwallowsException));
                }
            }
        }
        return violations;
    }

    /// <summary>it.skip, test.only, describe.todo …</summary>
    [GeneratedRegex(@"(?<![\w.$])(?<fn>it|test|describe|suite)\s*\.\s*(?<mod>skip|only|todo)\b")]
    private static partial Regex Modified();

    /// <summary>xit(, fit(, xdescribe(, fdescribe(, xtest(</summary>
    [GeneratedRegex(@"(?<![\w.$])(?<fn>xit|fit|xdescribe|fdescribe|xtest)\s*\(")]
    private static partial Regex Prefixed();

    // A test: it(…) / test(…), it.skip(…) (a suspected bug still asserts), optionally it.each(table)(…). Not
    // describe: a suite holds tests, it is not one.
    [GeneratedRegex(@"(?<![\w.$])(?<fn>it|test)(?:\s*\.\s*skip)?(?:\s*\.\s*each\s*(?:\([^()]*\)|`[^`]*`))?\s*\(")]
    private static partial Regex TestCall();

    [GeneratedRegex(@"(?<![\w$])fs(?:\s*\.\s*promises)?\s*\.\s*(?:writeFile|writeFileSync|appendFile|appendFileSync|rm|rmSync|rmdir|rmdirSync|unlink|unlinkSync|rename|renameSync|copyFile|copyFileSync|truncate|truncateSync)\s*\(")]
    private static partial Regex FsWrite();

    /// <summary>Whether the suspected-bug marker is on the skip's line or the line before it.</summary>
    private static bool MarkedNear(string source, int index)
    {
        var lineStart = source.LastIndexOf('\n', Math.Max(0, index - 1)) + 1;
        var previousStart = lineStart > 0 ? source.LastIndexOf('\n', Math.Max(0, lineStart - 2)) + 1 : lineStart;
        var lineEnd = source.IndexOf('\n', index);
        var text = source[previousStart..(lineEnd < 0 ? source.Length : lineEnd)];
        return text.Contains(SuspectedBug.Marker, StringComparison.Ordinal);
    }

    /// <summary>The name of the last test opened before <paramref name="index"/>, for a violation found inside one.</summary>
    private static string EnclosingTest(string source, string code, int index)
    {
        var last = TestCall().Matches(code[..index]).LastOrDefault();
        return last is null ? "(outside a test)" : TestName(source, code, last.Index + last.Length);
    }

    [GeneratedRegex(@"\bexpect\s*[.(]|\bassert\w*\s*[.(]|\.rejects\b|\.resolves\b")]
    private static partial Regex Asserts();

    [GeneratedRegex(@"\bcatch\s*(?:\([^)]*\))?\s*\{")]
    private static partial Regex Catch();

    /// <summary>The first string argument after <paramref name="from"/>, read from the original source.</summary>
    private static string TestName(string source, string blanked, int from)
    {
        for (var i = from; i < blanked.Length && i < from + 200; i++)
        {
            if (source[i] is '\'' or '"' or '`' && blanked[i] == source[i])
            {
                var end = source.IndexOf(source[i], i + 1);
                if (end > i)
                {
                    return source[(i + 1)..end];
                }
            }
        }
        return "(unnamed test)";
    }

    /// <summary>The index just past the bracket that closes the one at <paramref name="open"/>, or -1.</summary>
    private static int Matching(string code, int open, char openChar, char closeChar)
    {
        if (open < 0)
        {
            return -1;
        }
        var depth = 0;
        for (var i = open; i < code.Length; i++)
        {
            if (code[i] == openChar)
            {
                depth++;
            }
            else if (code[i] == closeChar && --depth == 0)
            {
                return i + 1;
            }
        }
        return -1;
    }

    /// <summary>
    /// The source with the contents of comments, strings and template literals replaced by spaces (quotes kept),
    /// so nothing inside them is read as code. Positions are unchanged.
    /// </summary>
    internal static string Blank(string source)
    {
        var sb = new StringBuilder(source);
        var i = 0;
        while (i < sb.Length)
        {
            var c = sb[i];
            if (c == '/' && i + 1 < sb.Length && sb[i + 1] == '/')
            {
                while (i < sb.Length && sb[i] != '\n')
                {
                    sb[i++] = ' ';
                }
            }
            else if (c == '/' && i + 1 < sb.Length && sb[i + 1] == '*')
            {
                while (i < sb.Length && !(sb[i] == '*' && i + 1 < sb.Length && sb[i + 1] == '/'))
                {
                    if (sb[i] != '\n') sb[i] = ' ';
                    i++;
                }
                if (i < sb.Length) sb[i++] = ' ';
                if (i < sb.Length) sb[i++] = ' ';
            }
            else if (c is '\'' or '"' or '`')
            {
                i++;
                while (i < sb.Length && sb[i] != c)
                {
                    if (sb[i] == '\\' && i + 1 < sb.Length)
                    {
                        sb[i++] = ' ';
                    }
                    if (sb[i] != '\n') sb[i] = ' ';
                    i++;
                }
                i++;
            }
            else
            {
                i++;
            }
        }
        return sb.ToString();
    }
}
