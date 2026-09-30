using System.Diagnostics;
using System.Text;

namespace Maf.Lab.TestGen;

/// <summary>What a git command returned. Output is kept as bytes: a file's content is not necessarily UTF-8.</summary>
public sealed record GitResult(int ExitCode, byte[] Stdout, string Stderr)
{
    public bool Ok => ExitCode == 0;
    public string Text => Encoding.UTF8.GetString(Stdout);
}

/// <summary>A git command that did not succeed. The message names the subcommand only, never paths or output.</summary>
public sealed class GitException(string subcommand, int exitCode, string stderr)
    : Exception($"git {subcommand} failed with exit code {exitCode}")
{
    public string Subcommand { get; } = subcommand;
    public int ExitCode { get; } = exitCode;
    /// <summary>For logs at debug level and for tests; never shown to a user or a model.</summary>
    public string Stderr { get; } = stderr;
}

/// <summary>
/// Runs git as a child process: arguments as a list (never through a shell), no terminal prompt, no pager, and a
/// time limit. Every git call in the api, the agent and the runner goes through here.
/// </summary>
public static class Git
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(2);

    public static async Task<GitResult> RunAsync(string workingDirectory, IEnumerable<string> args, CancellationToken ct,
        byte[]? stdin = null, TimeSpan? timeout = null, IReadOnlyDictionary<string, string>? environment = null)
    {
        var info = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in args)
        {
            info.ArgumentList.Add(arg);
        }
        info.Environment["GIT_TERMINAL_PROMPT"] = "0";
        info.Environment["GIT_PAGER"] = "cat";
        info.Environment["LC_ALL"] = "C";
        if (environment is not null)
        {
            foreach (var (key, value) in environment)
            {
                info.Environment[key] = value;
            }
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout ?? DefaultTimeout);
        using var process = Process.Start(info) ?? throw new GitException(info.ArgumentList.FirstOrDefault() ?? "", -1, "not started");
        try
        {
            var stdout = new MemoryStream();
            var copyOut = process.StandardOutput.BaseStream.CopyToAsync(stdout, cts.Token);
            var readErr = process.StandardError.ReadToEndAsync(cts.Token);
            if (stdin is not null)
            {
                await process.StandardInput.BaseStream.WriteAsync(stdin, cts.Token);
            }
            process.StandardInput.Close();
            await Task.WhenAll(copyOut, readErr);
            await process.WaitForExitAsync(cts.Token);
            return new GitResult(process.ExitCode, stdout.ToArray(), await readErr);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // Already gone.
            }
            throw;
        }
    }

    /// <summary>Runs git and returns its output, throwing <see cref="GitException"/> when it fails.</summary>
    public static async Task<GitResult> CheckedAsync(string workingDirectory, IReadOnlyList<string> args, CancellationToken ct,
        byte[]? stdin = null, TimeSpan? timeout = null, IReadOnlyDictionary<string, string>? environment = null)
    {
        var result = await RunAsync(workingDirectory, args, ct, stdin, timeout, environment);
        return result.Ok ? result : throw new GitException(args.FirstOrDefault(a => !a.StartsWith('-')) ?? "", result.ExitCode, result.Stderr);
    }

    /// <summary>Whether a string is a full or abbreviated commit id (hex, 7 to 40 characters), so it is never an option or a path.</summary>
    public static bool IsCommitId(string? value) =>
        value is { Length: >= 7 and <= 40 } && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
}
