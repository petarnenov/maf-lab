using System.Text;
using Maf.Lab.TestGen;

namespace Maf.Lab.Api.Coverage;

/// <summary>A copy of the repository at a commit with a diff applied, in a temporary worktree nobody works in.</summary>
public sealed class WorkingCopy : IAsyncDisposable
{
    private readonly string _repo;

    internal WorkingCopy(string repo, string root, string commit)
    {
        _repo = repo;
        Root = root;
        Commit = commit;
    }

    public string Root { get; }
    public string Commit { get; }

    /// <summary>The current text of every file that differs from the commit.</summary>
    public async Task<IReadOnlyDictionary<string, string>> ChangedFilesAsync(CancellationToken ct)
    {
        await Git.CheckedAsync(Root, ["add", "-A"], ct);
        var names = (await Git.CheckedAsync(Root, ["diff", "--cached", "--name-only", "-z", Commit], ct)).Text
            .Split('\0', StringSplitOptions.RemoveEmptyEntries);
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in names.Where(n => File.Exists(Path.Combine(Root, n))))
        {
            files[name] = await File.ReadAllTextAsync(Path.Combine(Root, name), ct);
        }
        return files;
    }

    public Task WriteAsync(string path, string content, CancellationToken ct) =>
        File.WriteAllTextAsync(Path.Combine(Root, WorkspacePaths.Relative(path)), content, ct);

    /// <summary>Everything that differs from the commit, as one diff.</summary>
    public async Task<string> DiffAsync(CancellationToken ct)
    {
        await Git.CheckedAsync(Root, ["add", "-A"], ct);
        return (await Git.CheckedAsync(Root, ["diff", "--cached", "--no-color", "--no-ext-diff", Commit], ct)).Text;
    }

    public async ValueTask DisposeAsync()
    {
        await Git.RunAsync(_repo, ["worktree", "remove", "--force", Root], CancellationToken.None);
        if (Directory.Exists(Root))
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch (IOException)
            {
            }
        }
        await Git.RunAsync(_repo, ["worktree", "prune"], CancellationToken.None);
    }
}

/// <summary>How an accept ended.</summary>
public abstract record MergeOutcome
{
    public sealed record Merged(string Commit) : MergeOutcome;
    public sealed record Conflict : MergeOutcome;
    public sealed record Dirty : MergeOutcome;
    public sealed record Missing : MergeOutcome;
}

/// <summary>
/// The only place the api writes to the repository: candidate branches, and merging one into main. Branches are made
/// in a temporary worktree, so no one's checkout is touched. A merge goes through the checkout that has main when there
/// is one — and only if it is clean — and otherwise updates main by compare-and-swap. One write at a time.
/// </summary>
public sealed class RepoWriter(GitRepository repository, ILogger<RepoWriter> logger)
{
    public static readonly IReadOnlyDictionary<string, string> Identity = new Dictionary<string, string>
    {
        ["GIT_AUTHOR_NAME"] = "maf-lab test-agent",
        ["GIT_AUTHOR_EMAIL"] = "test-agent@maf-lab.invalid",
        ["GIT_COMMITTER_NAME"] = "maf-lab test-agent",
        ["GIT_COMMITTER_EMAIL"] = "test-agent@maf-lab.invalid",
    };

    private static readonly SemaphoreSlim Writes = new(1, 1);

    /// <summary>Runs between building a merge commit and moving main: a test moves main here to prove the swap.</summary>
    internal Func<Task>? BeforeSwap { get; set; }

    /// <summary>A working copy at the commit with the diff applied, or null when the diff does not apply.</summary>
    public async Task<WorkingCopy?> CheckOutAsync(string commit, string diff, CancellationToken ct)
    {
        var repo = await repository.RootAsync(ct);
        var root = Path.Combine(Path.GetTempPath(), $"maf-wc-{Guid.NewGuid():N}");
        await Writes.WaitAsync(ct);
        try
        {
            await Git.CheckedAsync(repo, ["worktree", "add", "-q", "--detach", root, commit], ct);
        }
        finally
        {
            Writes.Release();
        }
        var copy = new WorkingCopy(repo, root, commit);
        if (diff.Length > 0)
        {
            var applied = await Git.RunAsync(root, ["apply", "--whitespace=nowarn", "-"], ct, stdin: Encoding.UTF8.GetBytes(diff));
            if (!applied.Ok)
            {
                await copy.DisposeAsync();
                return null;
            }
        }
        return copy;
    }

