using System.Text.Json;
using System.Text.RegularExpressions;
using Maf.Lab.TestGen;

namespace Maf.Lab.CoverageRunner;

/// <summary>
/// Holds the files a diff adds or changes to the lint CI applies (coverage-runner): `make lint` builds with
/// warnings as errors and runs ESLint and Prettier over the web app, so a candidate that passes here must pass there.
/// A finding fails the build and keeps what the run measured, so the next attempt sees it beside the test results.
/// Only the diff's files are held to it: a warning elsewhere is not the diff's doing, and a run without a diff (a
/// baseline, a refresh of main) is never checked at all.
/// </summary>
public static partial class LintBar
{
    /// <summary>What ESLint is given: the files its configuration lints (eslint.config.js).</summary>
    private static readonly string[] Scripts = [".ts", ".tsx", ".js", ".jsx", ".mjs", ".cjs"];

    private const int MaxFindings = 50;

    public static async Task<ToolchainOutcome> HoldAsync(string toolchain, string diff, string workspace, string outputDir,
        ToolchainOutcome outcome, TimeSpan timeLimit, CancellationToken ct)
    {
        var changed = ChangedFiles(diff);
        IReadOnlyList<string> findings;
        if (toolchain == "dotnet")
        {
            findings = DotnetFindings(outcome.Warnings ?? [], changed);
        }
        else
        {
            var web = changed.Where(f => f.StartsWith("web/", StringComparison.Ordinal)).Select(f => f["web/".Length..]).ToList();
            if (web.Count == 0)
            {
                return outcome;
            }
            (findings, var timedOut) = await WebFindingsAsync(Path.Combine(workspace, "web"), web, outputDir, timeLimit, ct);
            if (timedOut)
            {
                return outcome with { TimedOut = true };
            }
        }
        return Apply(outcome, findings);
    }

    /// <summary>A lint finding is a build failure; the test results and the coverage report stay.</summary>
    public static ToolchainOutcome Apply(ToolchainOutcome outcome, IReadOnlyList<string> findings) =>
        findings.Count == 0
            ? outcome
            : outcome with
            {
                Build = BuildOutcome.Failed,
                Diagnostics = [.. outcome.Diagnostics.Concat(findings).Distinct().Take(MaxFindings)],
            };

