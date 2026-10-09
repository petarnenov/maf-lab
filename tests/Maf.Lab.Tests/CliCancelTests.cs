using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;

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
public sealed partial class CliCancelTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Accepts connections and never says a word: whatever talks to it waits.</summary>
    private sealed class BlackHole : IDisposable
    {
        private readonly TcpListener listener = new(IPAddress.Loopback, 0);
        private readonly List<TcpClient> held = [];
        private readonly TaskCompletionSource reached = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public BlackHole()
        {
            listener.Start();
            _ = AcceptAsync();
        }

        public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;

        /// <summary>Completes once something has connected: whatever did is now waiting on it.</summary>
        public Task Reached => reached.Task;

        private async Task AcceptAsync()
        {
            try
            {
                while (true)
                {
                    held.Add(await listener.AcceptTcpClientAsync());
                    reached.TrySetResult();
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

    /// <summary>The CLI's installed providers, found by manifest capability so no test names a bundled plugin.</summary>
    private sealed class CliProviders : IDisposable
    {
        public string Root { get; } = Directory.CreateTempSubdirectory("maf-lab-cli-providers-").FullName;
        public string Chat { get; }

        public CliProviders()
        {
            var providers = Directory.EnumerateDirectories(Path.Combine(CorpusLoaderTests.RepoRoot(), "plugins"))
                .Select(d => Path.Combine(d, "plugin.toml")).Where(File.Exists).Select(File.ReadAllText)
                .Where(t => Regex.IsMatch(t, @"(?m)^kind\s*=\s*""provider"""))
                .Select(t => new
                {
                    schema = 1,
                    name = Regex.Match(t, @"(?m)^name\s*=\s*""([^""]+)""").Groups[1].Value,
                    kind = "provider",
                    provides = Regex.Match(t, @"(?m)^provides\s*=\s*""([^""]+)""").Groups[1].Value,
                    environments = new[] { "dev" },
                }).OrderBy(p => p.name, StringComparer.Ordinal).GroupBy(p => p.provides).Select(g => g.First()).ToList();
            Assert.SkipUnless(new[] { "decision-engine", "chat-model", "embeddings" }.All(k => providers.Any(p => p.provides == k)),
                "CLI cancellation needs bundled decision, chat and embeddings providers");
            Chat = providers.Single(p => p.provides == "chat-model").name;
            File.WriteAllText(Path.Combine(Root, ".installed"), JsonSerializer.Serialize(new
            {
                schema = 1, env = "dev",
                plugins = providers.Select(p => new { manifest = p, serverJson = (object?)null, hasServer = false }),
            }));
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
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

    private static async Task<(int Exit, string Stderr)> InterruptAsync(string signal, BlackHole hole, string tool, string[] args,
        Dictionary<string, string> env)
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
        // Under `dotnet test --coverage` the tool would inherit the coverage profiler and join the test host's session:
        // killed seconds later, its near-empty hits replace the host's for every module it loads (Api, Domain, …).
        foreach (var key in start.Environment.Keys.Where(k => k.StartsWith("CORECLR_", StringComparison.Ordinal)
                     || k.StartsWith("CODE_COVERAGE_", StringComparison.Ordinal)
                     || k.StartsWith("MicrosoftInstrumentationEngine_", StringComparison.Ordinal)).ToList())
        {
            start.Environment.Remove(key);
        }
        foreach (var (k, v) in env)
        {
            start.Environment[k] = v;
        }
        using var process = Process.Start(start)!;
        var stderr = process.StandardError.ReadToEndAsync(Ct);
        _ = process.StandardOutput.ReadToEndAsync(Ct);
        // Caught waiting: every tool takes its signals before it first connects, so once the black hole is reached a
        // signal finds the tool's handler, however slowly the machine started it. The bound only keeps a tool that
        // never connects from hanging the run.
        await Task.WhenAny(hole.Reached, process.WaitForExitAsync(Ct)).WaitAsync(TimeSpan.FromSeconds(60), Ct);
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
    public async Task The_indexer_stops_when_told_and_says_what_to_run_again(string signal)
    {
        using var hole = new BlackHole();
        using var providers = new CliProviders();
        // A corpus of one document: a run over a missing corpus stops before it reaches the store, with nothing to stop.
        var corpus = Directory.CreateTempSubdirectory("maf-lab-cli-corpus-");
        Directory.CreateDirectory(Path.Combine(corpus.FullName, "firm-a", "docs"));
        File.WriteAllText(Path.Combine(corpus.FullName, "firm-a", "docs", "fees.md"), "# Fees\n\nA fee schedule is assigned per account.\n");

        try
        {
            var (exit, stderr) = await InterruptAsync(signal, hole, ToolPath("src/Maf.Lab.Indexing", "Maf.Lab.Indexing.dll"), ["index"], new()
            {
                ["Qdrant__Host"] = "127.0.0.1",
                ["Qdrant__GrpcPort"] = hole.Port.ToString(),
                ["Indexing__CorpusRoot"] = corpus.FullName,
                ["Plugins__Root"] = providers.Root,
                ["MAF_CHAT_MODEL"] = providers.Chat,
            });

            Assert.Equal(130, exit);
            Assert.Contains("Cancelled.", LastLine(stderr), StringComparison.Ordinal);
            Assert.Contains("make index", LastLine(stderr), StringComparison.Ordinal);
        }
        finally
        {
            corpus.Delete(recursive: true);
        }
    }
}
