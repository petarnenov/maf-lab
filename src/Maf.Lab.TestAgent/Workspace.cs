using Maf.Lab.TestGen;

namespace Maf.Lab.TestAgent;

/// <summary>
/// One task's scratch checkout: a clone of the read-only repository at the task's commit, sharing its objects. The
/// agent reads it and writes test files into it; nothing in it is ever built or run here — the diff goes to the
/// runner. It is deleted when the task ends.
/// </summary>
public sealed class Workspace : IAsyncDisposable
{
    private Workspace(string root, string commit, string toolchain)
    {
        Root = root;
        Commit = commit;
        Toolchain = toolchain;
    }

    public string Root { get; }
    public string Commit { get; }
    public string Toolchain { get; }

    public static async Task<Workspace> CreateAsync(string repoRoot, string workRoot, string taskId, string commit, string toolchain,
        CancellationToken ct)
    {
        var dir = Path.Combine(workRoot, $"task-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workRoot);
        await Git.CheckedAsync(workRoot, ["clone", "-q", "--shared", "--no-checkout", repoRoot, dir], ct);
        await Git.CheckedAsync(dir, ["checkout", "-q", "--detach", commit], ct);
        return new Workspace(dir, commit, toolchain);
    }

    /// <summary>Whether the commit has this file (as committed, not as the agent may have written it since).</summary>
    public async Task<bool> CommitHasAsync(string path, CancellationToken ct) =>
        (await Git.RunAsync(Root, ["cat-file", "-e", $"{Commit}:{path}"], ct)).Ok;

    /// <summary>Everything written so far, as a unified diff against the commit (new files included).</summary>
    public async Task<string> DiffAsync(CancellationToken ct)
    {
        await Git.CheckedAsync(Root, ["add", "-A"], ct);
        return (await Git.CheckedAsync(Root, ["diff", "--cached", "--no-color", "--no-ext-diff", Commit], ct)).Text;
    }

    /// <summary>The current content of every file the diff touches that still exists, for the guardrails.</summary>
    public async Task<IReadOnlyDictionary<string, string>> ChangedFilesAsync(CancellationToken ct)
    {
        await Git.CheckedAsync(Root, ["add", "-A"], ct);
        var names = (await Git.CheckedAsync(Root, ["diff", "--cached", "--name-only", "-z", Commit], ct)).Text
            .Split('\0', StringSplitOptions.RemoveEmptyEntries);
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in names)
        {
            var full = Path.Combine(Root, name);
            if (File.Exists(full))
            {
                files[name] = await File.ReadAllTextAsync(full, ct);
            }
        }
        return files;
    }

    /// <summary>Puts the tests back as a diff left them: used to return to the best attempt so far.</summary>
    public async Task ResetToAsync(string diff, CancellationToken ct)
    {
        await Git.CheckedAsync(Root, ["reset", "-q", "--hard", Commit], ct);
        await Git.CheckedAsync(Root, ["clean", "-q", "-fd"], ct);
        if (diff.Length > 0)
        {
            await Git.CheckedAsync(Root, ["apply", "--whitespace=nowarn", "-"], ct, stdin: System.Text.Encoding.UTF8.GetBytes(diff));
        }
    }

    public ValueTask DisposeAsync()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // Left for the next start's sweep; a scratch checkout holds nothing anyone needs.
        }
        catch (UnauthorizedAccessException)
        {
        }
        return ValueTask.CompletedTask;
    }
}
