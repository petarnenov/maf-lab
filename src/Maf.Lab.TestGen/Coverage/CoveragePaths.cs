namespace Maf.Lab.TestGen.Coverage;

/// <summary>
/// Turns the file names a report uses into repo-relative paths, and decides which files are coverage targets.
/// A name is resolved against, in order: the directory the tests ran in (when the caller knows it), the report's own
/// sources, and finally the longest tail of an absolute name that names a file the repository has — which is how a
/// report written inside a container (<c>/src/src/Maf.Lab.Api/…</c>) is read on the host. Whatever does not end up
/// naming a file of the repository is dropped and counted, never stored.
/// </summary>
public static class CoveragePaths
{
    public static NormalisedCoverage Normalise(RawCoverageReport raw, string? measuredRoot, IReadOnlySet<string> repoFiles)
    {
        var roots = new List<string>();
        if (!string.IsNullOrWhiteSpace(measuredRoot))
        {
            roots.Add(measuredRoot);
        }
        roots.AddRange(raw.Sources);

        var files = new Dictionary<string, FileCoverage>(StringComparer.Ordinal);
        var dropped = 0;
        foreach (var file in raw.Files)
        {
            var path = Resolve(file.Path, roots, repoFiles);
            if (path is null)
            {
                dropped++;
                continue;
            }
            if (!IsTarget(path))
            {
                continue;
            }
            // Two report entries for one file (unlikely, but harmless): the later wins.
            files[path] = file with { Path = path };
        }
        return new NormalisedCoverage(files.Values.OrderBy(f => f.Path, StringComparer.Ordinal).ToList(), dropped);
    }

    /// <summary>A repo-relative, forward-slash path for <paramref name="name"/>, or null when it names nothing in the repository.</summary>
    public static string? Resolve(string name, IEnumerable<string> roots, IReadOnlySet<string> repoFiles)
    {
        var slashed = name.Replace('\\', '/');
        var absolute = slashed.StartsWith('/') || (slashed.Length > 2 && slashed[1] == ':');
        var candidates = new List<string>();
        foreach (var root in roots)
        {
            var r = root.Replace('\\', '/').TrimEnd('/');
            if (absolute)
            {
                if (slashed.StartsWith(r + "/", StringComparison.Ordinal))
                {
                    candidates.Add(slashed[(r.Length + 1)..]);
                }
            }
            else
            {
                candidates.Add(r + "/" + slashed);
            }
        }
        if (!absolute)
        {
            candidates.Add(slashed);
        }

        foreach (var candidate in candidates)
        {
            // A source root may itself be absolute (Vitest writes the web directory's absolute path): a combined
            // candidate is then retried through the tail search below.
            if (Clean(candidate) is { } clean && repoFiles.Contains(clean))
            {
                return clean;
            }
        }
        foreach (var candidate in candidates.Where(c => c.StartsWith('/')).Append(absolute ? slashed : null).OfType<string>())
        {
            if (Tail(candidate, repoFiles) is { } tail)
            {
                return tail;
            }
        }
        return null;
    }

    /// <summary>Whether a repo-relative path is production source the Coverage screen shows: C# under src/, TS under web/src/.</summary>
    public static bool IsTarget(string path)
    {
        if (path.EndsWith(".cs", StringComparison.Ordinal))
        {
            return path.StartsWith("src/", StringComparison.Ordinal)
                && !path.Contains("/obj/", StringComparison.Ordinal)
                && !path.Contains("/bin/", StringComparison.Ordinal)
                && !path.EndsWith(".g.cs", StringComparison.Ordinal)
                && !path.EndsWith(".designer.cs", StringComparison.OrdinalIgnoreCase);
        }
        if (path.EndsWith(".ts", StringComparison.Ordinal) || path.EndsWith(".tsx", StringComparison.Ordinal))
        {
            return path.StartsWith("web/src/", StringComparison.Ordinal)
                && !path.StartsWith("web/src/test/", StringComparison.Ordinal)
                && !path.EndsWith(".d.ts", StringComparison.Ordinal)
                && !path.Contains(".test.", StringComparison.Ordinal);
        }
        return false;
    }

    /// <summary>
    /// A repo-relative path as given, with "." segments removed. Null for anything that is not one: absolute paths,
    /// ".." segments, empty segments, control characters.
    /// </summary>
    public static string? Clean(string path)
    {
        var slashed = path.Replace('\\', '/');
        if (slashed.StartsWith('/') || slashed.Contains(':') || slashed.Any(char.IsControl))
        {
            return null;
        }
        var segments = slashed.Split('/').Where(s => s != ".").ToList();
        if (segments.Count == 0 || segments.Any(s => s.Length == 0 || s == ".."))
        {
            return null;
        }
        return string.Join('/', segments);
    }

    /// <summary>The longest tail of an absolute name that is a repository file under src/ or web/src/.</summary>
    private static string? Tail(string absolute, IReadOnlySet<string> repoFiles)
    {
        var segments = absolute.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Contains(".."))
        {
            return null;
        }
        for (var i = 0; i < segments.Length; i++)
        {
            var tail = string.Join('/', segments[i..]);
            if ((tail.StartsWith("src/", StringComparison.Ordinal) || tail.StartsWith("web/src/", StringComparison.Ordinal))
                && repoFiles.Contains(tail))
            {
                return tail;
            }
        }
        return null;
    }
}
