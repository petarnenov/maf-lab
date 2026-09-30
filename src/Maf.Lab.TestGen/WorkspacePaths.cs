namespace Maf.Lab.TestGen;

public enum PathAccess
{
    Read,
    Write,
}

/// <summary>A path that may not be used for what was asked, with the reason in words fit for the model.</summary>
public sealed class PathRefusedException(string message) : Exception(message);

/// <summary>
/// The one place a path the model (or a diff) names is checked, in the agent and in the api alike. A path is
/// resolved against the workspace root and must stay under it after normalisation; no segment may be a link; a
/// write must land in a test location of the task's toolchain. Production code is read-only.
/// </summary>
public static class WorkspacePaths
{
    public const string WriteRefusal =
        "Writes are limited to test files: under tests/ for dotnet, and *.test.ts, *.test.tsx or web/src/test/ for vitest. Production code is read-only.";

    /// <summary>The full path for <paramref name="path"/> under <paramref name="root"/>, or a refusal.</summary>
    public static string Resolve(string root, string path, PathAccess access, string toolchain)
    {
        var relative = Relative(path);
        if (access == PathAccess.Write && !IsWritable(relative, toolchain))
        {
            throw new PathRefusedException(WriteRefusal);
        }
        if (access == PathAccess.Read && !IsReadable(relative))
        {
            throw new PathRefusedException("That file cannot be read.");
        }

        var fullRoot = Path.GetFullPath(root);
        var full = Path.GetFullPath(Path.Combine(fullRoot, relative));
        if (!full.StartsWith(fullRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new PathRefusedException("That path is outside the repository.");
        }
        // Every segment that exists must be a plain file or directory: a link could point anywhere.
        var current = fullRoot;
        foreach (var segment in relative.Split('/'))
        {
            current = Path.Combine(current, segment);
            FileSystemInfo info = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
            if (info.Exists && info.LinkTarget is not null)
            {
                throw new PathRefusedException("That path goes through a link.");
            }
        }
        return full;
    }

    /// <summary>
    /// A repo-relative, forward-slash path as given, or a refusal: absolute paths, drive letters, backslashes,
    /// NUL and other control characters, empty and ".." segments are never a path in the repository.
    /// </summary>
    public static string Relative(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Any(char.IsControl) || path.Contains('\\') || path.StartsWith('/')
            || (path.Length > 1 && path[1] == ':'))
        {
            throw new PathRefusedException("Give a path relative to the repository root, with forward slashes.");
        }
        var segments = path.Split('/').Where(s => s != ".").ToList();
        if (segments.Count == 0 || segments.Any(s => s.Length == 0 || s == ".."))
        {
            throw new PathRefusedException("That path is outside the repository.");
        }
        return string.Join('/', segments);
    }

    /// <summary>Whether a repo-relative path is a test location the toolchain's tests may be written to.</summary>
    public static bool IsWritable(string relative, string toolchain) => toolchain switch
    {
        "dotnet" => relative.StartsWith("tests/", StringComparison.Ordinal)
            && relative.EndsWith(".cs", StringComparison.Ordinal)
            && !relative.Split('/').Any(s => s is "bin" or "obj"),
        "vitest" => relative.StartsWith("web/src/", StringComparison.Ordinal)
            && (relative.EndsWith(".test.ts", StringComparison.Ordinal) || relative.EndsWith(".test.tsx", StringComparison.Ordinal)
                || relative.StartsWith("web/src/test/", StringComparison.Ordinal))
            && !relative.Split('/').Contains("node_modules"),
        _ => false,
    };

    /// <summary>Anything in the repository except its git internals and files that hold settings or secrets.</summary>
    public static bool IsReadable(string relative)
    {
        var segments = relative.Split('/');
        var name = segments[^1];
        return !segments.Contains(".git")
            && !name.StartsWith(".env", StringComparison.Ordinal)
            && !(name.StartsWith("appsettings", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                 && !name.Equals("appsettings.json", StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>The files a unified diff touches, read from its headers — both sides of every rename or copy.</summary>
public static class DiffPaths
{
    public static IReadOnlyList<string> Of(string diff)
    {
        var paths = new List<string>();
        foreach (var raw in diff.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.StartsWith("diff --git ", StringComparison.Ordinal))
            {
                // "diff --git a/x b/y": split on " b/" from the right, so a path containing " b/" still reads right.
                var rest = line["diff --git ".Length..];
                var split = rest.LastIndexOf(" b/", StringComparison.Ordinal);
                if (split > 0 && rest.StartsWith("a/", StringComparison.Ordinal))
                {
                    paths.Add(rest[2..split]);
                    paths.Add(rest[(split + 3)..]);
                }
                else
                {
                    paths.Add(rest);
                }
            }
            else if (line.StartsWith("--- ", StringComparison.Ordinal) || line.StartsWith("+++ ", StringComparison.Ordinal))
            {
                var name = line[4..];
                if (name != "/dev/null")
                {
                    paths.Add(name.StartsWith("a/", StringComparison.Ordinal) || name.StartsWith("b/", StringComparison.Ordinal) ? name[2..] : name);
                }
            }
            else if (line.StartsWith("rename from ", StringComparison.Ordinal) || line.StartsWith("copy from ", StringComparison.Ordinal))
            {
                paths.Add(line[(line.IndexOf(" from ", StringComparison.Ordinal) + 6)..]);
            }
            else if (line.StartsWith("rename to ", StringComparison.Ordinal) || line.StartsWith("copy to ", StringComparison.Ordinal))
            {
                paths.Add(line[(line.IndexOf(" to ", StringComparison.Ordinal) + 4)..]);
            }
        }
        return paths.Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>The paths of a diff that are not writable test locations for the toolchain; empty when it may be applied.</summary>
    public static IReadOnlyList<string> Forbidden(string diff, string toolchain) =>
        // A binary patch is never a test the lab can read, check or review.
        (diff.Contains("GIT binary patch", StringComparison.Ordinal) || diff.Contains("Binary files ", StringComparison.Ordinal)
            ? ["(binary patch)"]
            : Array.Empty<string>()).Concat(Of(diff).Where(p =>
        {
            try
            {
                return !WorkspacePaths.IsWritable(WorkspacePaths.Relative(p), toolchain);
            }
            catch (PathRefusedException)
            {
                return true;
            }
        })).ToList();
}
