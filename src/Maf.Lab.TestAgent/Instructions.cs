using System.Text;
using Maf.Lab.TestGen;

namespace Maf.Lab.TestAgent;

/// <summary>What the model is told: the rules once, and each attempt's task with what the last one taught.</summary>
public static class Instructions
{
    /// <summary>Told to the model when this many tool rounds of the attempt remain.</summary>
    public const int NudgeAtRoundsLeft = 3;

    public static string Nudge(int roundsLeft) =>
        $"{roundsLeft} tool rounds remain in this attempt. Write or improve a test file now; stop reading.";

    public const string System = """
        You write automated tests for one source file of the maf-lab repository, to raise its line coverage.

        Rules you must follow:
        - Only write test files. For dotnet: C# xUnit v3 tests under tests/Maf.Lab.Tests/ (namespace Maf.Lab.Tests).
          To stand in for an interface the code depends on, use NSubstitute, which the test project already references:
          var db = Substitute.For<IDatabase>(); db.HashGetAllAsync(key).Returns(entries); await db.Received(1).HashSetAsync(...).
          Never implement a large interface (such as StackExchange.Redis IConnectionMultiplexer or IDatabase) by hand,
          and never add a package: use only what tests/Maf.Lab.Tests already references.
          For vitest: *.test.ts or *.test.tsx next to the code under web/src/, using vitest and Testing Library.
        - Never change production code, and never write a test that changes it (no writing, moving or deleting files
          under src/ or web/src/).
        - Assert the behaviour the code is meant to have — what its names, documentation, specs and callers say —
          not whatever it happens to do.
        - Every test asserts something. Never skip or focus a test (.skip, .only, Skip=), and never make a test pass by
          catching the exception it should verify: use Assert.Throws / expect(...).toThrow.
        - If a test fails because the production code does not do what it is meant to, that is a suspected bug. Do not
          change the code and do not bend the assertion. Keep the test, skip it with the reason
          "suspected-bug: <short title>" ([Fact(Skip = "suspected-bug: <title>")] in C#; in TypeScript it.skip(...) with
          the comment // suspected-bug: <title> on the line before), and call report_suspected_bug, within the run's limit stated below.
        - Your files are held to the lint CI applies, and a finding fails the build. For dotnet, every compiler or
          analyzer warning in a file you write is an error (CI builds with -warnaserror): for example CA2022, so use
          stream.ReadExactly(...) rather than ignoring what stream.Read(...) returns. For vitest, a file you write must
          have no ESLint error (no unused variables or imports, no `any`) and must be formatted as Prettier formats it
          here: single quotes, semicolons, 2-space indent, trailing commas wherever allowed, lines up to 100 characters.
        - Read before you write: the target file, its callers, and the existing tests nearby, and follow their style.
        - Use run_tests to check your work; finish when the tests build, pass, and cover what you can.
        """;

    public static string Attempt(TestGenRequest request, int attempt, double? currentPct, string? feedback)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Target file: {request.TargetFile} ({request.Toolchain}).");
        sb.AppendLine($"Goal: at least {request.TargetLinePct}% line coverage. Now: {(currentPct is { } p ? $"{p:0.0}%" : "not measured")}.");
        sb.AppendLine($"This is attempt {attempt} of {request.MaxAttempts}.");
        sb.AppendLine($"You have {request.ToolRounds} tool rounds in this attempt. Read only what you need, and write a test file " +
            "within the first half of them: an attempt that ends without writing a test makes no progress.");
        sb.AppendLine(request.SuspectedBugLimit > 0
            ? $"This run may report at most {request.SuspectedBugLimit} suspected bug{(request.SuspectedBugLimit == 1 ? "" : "s")}."
            : "This run reports no suspected bugs: do not skip a failing test; fix the test or leave it out.");
        sb.AppendLine(request.TestRuns > 0
            ? $"You may call run_tests at most {request.TestRuns} time{(request.TestRuns == 1 ? "" : "s")} in this attempt."
            : "Do not call run_tests in this attempt: it is measured when you finish.");
        if (feedback is { Length: > 0 })
        {
            sb.AppendLine();
            sb.AppendLine("What the last attempt's measured run found:");
            sb.AppendLine(feedback);
        }
        sb.AppendLine();
        sb.AppendLine("Write or improve the tests now, then finish with one sentence saying what you changed.");
        return sb.ToString();
    }

    /// <summary>What the next attempt needs to know from this one: errors, failures, violations, what is still uncovered.</summary>
    /// <param name="wroteNothing">The attempt number, when that attempt added or changed no test file.</param>
    public static string Feedback(RunnerResult result, IReadOnlyList<GuardrailViolation> violations, int? wroteNothing = null)
    {
        var sb = new StringBuilder();
        if (wroteNothing is { } idle)
        {
            sb.AppendLine($"- Attempt {idle} wrote no test file. Reading without writing is not progress: write a test in this attempt.");
        }
        if (result.Status == RunnerStatus.DiffRejected)
        {
            sb.AppendLine("- Your changes could not be applied to the commit.");
        }
        if (result.Build == BuildOutcome.Failed)
        {
            sb.AppendLine("- The build failed:");
            foreach (var d in result.Diagnostics.Take(20))
            {
                sb.AppendLine($"  {d}");
            }
            if (LintNote(result) is { } note)
            {
                sb.AppendLine($"- {note}");
            }
        }
        foreach (var f in result.Failures.Take(10))
        {
            sb.AppendLine($"- Failing test {f.Name}: {f.Message}");
        }
        foreach (var v in violations)
        {
            sb.AppendLine($"- Rule broken: {v}");
        }
        if (result.Uncovered.Count > 0)
        {
            sb.AppendLine($"- Lines still uncovered: {string.Join(", ", result.Uncovered.Take(40).Select(r => r[0] == r[1] ? $"{r[0]}" : $"{r[0]}-{r[1]}"))}");
        }
        return sb.ToString().TrimEnd();
    }

    public const string LintFailsTheBuild =
        "Warnings, ESLint errors and Prettier differences in your files fail the build here, as they fail CI: fix each one listed.";

    /// <summary>Said when a run's diagnostics include lint findings, which a model may otherwise take for advice.</summary>
    public static string? LintNote(RunnerResult result) =>
        result.Build == BuildOutcome.Failed && result.Diagnostics.Any(LintDiagnostics.Is) ? LintFailsTheBuild : null;
}
