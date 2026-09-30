using Maf.Lab.TestGen;

namespace Maf.Lab.Tests;

/// <summary>
/// Where the test agent may write, and what a generated test may not be (test-generation-agent: tools with a
/// test-only write allowlist, test guardrails, suspected bugs).
/// </summary>
public sealed class TestGenSafetyTests
{
    private static readonly string Root = CreateWorkspace();

    private static string CreateWorkspace()
    {
        var root = Directory.CreateTempSubdirectory("maf-ws-").FullName;
        Directory.CreateDirectory(Path.Combine(root, "src", "Lab"));
        Directory.CreateDirectory(Path.Combine(root, "tests", "Lab.Tests"));
        File.WriteAllText(Path.Combine(root, "src", "Lab", "Calc.cs"), "class Calc {}");
        // A link inside the tests folder that points at production code.
        Directory.CreateSymbolicLink(Path.Combine(root, "tests", "linked"), Path.Combine(root, "src"));
        return root;
    }

    [Theory]
    [InlineData("tests/Lab.Tests/CalcTests.cs", "dotnet")]
    [InlineData("web/src/coverage/format.test.ts", "vitest")]
    [InlineData("web/src/coverage/Page.test.tsx", "vitest")]
    [InlineData("web/src/test/helpers.ts", "vitest")]
    public void Test_locations_are_writable(string path, string toolchain) =>
        Assert.StartsWith(Root, WorkspacePaths.Resolve(Root, path, PathAccess.Write, toolchain));

    [Theory]
    [InlineData("src/Lab/Calc.cs", "dotnet")]
    [InlineData("tests/../src/Lab/Calc.cs", "dotnet")]
    [InlineData("/etc/passwd", "dotnet")]
    [InlineData("tests\\Lab.Tests\\X.cs", "dotnet")]
    [InlineData("tests/Lab.Tests/bin/X.cs", "dotnet")]
    [InlineData("tests/Lab.Tests/CalcTests.cs", "vitest")]
    [InlineData("web/src/coverage/format.ts", "vitest")]
    [InlineData("web/src/node_modules/x.test.ts", "vitest")]
    [InlineData("C:/tests/X.cs", "dotnet")]
    public void Production_code_and_escapes_are_refused(string path, string toolchain)
    {
        var refusal = Assert.Throws<PathRefusedException>(() => WorkspacePaths.Resolve(Root, path, PathAccess.Write, toolchain));
        Assert.DoesNotContain(Root, refusal.Message);
    }

    [Fact]
    public void A_write_to_production_code_is_told_why()
    {
        var refusal = Assert.Throws<PathRefusedException>(() =>
            WorkspacePaths.Resolve(Root, "src/Lab/Calc.cs", PathAccess.Write, "dotnet"));

        Assert.Equal(WorkspacePaths.WriteRefusal, refusal.Message);
    }

    [Fact]
    public void A_link_cannot_carry_a_write_out_of_the_tests()
    {
        Assert.Throws<PathRefusedException>(() => WorkspacePaths.Resolve(Root, "tests/linked/Lab/Evil.cs", PathAccess.Write, "dotnet"));
    }

    [Theory]
    [InlineData(".git/config")]
    [InlineData("src/Lab/.env")]
    [InlineData("src/Lab/appsettings.Development.json")]
    public void Secrets_and_git_internals_are_not_readable(string path) =>
        Assert.Throws<PathRefusedException>(() => WorkspacePaths.Resolve(Root, path, PathAccess.Read, "dotnet"));

    [Fact]
    public void Production_code_is_readable() =>
        Assert.EndsWith("Calc.cs", WorkspacePaths.Resolve(Root, "src/Lab/Calc.cs", PathAccess.Read, "dotnet"));

    [Fact]
    public void A_diff_is_checked_on_both_sides_of_a_rename()
    {
        const string diff = """
            diff --git a/tests/Lab.Tests/A.cs b/src/Lab/A.cs
            similarity index 100%
            rename from tests/Lab.Tests/A.cs
            rename to src/Lab/A.cs
            diff --git a/tests/Lab.Tests/B.cs b/tests/Lab.Tests/B.cs
            new file mode 100644
            --- /dev/null
            +++ b/tests/Lab.Tests/B.cs
            @@ -0,0 +1 @@
            +class B {}
            """;

        Assert.Equal(["src/Lab/A.cs"], DiffPaths.Forbidden(diff, "dotnet"));
        Assert.Contains("tests/Lab.Tests/B.cs", DiffPaths.Of(diff));
    }

