using System.Reflection;
using System.Text.Json;
using Maf.Lab.Plugins.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Maf.Lab.Indexing.Graph;

/// <summary>Installed graph builders, discovered from the generic contribution library glob.</summary>
internal static class GraphContributionHost
{
    public static void Add(IServiceCollection services, IConfiguration configuration)
    {
        var path = Path.Combine(configuration["Plugins:Root"] ?? "plugins", ".installed");
        if (!File.Exists(path)) return;
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var installed = document.RootElement.GetProperty("plugins").EnumerateArray()
            .Select(p => p.GetProperty("manifest").GetProperty("name").GetString()).ToHashSet(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(AppContext.BaseDirectory, "Maf.Lab.Plugins.*.dll"))
        {
            var assembly = Assembly.LoadFrom(file);
            foreach (var type in assembly.GetTypes().Where(t => !t.IsAbstract && typeof(IGraphBuildContribution).IsAssignableFrom(t)))
            {
                var contribution = (IGraphBuildContribution)Activator.CreateInstance(type)!;
                if (installed.Contains(contribution.Plugin)) services.TryAddEnumerable(ServiceDescriptor.Singleton(typeof(IGraphBuildContribution), type));
            }
        }
    }
}
