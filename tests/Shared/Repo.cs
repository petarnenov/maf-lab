namespace Maf.Lab.TestSupport;

/// <summary>The repository the tests run from.</summary>
public static class Repo
{
    public static string Root()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "maf-lab.sln")))
            {
                return dir.FullName;
            }
        }
        throw new InvalidOperationException("repo root not found");
    }
}
