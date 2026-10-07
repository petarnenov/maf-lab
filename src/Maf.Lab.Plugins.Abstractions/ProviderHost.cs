using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Maf.Lab.Plugins.Abstractions;

/// <summary>
/// How a process takes its providers (introduce-provider-plugins 5t/5u): every host that holds provider code (the api,
/// the MCP servers, the indexer, the test agent, the eval and the test hosts) calls <see cref="AddInstalledProviders"/>,
/// which registers each installed <c>provider</c> plugin found next to it, and checks at start that exactly one decision
/// engine is installed. A provider lives in every such process, so it changes only with a restart of the stack (make up),
/// never by plugin-on/off.
/// </summary>
public static class ProviderHost
{
    private const string AssemblyPrefix = "Maf.Lab.Plugins.";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>The installed providers' manifests, read from <c>{root}/.installed</c>; none when the file is missing.</summary>
    public static IReadOnlyList<PluginManifest> Installed(string root)
    {
        var path = Path.Combine(root, ".installed");
        if (!File.Exists(path))
        {
            return [];
        }
        var document = JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? throw new FormatException(".installed is not an object");
        return [.. (document["plugins"] as JsonArray ?? [])
            .Select(entry => entry?["manifest"]?.Deserialize<PluginManifest>(Json))
            .OfType<PluginManifest>()
            .Where(m => m.Kind == PluginKinds.Provider)];
    }

    /// <summary>
    /// Registers every installed provider whose code is in this process, in <c>.installed</c>'s order, and validates the
    /// set on start: exactly one <c>decision-engine</c>. An installed provider with no code here stops the start, naming
    /// it, as an in-process plugin's missing server part does.
    /// </summary>
    public static IServiceCollection AddInstalledProviders(this IServiceCollection services, IConfiguration configuration,
        IEnumerable<Assembly>? extraAssemblies = null)
    {
        var root = configuration["Plugins:Root"] is { Length: > 0 } r ? r : "plugins";
        var installed = Installed(root);
        // A host may name further assemblies to search (tests put their fixture providers here); only this lab's own.
        var named = configuration.GetSection("Plugins:ExtraAssemblies").GetChildren().Select(c => c.Value)
            .Where(n => n is not null && n.StartsWith("Maf.Lab.", StringComparison.Ordinal))
            .Select(n => Assembly.Load(new AssemblyName(n!)));
        var types = Discover((extraAssemblies ?? []).Concat(named));
        foreach (var manifest in installed)
        {
            if (!types.TryGetValue(manifest.Name, out var type))
            {
                throw new InvalidOperationException(
                    $"provider '{manifest.Name}' is installed, but this process has no code for it (is this the product variant?)");
            }
            ((IContributesProvider)Activator.CreateInstance(type)!).ConfigureProvider(services, configuration);
        }
        services.AddOptions<InstalledProviders>()
            .Configure(o => o.Provides = [.. installed.Select(m => (m.Name, m.Provides ?? ""))])
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<InstalledProviders>, InstalledProvidersValidation>();
        return services;
    }

    /// <summary>Provider plugin types by name: every Maf.Lab.Plugins.* assembly next to this process, plus any a host adds (tests).</summary>
    public static IReadOnlyDictionary<string, Type> Discover(IEnumerable<Assembly> extraAssemblies)
    {
        var assemblies = new List<Assembly>(extraAssemblies);
        foreach (var file in Directory.EnumerateFiles(AppContext.BaseDirectory, AssemblyPrefix + "*.dll"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (name != typeof(IContributesProvider).Assembly.GetName().Name)
            {
                assemblies.Add(Assembly.Load(new AssemblyName(name)));
            }
        }
        var types = new Dictionary<string, Type>(StringComparer.Ordinal);
        foreach (var type in assemblies.Distinct().SelectMany(a => a.GetTypes())
                     .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IContributesProvider).IsAssignableFrom(t)
                         && typeof(IMafPlugin).IsAssignableFrom(t) && t.GetConstructor(Type.EmptyTypes) is not null))
        {
            types.TryAdd(((IMafPlugin)Activator.CreateInstance(type)!).Name, type);
        }
        return types;
    }
}

/// <summary>The installed providers, by name and what each provides, as the start-up check reads them.</summary>
public sealed class InstalledProviders
{
    public IReadOnlyList<(string Name, string Provides)> Provides { get; set; } = [];

    /// <summary>Why the set is not what the core needs, or null when it is: exactly one decision engine.</summary>
    public string? Problem
    {
        get
        {
            var engines = Provides.Where(p => p.Provides == ProviderKinds.DecisionEngine).Select(p => p.Name).ToList();
            return engines.Count switch
            {
                1 => null,
                0 => "no decision engine is installed: install exactly one provider with provides = \"decision-engine\" (MAF_CORE_PROVIDERS)",
                _ => $"more than one decision engine is installed ({string.Join(", ", engines)}): install exactly one",
            };
        }
    }
}

/// <summary>The start-up check (standard options validation), with the problem named in the failure.</summary>
internal sealed class InstalledProvidersValidation : IValidateOptions<InstalledProviders>
{
    public ValidateOptionsResult Validate(string? name, InstalledProviders options) =>
        options.Problem is { } problem ? ValidateOptionsResult.Fail(problem) : ValidateOptionsResult.Success;
}