    [Fact]
    public void A_binary_patch_is_refused() =>
        Assert.Contains("(binary patch)", DiffPaths.Forbidden("diff --git a/tests/x.bin b/tests/x.bin\nGIT binary patch\nliteral 3\n", "dotnet"));

    // ---- guardrails: C# ----

    private static IReadOnlyList<GuardrailViolation> Cs(string body, IReadOnlyList<SuspectedBug>? bugs = null) =>
        TestGuardrails.Check(new Dictionary<string, string> { ["tests/Lab.Tests/CalcTests.cs"] = $$"""
            using Xunit;
            public class CalcTests
            {
            {{body}}
            }
            """ }, bugs);

    [Fact]
    public void A_clean_xunit_test_passes()
    {
        Assert.Empty(Cs("""
            [Fact] public void Adds() { Assert.Equal(2, Calc.Add(1, 1)); }
            [Theory, InlineData(1)] public void Throws(int x) => Assert.Throws<ArgumentException>(() => Calc.Fail(x));
            [Fact] public void Helper() { AssertAdds(1, 1); }
            [Fact] public void Rethrows() { try { Calc.Run(); } catch (IOException) { throw; } Assert.True(true); }
            """));
    }

    [Fact]
    public void A_skipped_xunit_test_is_a_violation()
    {
        var v = Cs("""[Fact(Skip = "flaky")] public void Adds() { Assert.Equal(2, Calc.Add(1, 1)); }""");

        Assert.Equal([("CalcTests.Adds", TestGuardrails.Skipped)], v.Select(x => (x.Test, x.Rule)));
    }

    [Fact]
    public void An_assertion_free_xunit_test_is_a_violation()
    {
        var v = Cs("""[Fact] public void Adds() { Calc.Add(1, 1); }""");

        Assert.Equal([TestGuardrails.NoAssertion], v.Select(x => x.Rule));
    }

    [Fact]
    public void Swallowing_the_expected_exception_is_a_violation()
    {
        var v = Cs("""[Fact] public void Fails() { try { Calc.Fail(1); } catch (ArgumentException) { } Assert.True(true); }""");

        Assert.Equal([TestGuardrails.SwallowsException], v.Select(x => x.Rule));
    }

    [Fact]
    public void Writing_to_production_code_is_a_violation()
    {
        var v = Cs("""[Fact] public void Patches() { File.WriteAllText("src/Lab/Calc.cs", "class Calc {}"); Assert.True(true); }""");

        Assert.Equal([TestGuardrails.ModifiesProduction], v.Select(x => x.Rule));
    }

    [Fact]
    public void Writing_a_fixture_under_a_temp_folder_is_not()
    {
        Assert.Empty(Cs("""
            [Fact] public void Fixture() { File.WriteAllText(Path.Combine(Path.GetTempPath(), "src", "a.cs"), ""); Assert.True(true); }
            """));
    }

    private static SuspectedBug Bug(string file, string test) =>
        new(file, test, "Pct of an empty file is 0", "Pct(0, 0) should be 100", "100", "0", "Assert.Equal() Failure");

    [Fact]
    public void A_reported_suspected_bug_may_be_skipped()
    {
        var v = Cs("""
            [Fact(Skip = "suspected-bug: Pct of an empty file is 0")] public void EmptyIsFull() { Assert.Equal(100, Calc.Pct(0, 0)); }
            """, [Bug("tests/Lab.Tests/CalcTests.cs", "EmptyIsFull")]);

        Assert.Empty(v);
    }

    [Fact]
    public void A_suspected_bug_skip_that_was_not_reported_is_a_violation()
    {
        var v = Cs("""
            [Fact(Skip = "suspected-bug: Pct of an empty file is 0")] public void EmptyIsFull() { Assert.Equal(100, Calc.Pct(0, 0)); }
            """);

        Assert.Equal([TestGuardrails.Skipped], v.Select(x => x.Rule));
    }

    [Fact]
    public void A_reported_bug_skipped_without_the_marker_is_a_violation()
    {
        var v = Cs("""[Fact(Skip = "broken")] public void EmptyIsFull() { Assert.Equal(100, Calc.Pct(0, 0)); }""",
            [Bug("tests/Lab.Tests/CalcTests.cs", "EmptyIsFull")]);

        Assert.Equal([TestGuardrails.Skipped], v.Select(x => x.Rule));
    }

