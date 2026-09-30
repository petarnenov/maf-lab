using System.Diagnostics;
using System.Text;

namespace Maf.Lab.CoverageRunner;

/// <summary>What a child process printed, capped, and how it ended.</summary>
public sealed record ProcessOutcome(int ExitCode, string Output, bool TimedOut);

/// <summary>
/// Runs a build or test tool as a child process with a clean environment: only the variables a toolchain needs, so
/// model-written code finds no signing key or other secret to read, print and carry out through a failure message.
/// </summary>
public static class ChildProcess
{
    /// <summary>The variables passed through. Everything else in this process's environment stays here.</summary>
    public static readonly string[] Passed =
    [
        "PATH", "HOME", "USER", "LANG", "LC_ALL", "TMPDIR", "TEMP", "TMP", "DOTNET_ROOT", "NUGET_PACKAGES",
        "NUGET_FALLBACK_PACKAGES", "npm_config_cache", "NODE_PATH",
    ];

    public const int MaxOutputChars = 400_000;

    public static async Task<ProcessOutcome> RunAsync(string file, IEnumerable<string> args, string workingDirectory, TimeSpan timeout,
        CancellationToken ct, IReadOnlyDictionary<string, string>? extra = null)
    {
        var info = new ProcessStartInfo(file)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
        };
        foreach (var arg in args)
        {
            info.ArgumentList.Add(arg);
        }
        info.Environment.Clear();
        foreach (var name in Passed)
        {
            if (Environment.GetEnvironmentVariable(name) is { } value)
            {
                info.Environment[name] = value;
            }
        }
        info.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        info.Environment["DOTNET_NOLOGO"] = "1";
        info.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";
        info.Environment["CI"] = "true";
        info.Environment["NO_COLOR"] = "1";
        foreach (var (key, value) in extra ?? new Dictionary<string, string>())
        {
            info.Environment[key] = value;
        }

        using var process = Process.Start(info) ?? throw new InvalidOperationException($"{file} did not start");
        process.StandardInput.Close();
        var output = new StringBuilder();
        void Collect(object _, DataReceivedEventArgs e)
        {
            if (e.Data is null)
            {
                return;
            }
            lock (output)
            {
                if (output.Length < MaxOutputChars)
                {
                    output.AppendLine(e.Data);
                }
            }
        }
        process.OutputDataReceived += Collect;
        process.ErrorDataReceived += Collect;
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(timeout);
        var timedOut = false;
        try
        {
            await process.WaitForExitAsync(limit.Token);
        }
        catch (OperationCanceledException)
        {
            timedOut = !ct.IsCancellationRequested;
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }
            if (!timedOut)
            {
                throw;
            }
        }
        // Let the output handlers drain what the process wrote before it ended.
        process.WaitForExit(2_000);
        lock (output)
        {
            return new ProcessOutcome(timedOut ? -1 : process.ExitCode, output.ToString(), timedOut);
        }
    }
}
