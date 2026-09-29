using System.Diagnostics;
using Maf.Lab.Domain.Admin;
using Maf.Lab.Domain.Retrieval;
using Maf.Lab.Domain.Tenancy;

namespace Maf.Lab.Indexing.Corpus;

/// <summary>
/// Loads the repository itself as a corpus (add-codebase-search): the files git tracks — or, outside a work tree, a walk
/// that skips build output — under the configured include prefixes, minus the excluded ones. Source files are code,
/// Markdown is docs. The codebase belongs to no firm, so every document is shared: the tenant still comes from the
/// layout, which here has exactly one tenant, never from a path segment.
/// </summary>
public static class RepositoryCorpusLoader
{
    public static readonly IReadOnlySet<string> CodeExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".ts", ".tsx", ".js", ".jsx", ".py", ".sql", ".sh",
    };

    public static readonly IReadOnlySet<string> DocExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".md" };

    private static readonly string[] BuildOutput = ["bin/", "obj/", "node_modules/", "dist/", "TestResults/", ".git/"];

    public static CorpusSnapshot Load(string root, IReadOnlyList<string> include, IReadOnlyList<string> exclude, int maxFileBytes,
        IReadOnlySet<TenantId>? onlyTenants = null)
    {
        var documents = new List<SourceDocument>();
        var rejected = new List<RejectedDocument>();
        if (!Directory.Exists(root))
        {
            return new CorpusSnapshot(documents, [new RejectedDocument(root, "Repository root does not exist.")]);
        }
        var layout = new HashSet<TenantId> { TenantId.Shared };
        if (onlyTenants is not null && !onlyTenants.Contains(TenantId.Shared))
        {
            return new CorpusSnapshot(documents, rejected) { LayoutTenants = new HashSet<TenantId>() };
        }

        foreach (var relative in (GitFiles(root) ?? WalkedFiles(root)).Order(StringComparer.Ordinal))
        {
            if (!Included(relative, include, exclude))
            {
                continue;
            }
            var extension = Path.GetExtension(relative);
            var sourceType = CodeExtensions.Contains(extension) ? SourceType.Code
                : DocExtensions.Contains(extension) ? SourceType.Docs
                : null;
            if (sourceType is null)
            {
                continue;
            }
            var full = Path.Combine(root, relative);
            var info = new FileInfo(full);
            if (!info.Exists)
            {
                continue;
            }
            if (info.Length > maxFileBytes)
            {
                rejected.Add(new RejectedDocument(relative, $"Larger than {maxFileBytes} bytes; generated or data, not source."));
                continue;
            }
            var updatedAt = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero);
            updatedAt = updatedAt.AddTicks(-(updatedAt.Ticks % TimeSpan.TicksPerSecond));
            documents.Add(new SourceDocument(TenantId.Shared, sourceType, relative, full, File.ReadAllText(full), updatedAt) { FromRepository = true });
        }
        return new CorpusSnapshot(documents, rejected) { LayoutTenants = layout };
    }

    /// <summary>A path is in when it starts with an include prefix (or equals it) and with no exclude prefix.</summary>
    public static bool Included(string relative, IReadOnlyList<string> include, IReadOnlyList<string> exclude)
    {
        if (BuildOutput.Any(b => relative.StartsWith(b, StringComparison.Ordinal) || relative.Contains('/' + b, StringComparison.Ordinal)))
        {
            return false;
        }
        if (relative.Split('/').Any(s => s.StartsWith('.')))
        {
            return false;
        }
        return include.Any(p => Matches(relative, p)) && !exclude.Any(p => Matches(relative, p));
    }

    private static bool Matches(string relative, string prefix) =>
        prefix.EndsWith('/') ? relative.StartsWith(prefix, StringComparison.Ordinal) : relative == prefix || relative.StartsWith(prefix + "/", StringComparison.Ordinal);

    /// <summary>Tracked and untracked-but-not-ignored files, so .gitignore decides what is build output. Null outside git.</summary>
    private static IEnumerable<string>? GitFiles(string root)
    {
        try
        {
            using var git = Process.Start(new ProcessStartInfo("git", ["-C", root, "ls-files", "--cached", "--others", "--exclude-standard", "-z"])
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            });
            if (git is null)
            {
                return null;
            }
            var output = git.StandardOutput.ReadToEnd();
            git.WaitForExit();
            return git.ExitCode == 0 ? output.Split('\0', StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.Ordinal).ToList() : null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    private static IEnumerable<string> WalkedFiles(string root) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'));
}
