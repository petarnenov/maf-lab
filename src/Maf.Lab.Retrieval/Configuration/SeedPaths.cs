using Microsoft.Extensions.Configuration;

namespace Maf.Lab.Retrieval.Configuration;

/// <summary>Where the seed files are. Configuration wins; otherwise the repository, then the published seed folder.</summary>
public static class SeedPaths
{
    public static string Resolve(IConfiguration configuration, string configurationKey, string fileName)
    {
        if (configuration[configurationKey] is { Length: > 0 } configured)
        {
            return Path.GetFullPath(configured);
        }
        for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "compose", "seed", fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
        return Path.Combine(AppContext.BaseDirectory, "seed", fileName);
    }

    /// <summary>The seed file's text; a missing file fails naming where it looked and the key that says where it is.</summary>
    public static string Read(IConfiguration configuration, string configurationKey, string fileName)
    {
        var path = Resolve(configuration, configurationKey, fileName);
        return File.Exists(path)
            ? File.ReadAllText(path)
            : throw new FileNotFoundException($"The seed file {fileName} is not at {path}; set {configurationKey} to where it is.", path);
    }
}
