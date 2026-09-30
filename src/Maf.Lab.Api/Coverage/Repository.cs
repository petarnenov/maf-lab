using Maf.Lab.TestGen;
using Microsoft.Extensions.Options;

namespace Maf.Lab.Api.Coverage;

/// <summary>The repository this lab is built from, read through git at a given commit. Never the working tree.</summary>
public interface IRepository
{
    /// <summary>The commit a revision (a branch name) points at, or null.</summary>
    Task<string?> ResolveAsync(string revision, CancellationToken ct);

    /// <summary>Every file in the commit, repo-relative.</summary>
    Task<IReadOnlySet<string>> FilesAsync(string commit, CancellationToken ct);

    /// <summary>A file's text at the commit, or null when the commit does not have it.</summary>
    Task<string?> ShowAsync(string commit, string path, CancellationToken ct);
}

/// <summary><see cref="IRepository"/> on the git command line.</summary>
public sealed class GitRepository(IOptions<CoverageOptions> options, IHostEnvironment environment) : IRepository
{
    private string? _root;

    /// <summary>The working tree git commands run in.</summary>
    public async Task<string> RootAsync(CancellationToken ct)
    {
        if (_root is not null)
        {
            return _root;
        }
        var configured = options.Value.RepoRoot;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return _root = configured;
        }
        var top = await Git.CheckedAsync(environment.ContentRootPath, ["rev-parse", "--show-toplevel"], ct);
        return _root = top.Text.Trim();
    }

    public async Task<string?> ResolveAsync(string revision, CancellationToken ct)
    {
        // A branch name, never an option: git reads a leading dash as one.
        if (revision.StartsWith('-') || revision.Any(char.IsWhiteSpace))
        {
            return null;
        }
        var result = await Git.RunAsync(await RootAsync(ct), ["rev-parse", "--verify", "--quiet", $"{revision}^{{commit}}"], ct);
        return result.Ok ? result.Text.Trim() : null;
    }

    public async Task<IReadOnlySet<string>> FilesAsync(string commit, CancellationToken ct)
    {
        if (!Git.IsCommitId(commit))
        {
            return new HashSet<string>();
        }
        var result = await Git.RunAsync(await RootAsync(ct), ["ls-tree", "-r", "--name-only", "-z", commit], ct);
        return result.Ok
            ? result.Text.Split('\0', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>();
    }

    public async Task<string?> ShowAsync(string commit, string path, CancellationToken ct)
    {
        if (!Git.IsCommitId(commit) || CoveragePaths.Clean(path) != path)
        {
            return null;
        }
        var result = await Git.RunAsync(await RootAsync(ct), ["show", $"{commit}:{path}"], ct);
        return result.Ok ? result.Text : null;
    }
}
