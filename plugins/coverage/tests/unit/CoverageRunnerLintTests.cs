extern alias service;
using Maf.Lab.Plugins.Coverage;
using service::Maf.Lab.CoverageRunner;
using Maf.Lab.TestGen;

namespace Maf.Lab.Tests;

/// <summary>The lint bar the runner holds a diff's files to (coverage-runner: held to the lint bar CI applies).</summary>
public sealed partial class CoverageRunnerTests
{
    /// <summary>The line `dotnet test` prints for CA2022, as captured from a real build, after the workspace prefix.</summary>
    private const string Ca2022 =
        "tests/Lab.Tests/CalcTests.cs(9,9): warning CA2022: Avoid inexact read with 'System.IO.Stream.Read(byte[], int, int)' " +
        "(https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca2022)";

    private static readonly ToolchainOutcome Built =
        new(BuildOutcome.Ok, [], new TestCounts(3, 0, 0), [], "/out/r.xml", false);

    [Fact]
    public async Task A_test_that_triggers_CA2022_is_a_build_failure_with_that_diagnostic()
    {
        var repo = await RepoAsync();
        var toolchain = new FakeToolchain { Warnings = [Ca2022] };
        await using var runner = new Runner(repo, toolchain);

        var result = await runner.RunAsync(new RunnerRequest(await repo.HeadAsync(Ct), "dotnet", AddTest("CalcTests"), "src/Lab/Calc.cs"));

        Assert.Equal((RunnerStatus.Ok, BuildOutcome.Failed), (result.Status, result.Build));
        Assert.Equal([Ca2022], result.Diagnostics);
        Assert.False(result.Green);
        Assert.True(LintDiagnostics.OnlyLint(result));
        // What the run measured stays, so the next attempt sees it beside the warning.
        Assert.Equal(2, result.Tests.Passed);
        Assert.Equal(50.0, result.TargetPct);
        Assert.Equal([[3, 4]], result.Uncovered);
    }

