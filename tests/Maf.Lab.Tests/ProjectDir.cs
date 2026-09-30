namespace Maf.Lab.Tests;

/// <summary>
/// A project's source directory, the content root a service has in its container. Every referenced project copies its
/// own appsettings.json into this test output, and the last copy wins; a host started with <c>BuildApp</c> must read
/// its own, so tests pass it <c>--contentRoot</c> from here.
/// </summary>
public static class ProjectDir
{
    public static string Of(string project)
    {
        var dir = Path.Combine(CorpusLoaderTests.RepoRoot(), "src", project);
        return Directory.Exists(dir)
            ? dir
            : throw new DirectoryNotFoundException($"No project directory at {dir}.");
    }

    /// <summary>The arguments that make <c>Program.BuildApp</c> read the project's own configuration.</summary>
    public static string[] ContentRootArgs(string project) => ["--contentRoot", Of(project)];
}