    /// <summary>The paths a unified diff adds or changes (not the ones it deletes), relative to the repository root.</summary>
    public static IReadOnlySet<string> ChangedFiles(string diff)
    {
        var files = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in diff.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (!line.StartsWith("+++ ", StringComparison.Ordinal))
            {
                continue;
            }
            var path = Unquote(line[4..].Trim());
            if (path.StartsWith("b/", StringComparison.Ordinal))
            {
                files.Add(path[2..]);
            }
        }
        return files;
    }

    /// <summary>The build warnings whose file is one the diff adds or changes.</summary>
    public static IReadOnlyList<string> DotnetFindings(IReadOnlyList<string> warnings, IReadOnlySet<string> changed) =>
        warnings.Where(w => WarningFile().Match(w) is { Success: true } m && changed.Contains(m.Groups[1].Value)).Take(MaxFindings).ToList();

    /// <summary>ESLint errors and Prettier differences in the changed web files (paths relative to web/).</summary>
    public static async Task<(IReadOnlyList<string> Findings, bool TimedOut)> WebFindingsAsync(string web, IReadOnlyList<string> files,
        string outputDir, TimeSpan timeLimit, CancellationToken ct)
    {
        if (timeLimit <= TimeSpan.Zero)
        {
            return ([], true);
        }
        var bin = Path.Combine(web, "node_modules", ".bin");
        var eslint = Path.Combine(bin, "eslint");
        var prettier = Path.Combine(bin, "prettier");
        if (!File.Exists(eslint) || !File.Exists(prettier))
        {
            return ([LintDiagnostics.CouldNotRun("eslint", "the web app's dependencies are not installed")], false);
        }
        var findings = new List<string>();

        var scripts = files.Where(f => Scripts.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase)).ToList();
        if (scripts.Count > 0)
        {
            var report = Path.Combine(outputDir, "eslint.json");
            var run = await ChildProcess.RunAsync(eslint, ["--format", "json", "--output-file", report, "--no-warn-ignored", "--", .. scripts],
                web, timeLimit, ct);
            if (run.TimedOut)
            {
                return ([], true);
            }
            // 0: clean, 1: errors found; anything else (2) is ESLint failing to run, as is a missing report.
            findings.AddRange(run.ExitCode is 0 or 1 && File.Exists(report)
                ? EslintFindings(await File.ReadAllTextAsync(report, ct), scripts)
                : [LintDiagnostics.CouldNotRun("eslint", FirstLine(run.Output, web))]);
        }

        var listed = await ChildProcess.RunAsync(prettier, ["--list-different", "--ignore-unknown", "--", .. files], web, timeLimit, ct);
        if (listed.TimedOut)
        {
            return ([], true);
        }
        if (listed.ExitCode is not (0 or 1))
        {
            findings.Add(LintDiagnostics.CouldNotRun("prettier", FirstLine(listed.Output, web)));
            return (findings, false);
        }
        var unformatted = listed.Output.Split('\n').Select(l => l.Trim()).Where(l => files.Contains(l, StringComparer.Ordinal)).ToList();
        if (unformatted.Count > 0)
        {
            // The agent cannot run Prettier, so it is told what Prettier would write. The workspace copy is formatted
            // to find out; the workspace is thrown away with the job.
            var before = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var file in unformatted)
            {
                before[file] = await File.ReadAllTextAsync(Path.Combine(web, file), ct);
            }
            var wrote = await ChildProcess.RunAsync(prettier, ["--write", "--log-level", "warn", "--", .. unformatted], web, timeLimit, ct);
            if (wrote.TimedOut)
            {
                return ([], true);
            }
            foreach (var file in unformatted)
            {
                var after = await File.ReadAllTextAsync(Path.Combine(web, file), ct);
                findings.Add(PrettierFinding("web/" + file, before[file], after));
            }
        }
        return (findings.Take(MaxFindings).ToList(), false);
    }

    /// <summary>
    /// ESLint's JSON report: errors only (severity 2), as `npm run lint` fails on errors only. ESLint names files by
    /// absolute path, possibly through a resolved link, so each is matched to the file it was given (relative to web/).
    /// </summary>
    public static IReadOnlyList<string> EslintFindings(string json, IReadOnlyList<string> files)
    {
        var findings = new List<string>();
        using var doc = JsonDocument.Parse(json);
        foreach (var file in doc.RootElement.EnumerateArray())
        {
            var reported = (file.GetProperty("filePath").GetString() ?? "").Replace('\\', '/');
            var path = "web/" + (files.FirstOrDefault(f => reported == f || reported.EndsWith("/" + f, StringComparison.Ordinal))
                ?? Path.GetFileName(reported));
            foreach (var message in file.GetProperty("messages").EnumerateArray())
            {
                if (message.TryGetProperty("severity", out var severity) && severity.GetInt32() == 2)
                {
                    findings.Add(LintDiagnostics.Eslint(path,
                        message.TryGetProperty("line", out var line) ? line.GetInt32() : 1,
                        message.TryGetProperty("column", out var column) ? column.GetInt32() : 1,
                        message.TryGetProperty("ruleId", out var rule) && rule.ValueKind == JsonValueKind.String ? rule.GetString() : null,
                        Clip(message.GetProperty("message").GetString() ?? "", 300)));
                }
            }
        }
        return findings;
    }

    /// <summary>Where a file and the way Prettier writes it first part, said so the agent can fix it by hand.</summary>
    public static string PrettierFinding(string path, string before, string after)
    {
        if (before.TrimEnd('\r', '\n') == after.TrimEnd('\r', '\n') && !before.Contains('\r'))
        {
            return LintDiagnostics.Prettier(path, null, "the file must end with exactly one newline.");
        }
        var was = before.Split('\n');
        var now = after.Split('\n');
        for (var i = 0; i < Math.Max(was.Length, now.Length); i++)
        {
            var old = i < was.Length ? was[i] : null;
            var @new = i < now.Length ? now[i] : null;
            if (old == @new)
            {
                continue;
            }
            if (old is not null && old.EndsWith('\r') && old[..^1] == @new)
            {
                return LintDiagnostics.Prettier(path, i + 1, "use \\n line endings, not \\r\\n.");
            }
            return @new is null
                ? LintDiagnostics.Prettier(path, i + 1, "Prettier ends the file before this line.")
                : LintDiagnostics.Prettier(path, i + 1, $"not formatted as Prettier formats it; Prettier writes this line as: {Clip(@new, 200)}");
        }
        return LintDiagnostics.Prettier(path, null, "not formatted as Prettier formats it.");
    }

    private static string FirstLine(string output, string web)
    {
        var line = output.Replace(web.TrimEnd('/') + "/", "", StringComparison.Ordinal).Split('\n')
            .Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0);
        return Clip(line ?? "it printed nothing", 300);
    }

    private static string Clip(string text, int max) => text.Length <= max ? text : text[..max] + "…";

    /// <summary>git quotes a path with unusual characters: "b/dir/na\"me".</summary>
    private static string Unquote(string path) =>
        path.Length >= 2 && path[0] == '"' && path[^1] == '"' ? path[1..^1].Replace("\\\"", "\"").Replace("\\\\", "\\") : path;

    [GeneratedRegex(@"^(.+?)\(\d+(?:,\d+)*\): warning ")]
    private static partial Regex WarningFile();
}