    [Fact]
    public async Task A_warning_outside_the_diff_does_not_fail_the_build()
    {
        var repo = await RepoAsync();
        var elsewhere = "tests/Lab.Tests/Existing.cs(1,1): warning CS0168: The variable 'x' is declared but never used";
        var toolchain = new FakeToolchain { Warnings = [elsewhere, "CSC : warning CS2008: No source files specified."] };
        await using var runner = new Runner(repo, toolchain);

        var result = await runner.RunAsync(new RunnerRequest(await repo.HeadAsync(Ct), "dotnet", AddTest("CalcTests"), "src/Lab/Calc.cs"));

        Assert.True(result.Green);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public async Task A_run_without_a_diff_is_measured_whatever_it_warns()
    {
        var repo = await RepoAsync();
        var toolchain = new FakeToolchain { Warnings = ["tests/Lab.Tests/Existing.cs(1,1): warning CA2022: Avoid inexact read"] };
        await using var runner = new Runner(repo, toolchain);

        var result = await runner.RunAsync(new RunnerRequest(await repo.HeadAsync(Ct), "dotnet", null, "src/Lab/Calc.cs"));

        Assert.True(result.Green);
        Assert.Equal(25.0, result.TargetPct);
    }

    [Fact]
    public void Dotnet_output_gives_its_warnings_and_mixed_case_analyzer_errors()
    {
        const string output = """
            /work/job/ws/tests/Lab.Tests/CalcTests.cs(9,9): warning CA2022: Avoid inexact read with 'System.IO.Stream.Read(byte[], int, int)' (https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca2022) [/work/job/ws/tests/Lab.Tests/Lab.Tests.csproj]
            Test run summary: Passed!
              total: 3
              failed: 0
              succeeded: 3
              skipped: 0
            """;

        var built = DotnetToolchain.Parse(new ProcessOutcome(0, output, false), "/work/job/ws", "/out/r.xml");

        Assert.Equal(BuildOutcome.Ok, built.Build);
        Assert.Empty(built.Diagnostics);
        Assert.Equal([Ca2022], built.Warnings);

        const string analyzer = """
            /work/job/ws/tests/Lab.Tests/CalcTests.cs(7,13): error xUnit1031: Do not use blocking task operations in test method. [/work/job/ws/tests/Lab.Tests/Lab.Tests.csproj]
            Build failed with exit code: 1.
            """;
        var failed = DotnetToolchain.Parse(new ProcessOutcome(1, analyzer, false), "/work/job/ws", "/out/r.xml");
        Assert.Equal(["tests/Lab.Tests/CalcTests.cs(7,13): error xUnit1031: Do not use blocking task operations in test method."], failed.Diagnostics);
    }

    [Fact]
    public void A_diff_names_the_files_it_adds_or_changes_not_those_it_deletes()
    {
        const string diff = """
            diff --git a/tests/Lab.Tests/New.cs b/tests/Lab.Tests/New.cs
            new file mode 100644
            --- /dev/null
            +++ b/tests/Lab.Tests/New.cs
            @@ -0,0 +1 @@
            +class New {}
            diff --git a/web/src/a.test.ts b/web/src/a.test.ts
            --- a/web/src/a.test.ts
            +++ b/web/src/a.test.ts
            @@ -1 +1 @@
            -x
            +y
            diff --git a/tests/Lab.Tests/Old.cs b/tests/Lab.Tests/Old.cs
            deleted file mode 100644
            --- a/tests/Lab.Tests/Old.cs
            +++ /dev/null
            @@ -1 +0,0 @@
            -class Old {}
            diff --git "a/web/src/we\"ird.test.ts" "b/web/src/we\"ird.test.ts"
            --- "a/web/src/we\"ird.test.ts"
            +++ "b/web/src/we\"ird.test.ts"
            """;

        Assert.Equal(["tests/Lab.Tests/New.cs", "web/src/a.test.ts", "web/src/we\"ird.test.ts"], LintBar.ChangedFiles(diff).Order());
    }

    [Fact]
    public void Lint_diagnostics_are_told_apart_from_compiler_errors()
    {
        Assert.True(LintDiagnostics.Is(Ca2022));
        Assert.True(LintDiagnostics.Is(LintDiagnostics.Eslint("web/src/a.test.ts", 4, 9, "no-unused-vars", "'x' is unused.")));
        Assert.True(LintDiagnostics.Is(LintDiagnostics.Prettier("web/src/a.test.ts", 1, "not formatted")));
        Assert.True(LintDiagnostics.Is(LintDiagnostics.CouldNotRun("eslint", "exit 2")));
        Assert.False(LintDiagnostics.Is("tests/Lab.Tests/CalcTests.cs(4,51): error CS1002: ; expected"));
        Assert.False(LintDiagnostics.Is("web/src/b.test.ts: Transform failed: Unexpected token"));

        var mixed = FakeCoverageRunner.Result("<coverage/>", build: BuildOutcome.Failed) with
        {
            Diagnostics = ["tests/Lab.Tests/CalcTests.cs(4,51): error CS1002: ; expected", Ca2022],
        };
        Assert.False(LintDiagnostics.OnlyLint(mixed));
        Assert.True(LintDiagnostics.OnlyLint(mixed with { Diagnostics = [Ca2022] }));
    }

    [Fact]
    public void ESLint_errors_fail_and_its_warnings_do_not()
    {
        const string json = """
            [{"filePath":"/private/var/ws/web/src/a.test.ts","messages":[
               {"ruleId":"@typescript-eslint/no-unused-vars","severity":2,"message":"'unused' is assigned a value but never used.","line":4,"column":9},
               {"ruleId":"react-refresh/only-export-components","severity":1,"message":"Fast refresh only works when a file only exports components.","line":1,"column":1}]},
             {"filePath":"/private/var/ws/web/src/b.test.ts","messages":[{"fatal":true,"severity":2,"message":"Parsing error: ',' expected.","line":2,"column":7}]}]
            """;

        var findings = LintBar.EslintFindings(json, ["src/a.test.ts", "src/b.test.ts"]);

        Assert.Equal([
            "web/src/a.test.ts(4,9): eslint @typescript-eslint/no-unused-vars: 'unused' is assigned a value but never used.",
            "web/src/b.test.ts(2,7): eslint error: Parsing error: ',' expected.",
        ], findings);
    }

    [Fact]
    public void A_Prettier_difference_names_the_first_line_and_how_Prettier_writes_it()
    {
        Assert.Equal("web/src/a.test.ts(2): prettier: not formatted as Prettier formats it; Prettier writes this line as: it('adds', () => {",
            LintBar.PrettierFinding("web/src/a.test.ts", "import x from 'y';\nit(\"adds\", () => {\n", "import x from 'y';\nit('adds', () => {\n"));
        Assert.Equal("web/src/a.test.ts: prettier: the file must end with exactly one newline.",
            LintBar.PrettierFinding("web/src/a.test.ts", "x;", "x;\n"));
        Assert.Contains("use \\n line endings", LintBar.PrettierFinding("web/src/a.test.ts", "x;\r\n", "x;\n"));
    }

    [Fact]
    public async Task Without_the_lint_tools_a_web_diff_does_not_pass()
    {
        var tempWorkspace = Directory.CreateTempSubdirectory("maf-lint-").FullName;
        Directory.CreateDirectory(Path.Combine(tempWorkspace, "web", "src"));
        const string diff = "--- /dev/null\n+++ b/web/src/a.test.ts\n@@ -0,0 +1 @@\n+it('x', () => {});\n";

        var held = await LintBar.HoldAsync("vitest", diff, tempWorkspace, tempWorkspace, Built, TimeSpan.FromMinutes(1), Ct);

        Assert.Equal(BuildOutcome.Failed, held.Build);
        Assert.Contains("could not run", Assert.Single(held.Diagnostics));
        Assert.Equal(Built.Tests, held.Tests);
        // A diff that changes no web file has nothing for ESLint or Prettier to look at.
        Assert.Same(Built, await LintBar.HoldAsync("vitest", AddTest("X"), tempWorkspace, tempWorkspace, Built, TimeSpan.FromMinutes(1), Ct));
    }

    [Fact]
    public async Task The_repositorys_ESLint_and_Prettier_hold_a_changed_web_test()
    {
        var repoWeb = Path.Combine(CorpusLoaderTests.RepoRoot(), "web");
        var modules = Path.Combine(repoWeb, "node_modules");
        Assert.SkipUnless(File.Exists(Path.Combine(modules, ".bin", "eslint")), "the web app's dependencies are not installed (npm ci in web/)");
        var tempWeb = Path.Combine(Directory.CreateTempSubdirectory("maf-lint-").FullName, "web");
        Directory.CreateDirectory(Path.Combine(tempWeb, "src"));
        foreach (var file in new[] { "package.json", "eslint.config.js", ".prettierrc.json", ".prettierignore" })
        {
            File.Copy(Path.Combine(repoWeb, file), Path.Combine(tempWeb, file));
        }
        Directory.CreateSymbolicLink(Path.Combine(tempWeb, "node_modules"), modules);
        await File.WriteAllTextAsync(Path.Combine(tempWeb, "src", "bad.test.ts"),
            "import { expect, it } from \"vitest\";\n\nit('adds', () => {\n  const unused = 1;\n  expect(1 + 1).toBe(2);\n});\n", Ct);
        await File.WriteAllTextAsync(Path.Combine(tempWeb, "src", "good.test.ts"),
            "import { expect, it } from 'vitest';\n\nit('adds', () => {\n  expect(1 + 1).toBe(2);\n});\n", Ct);
        var output = Directory.CreateTempSubdirectory("maf-lint-out-").FullName;

        var (findings, timedOut) = await LintBar.WebFindingsAsync(tempWeb, ["src/bad.test.ts", "src/good.test.ts"], output, TimeSpan.FromMinutes(2), Ct);

        Assert.False(timedOut);
        Assert.Equal([
            "web/src/bad.test.ts(4,9): eslint @typescript-eslint/no-unused-vars: 'unused' is assigned a value but never used.",
            "web/src/bad.test.ts(1): prettier: not formatted as Prettier formats it; Prettier writes this line as: import { expect, it } from 'vitest';",
        ], findings);
        Assert.All(findings, f => Assert.True(LintDiagnostics.Is(f)));
    }
}