    /// <summary>Commits the working copy onto a new branch based on its commit. An existing branch of that name is replaced.</summary>
    public async Task CommitBranchAsync(WorkingCopy copy, string branch, string message, CancellationToken ct)
    {
        await Git.CheckedAsync(copy.Root, ["add", "-A"], ct);
        await Git.CheckedAsync(copy.Root, ["commit", "-q", "--no-verify", "-m", message], ct, environment: Identity);
        var head = (await Git.CheckedAsync(copy.Root, ["rev-parse", "HEAD"], ct)).Text.Trim();
        await Writes.WaitAsync(ct);
        try
        {
            await Git.CheckedAsync(await repository.RootAsync(ct), ["branch", "-f", branch, head], ct);
        }
        finally
        {
            Writes.Release();
        }
    }

    public async Task<MergeOutcome> MergeAsync(string branch, string mainBranch, string message, CancellationToken ct)
    {
        var repo = await repository.RootAsync(ct);
        await Writes.WaitAsync(ct);
        try
        {
            if (!(await Git.RunAsync(repo, ["rev-parse", "--verify", "--quiet", $"refs/heads/{branch}"], ct)).Ok)
            {
                return new MergeOutcome.Missing();
            }
            // Main checked out somewhere: merge there, the way a person would, but only into a clean tree.
            if (await CheckoutOfAsync(repo, mainBranch, ct) is { } checkout)
            {
                var status = await Git.CheckedAsync(checkout, ["status", "--porcelain", "--untracked-files=no"], ct);
                if (status.Text.Trim().Length > 0)
                {
                    return new MergeOutcome.Dirty();
                }
                var merged = await Git.RunAsync(checkout, ["merge", "--no-ff", "--no-edit", "-m", message, branch], ct, environment: Identity);
                if (!merged.Ok)
                {
                    await Git.RunAsync(checkout, ["merge", "--abort"], ct);
                    return new MergeOutcome.Conflict();
                }
                return new MergeOutcome.Merged((await Git.CheckedAsync(checkout, ["rev-parse", "HEAD"], ct)).Text.Trim());
            }
            // Nobody has main checked out: merge in a temporary worktree at main, then move main only if it has not
            // moved meanwhile (compare-and-swap), once more if it has.
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var main = (await Git.CheckedAsync(repo, ["rev-parse", $"refs/heads/{mainBranch}"], ct)).Text.Trim();
                var scratch = Path.Combine(Path.GetTempPath(), $"maf-merge-{Guid.NewGuid():N}");
                await Git.CheckedAsync(repo, ["worktree", "add", "-q", "--detach", scratch, main], ct);
                try
                {
                    var merged = await Git.RunAsync(scratch, ["merge", "--no-ff", "--no-edit", "-m", message, branch], ct, environment: Identity);
                    if (!merged.Ok)
                    {
                        return new MergeOutcome.Conflict();
                    }
                    var commit = (await Git.CheckedAsync(scratch, ["rev-parse", "HEAD"], ct)).Text.Trim();
                    if (BeforeSwap is { } hook)
                    {
                        await hook();
                    }
                    if ((await Git.RunAsync(repo, ["update-ref", $"refs/heads/{mainBranch}", commit, main], ct)).Ok)
                    {
                        return new MergeOutcome.Merged(commit);
                    }
                }
                finally
                {
                    await Git.RunAsync(repo, ["worktree", "remove", "--force", scratch], CancellationToken.None);
                    await Git.RunAsync(repo, ["worktree", "prune"], CancellationToken.None);
                }
                logger.LogInformation("{Main} moved during a merge; trying once more", mainBranch);
            }
            return new MergeOutcome.Conflict();
        }
        finally
        {
            Writes.Release();
        }
    }

    public async Task DeleteBranchAsync(string branch, CancellationToken ct)
    {
        await Writes.WaitAsync(ct);
        try
        {
            await Git.RunAsync(await repository.RootAsync(ct), ["branch", "-D", branch], ct);
        }
        finally
        {
            Writes.Release();
        }
    }

    /// <summary>The worktree that has <paramref name="branch"/> checked out, if any.</summary>
    private static async Task<string?> CheckoutOfAsync(string repo, string branch, CancellationToken ct)
    {
        var list = (await Git.CheckedAsync(repo, ["worktree", "list", "--porcelain"], ct)).Text;
        string? path = null;
        foreach (var line in list.Split('\n'))
        {
            if (line.StartsWith("worktree ", StringComparison.Ordinal))
            {
                path = line["worktree ".Length..];
            }
            else if (line == $"branch refs/heads/{branch}" && path is not null && Directory.Exists(path))
            {
                return path;
            }
        }
        return null;
    }

    /// <summary>A branch name for a run: test-agent/&lt;file&gt;-&lt;run&gt;, safe for git and short.</summary>
    public static string BranchFor(string path, string runId)
    {
        var slug = new string(path.Select(c => char.IsAsciiLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-').ToArray()).Trim('-');
        while (slug.Contains("--", StringComparison.Ordinal))
        {
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        }
        return $"test-agent/{(slug.Length > 60 ? slug[^60..].TrimStart('-') : slug)}-{runId}";
    }
}
