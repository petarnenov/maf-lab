using Microsoft.Extensions.Configuration;

namespace Maf.Lab.Retrieval.Billing;

/// <summary>Where the seed files are. Configuration wins; otherwise the repository, then the published seed folder.</summary>
internal static class SeedPaths
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
}