    [Fact]
    public void A_suspected_bug_still_needs_an_assertion()
    {
        var v = Cs("""[Fact(Skip = "suspected-bug: x")] public void EmptyIsFull() { Calc.Pct(0, 0); }""",
            [Bug("tests/Lab.Tests/CalcTests.cs", "EmptyIsFull")]);

        Assert.Equal([TestGuardrails.NoAssertion], v.Select(x => x.Rule));
    }

    [Fact]
    public void A_fourth_suspected_bug_is_a_violation()
    {
        var bugs = Enumerable.Range(1, 4).Select(i => Bug("tests/Lab.Tests/CalcTests.cs", $"Bug{i}")).ToList();
        var body = string.Join('\n', bugs.Select(b => $$"""[Fact(Skip = "suspected-bug: x")] public void {{b.Test}}() { Assert.True(false); }"""));

        var v = Cs(body, bugs);

        // The fourth is both one bug too many and a skip nobody may make.
        Assert.Contains(v, x => x.Test == "Bug4" && x.Rule == TestGuardrails.TooManyBugs);
        Assert.Contains(v, x => x.Test == "CalcTests.Bug4" && x.Rule == TestGuardrails.Skipped);
        Assert.DoesNotContain(v, x => x.Test.EndsWith("Bug1"));
    }

    // ---- guardrails: TypeScript ----

    private static IReadOnlyList<GuardrailViolation> Ts(string source, IReadOnlyList<SuspectedBug>? bugs = null) =>
        TestGuardrails.Check(new Dictionary<string, string> { ["web/src/coverage/format.test.ts"] = source }, bugs);

    [Fact]
    public void A_clean_vitest_file_passes()
    {
        Assert.Empty(Ts("""
            import { describe, expect, it } from 'vitest';
            // it.only('commented out', () => {});
            describe('format', () => {
              it('formats a percentage', () => {
                expect(pct(1)).toBe('1.0%');
              });
              it.each([[1, '1.0%'], [2, '2.0%']])('formats %s', (v, s) => {
                expect(pct(v)).toBe(s);
              });
              test('rejects', async () => {
                await expect(load()).rejects.toThrow();
              });
              it('says "it.skip(" in a string', () => { expect('it.skip(').toBeTruthy(); });
            });
            """));
    }

    [Theory]
    [InlineData("it.only('x', () => { expect(1).toBe(1); });", TestGuardrails.Focused)]
    [InlineData("fit('x', () => { expect(1).toBe(1); });", TestGuardrails.Focused)]
    [InlineData("describe.only('x', () => { it('y', () => { expect(1).toBe(1); }); });", TestGuardrails.Focused)]
    [InlineData("it.skip('x', () => { expect(1).toBe(1); });", TestGuardrails.Skipped)]
    [InlineData("xit('x', () => { expect(1).toBe(1); });", TestGuardrails.Skipped)]
    [InlineData("it('x', () => { pct(1); });", TestGuardrails.NoAssertion)]
    [InlineData("it('x', () => { try { load(); } catch (e) { } expect(1).toBe(1); });", TestGuardrails.SwallowsException)]
    [InlineData("it('x', () => { fs.writeFileSync('web/src/coverage/format.ts', ''); expect(1).toBe(1); });", TestGuardrails.ModifiesProduction)]
    public void A_broken_vitest_test_is_a_violation(string source, string rule)
    {
        Assert.Contains(rule, Ts(source).Select(v => v.Rule));
    }

    [Fact]
    public void A_reported_vitest_suspected_bug_may_be_skipped()
    {
        const string source = """
            // suspected-bug: pct rounds half down
            it.skip('rounds half up', () => {
              expect(pct(0.05)).toBe('0.1%');
            });
            """;

        Assert.Empty(Ts(source, [Bug("web/src/coverage/format.test.ts", "rounds half up")]));
        Assert.Equal([TestGuardrails.Skipped], Ts(source).Select(v => v.Rule));
    }

    [Fact]
    public void The_labs_own_tests_pass_the_guardrails()
    {
        var root = RepoRoot();
        var files = Directory.EnumerateFiles(Path.Combine(root, "tests"), "*.cs", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(Path.Combine(root, "web", "src"), "*.test.ts*", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .ToDictionary(f => Path.GetRelativePath(root, f).Replace('\\', '/'), File.ReadAllText);

        Assert.NotEmpty(files);
        Assert.Empty(TestGuardrails.Check(files).Select(v => v.ToString()));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "maf-lab.sln")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("repository root not found");
    }
}
