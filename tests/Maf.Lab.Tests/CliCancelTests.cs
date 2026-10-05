using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace Maf.Lab.Tests;

/// <summary>
/// The CLI tools stop when told to (stop-anything): each is started for real, from its own build output, against a
/// service that accepts and never answers, so it is caught waiting, then signalled. It ends with exit code 130 and a
/// last line that says it was cancelled and what to do next.
/// <para>
/// Each tool is signalled with SIGTERM and with SIGINT (Ctrl+C), which a .NET tool catches on two different lines. A
/// process started by a non-interactive parent inherits SIGINT as ignored (POSIX), and .NET keeps it so; a terminal's
/// Ctrl+C reaches its job with the default disposition. So for SIGINT the tool is started through a launcher that sets
/// SIGINT back to its default and then execs the tool under the same pid, as a terminal would have it.
/// </para>
/// </summary>
[Collection("TestGeneration")]
public sealed class CliCancelTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Accepts connections and never says a word: whatever talks to it waits.</summary>
    private sealed class BlackHole : IDisposable
    {
        private readonly TcpListener listener = new(IPAddress.Loopback, 0);
        private readonly List<TcpClient> held = [];

        public BlackHole()
        {
            listener.Start();
            _ = AcceptAsync();
        }

        public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;

        private async Task AcceptAsync()
        {
            try
            {
                while (true)
                {
                    held.Add(await listener.AcceptTcpClientAsync());
                }
            }
            catch (Exception ex) when (ex is ObjectDisposedException or SocketException)
            {
                // Stopped with the test.
            }
        }

        public void Dispose()
        {
            listener.Stop();
            held.ForEach(c => c.Dispose());
        }
    }

    /// <summary>A tool's own build output (its dependencies are its own, not this test project's).</summary>
    private static string ToolPath(string projectDir, string tool)
    {
        var bin = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
        var (tfm, configuration) = (bin.Name, bin.Parent!.Name);
        var root = bin;
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "maf-lab.sln")))
        {
            root = root.Parent;
        }
        return Path.Combine(root!.FullName, projectDir, "bin", configuration, tfm, tool);
    }

    /// <summary>Sets SIGINT to its default, then becomes the command it is given (same pid).</summary>
    private const string DefaultSigint =
        "import os, signal, sys; signal.signal(signal.SIGINT, signal.SIG_DFL); os.execvp(sys.argv[1], sys.argv[1:])";

    private static async Task<(int Exit, string Stderr)> InterruptAsync(string signal, string tool, string[] args, Dictionary<string, string> env)
    {
        var (file, argv) = signal == "INT"
            ? ("python3", (string[])["-c", DefaultSigint, "dotnet", tool, .. args])
            : ("dotnet", [tool, .. args]);
        var start = new ProcessStartInfo(file, argv)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            WorkingDirectory = Path.GetTempPath(),
        };
        foreach (var (k, v) in env)
        {
            start.Environment[k] = v;
        }
        using var process = Process.Start(start)!;
        var stderr = process.StandardError.ReadToEndAsync(Ct);
        _ = process.StandardOutput.ReadToEndAsync(Ct);
        // Long enough to be caught waiting on the black hole, not long enough to give up on it.
        await Task.Delay(TimeSpan.FromSeconds(4), Ct);
        if (process.HasExited)
        {
            Assert.Fail($"{Path.GetFileName(tool)} ended before it was interrupted: {await stderr}");
        }

        using (var kill = Process.Start("kill", [$"-{signal}", process.Id.ToString()])!)
        {
            await kill.WaitForExitAsync(Ct);
        }
        await process.WaitForExitAsync(Ct).WaitAsync(TimeSpan.FromSeconds(30), Ct);
        return (process.ExitCode, await stderr);
    }

    private static string LastLine(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()).Where(l => l.Length > 0).LastOrDefault() ?? "";

    [Theory]
    [InlineData("TERM")]
    [InlineData("INT")]
    public async Task The_a2a_probe_stops_when_told(string signal)
    {
        using var hole = new BlackHole();

        var (exit, stderr) = await InterruptAsync(signal, ToolPath("tools/Maf.Lab.A2AProbe", "Maf.Lab.A2AProbe.dll"),
            [$"http://127.0.0.1:{hole.Port}"], new());

        Assert.Equal(130, exit);
        Assert.Contains("Cancelled", LastLine(stderr), StringComparison.Ordinal);
        Assert.Contains("make eval-a2a", LastLine(stderr), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("TERM")]
    [InlineData("INT")]
    public async Task The_eval_tool_stops_when_told_and_keeps_what_finished(string signal)
    {
        using var hole = new BlackHole();

        var (exit, stderr) = await InterruptAsync(signal, ToolPath("src/Maf.Lab.Eval", "Maf.Lab.Eval.dll"),
            ["--ask", "what is the procedure when a fee schedule is missing"], new()
        {
            ["Qdrant__Host"] = "127.0.0.1",
            ["Qdrant__GrpcPort"] = hole.Port.ToString(),
            ["SharedState__ConnectionString"] = $"127.0.0.1:{hole.Port},abortConnect=false",
        });

        Assert.Equal(130, exit);
        Assert.Equal(Maf.Lab.Eval.Program.AfterCancel, LastLine(stderr));
    }

    [Theory]
    [InlineData("TERM")]
    [InlineData("INT")]
    public async Task The_indexer_stops_when_told_and_says_what_to_run_again(string signal)
    {
        using var hole = new BlackHole();

        var (exit, stderr) = await InterruptAsync(signal, ToolPath("src/Maf.Lab.Indexing", "Maf.Lab.Indexing.dll"), ["index"], new()
        {
            ["Qdrant__Host"] = "127.0.0.1",
            ["Qdrant__GrpcPort"] = hole.Port.ToString(),
        });

        Assert.Equal(130, exit);
        Assert.Contains("Cancelled.", LastLine(stderr), StringComparison.Ordinal);
        Assert.Contains("make index", LastLine(stderr), StringComparison.Ordinal);
    }
}
