using System.Reflection;
using Maf.Lab.Plugins.Abstractions;

namespace Maf.Lab.Api.Plugins;

/// <summary>The in-process plugins this process composed: each one's instance, in the installed order.</summary>
public sealed record LoadedPlugins(IReadOnlyList<IMafPlugin> Plugins);

/// <summary>
/// The composition root's plugin step (introduce-plugins decision 5): find every <see cref="IMafPlugin"/> compiled in,
/// keep those installed (plugins/.installed, in its dependency order), refuse any the deployment's environment does not
/// allow, and apply only the <c>IContributes*</c> interfaces each one implements. A plugin that is not installed
/// registers nothing, maps nothing and creates no table: its code is in the image but inert.
/// </summary>
public static class PluginHost
{
    /// <summary>The prefix every in-process plugin assembly carries; the abstractions themselves are not a plugin.</summary>
    public const string AssemblyPrefix = "Maf.Lab.Plugins.";

    public static WebApplicationBuilder AddMafPlugins(this WebApplicationBuilder builder, IEnumerable<Assembly>? extraAssemblies = null)
    {
        builder.Services.Configure<PluginOptions>(builder.Configuration.GetSection(PluginOptions.Section));
        builder.Services.AddSingleton<PluginCatalogue>();
        builder.Services.AddSingleton<IInstalledPlugins, InstalledPlugins>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<PluginCatalogue>());

        // Read now, for composition: what is installed at start is what this process registers. A later change to an
        // in-process plugin needs a restart (scripts/api_restart.sh); a remote plugin's needs none.
        var installed = ReadInstalled(builder.Configuration);

        var environment = builder.Configuration["MAF_ENV"] is { Length: > 0 } env ? env : "dev";
        RefuseDisallowed(installed, environment);

        // A host may name further assemblies to search (tests put their fixture plugins here); only this lab's own.
        var named = builder.Configuration.GetSection("Plugins:ExtraAssemblies").Get<string[]>() ?? [];
        var types = Discover((extraAssemblies ?? []).Concat(named
            .Where(n => n.StartsWith("Maf.Lab.", StringComparison.Ordinal))
            .Select(n => Assembly.Load(new AssemblyName(n)))));
        var loaded = new List<IMafPlugin>();
        foreach (var plugin in installed.Plugins)
        {
            if (!types.TryGetValue(plugin.Name, out var type))
            {
                if (plugin.HasServer)
                {
                    throw new InvalidOperationException(
                        $"plugin '{plugin.Name}' is installed with a server part, but this image has no code for it (is this the product variant?)");
                }
                continue;
            }
            loaded.Add((IMafPlugin)Activator.CreateInstance(type)!);
        }
        foreach (var plugin in loaded.OfType<IContributesServices>())
        {
            plugin.ConfigureServices(builder.Services, builder.Configuration);
        }
        foreach (var plugin in loaded.OfType<IContributesDomainBehaviour>())
        {
            // Read by the domain catalogue with each domain's descriptor (decision 6).
            builder.Services.AddSingleton(plugin.Behaviour);
        }
        foreach (var plugin in loaded.OfType<IContributesTurnObserver>())
        {
            // Composed into the core's TurnObservers (decision 7): the core registers no observer of its own.
            builder.Services.AddSingleton<ITurnObserver>(sp => plugin.CreateObserver(sp));
        }
        foreach (var plugin in loaded.OfType<IContributesWriteConfirmation>())
        {
            // A flow per write tool, chosen by the core's WriteFlows (generalize-write-confirmation). Scoped: its ports act
            // for the request that hands it a proposal.
            builder.Services.AddScoped<IWriteConfirmationFlow>(sp => plugin.CreateFlow(sp));
        }
        foreach (var plugin in loaded.OfType<IContributesOpenWork>())
        {
            builder.Services.AddSingleton(sp => new NamedOpenWork(((IMafPlugin)plugin).Name, plugin.CreateOpenWork(sp)));
        }
        foreach (var plugin in loaded.OfType<IContributesDataLifecycle>())
        {
            // Disabled plugins still own stored data: lifecycle follows installation, never enablement.
            builder.Services.AddSingleton(sp => new NamedDataLifecycle(((IMafPlugin)plugin).Name, plugin.CreateDataLifecycle(sp)));
        }
        builder.Services.AddSingleton(new LoadedPlugins(loaded));
        return builder;
    }

    /// <summary>
    /// The domain behaviours of the installed plugins whose code is in this process, for a host that composes no web app
    /// (the eval's agent host): the same plugins the api would load, contributing only their behaviour.
    /// </summary>
    public static IReadOnlyList<IDomainBehaviour> InstalledBehaviours(PluginSet installed)
    {
        var types = Discover([]);
        return [.. installed.Plugins
            .Select(p => types.GetValueOrDefault(p.Name))
            .OfType<Type>()
            .Select(t => Activator.CreateInstance(t))
            .OfType<IContributesDomainBehaviour>()
            .Select(p => p.Behaviour)];
    }

    /// <summary>The installed set as composition reads it: <c>plugins/.installed</c> under the configured root, once.</summary>
    public static PluginSet ReadInstalled(IConfiguration configuration)
    {
        var options = new PluginOptions();
        configuration.GetSection(PluginOptions.Section).Bind(options);
        var file = Path.Combine(options.Root, ".installed");
        return File.Exists(file) ? PluginCatalogue.Parse(File.ReadAllText(file)) : PluginSet.Empty;
    }

    /// <summary>
    /// The services of the installed plugins whose code is in this process, for a host that composes no web app (the
    /// eval's agent host): what each registers through <see cref="IContributesServices"/>, the same as for the api.
    /// </summary>
    public static void InstalledServices(PluginSet installed, IServiceCollection services, IConfiguration configuration)
    {
        var types = Discover([]);
        foreach (var plugin in installed.Plugins
            .Select(p => types.GetValueOrDefault(p.Name))
            .OfType<Type>()
            .Select(t => Activator.CreateInstance(t))
            .OfType<IContributesServices>())
        {
            plugin.ConfigureServices(services, configuration);
        }
    }

    /// <summary>
    /// The write-confirmation flows of the installed plugins whose code is in this process, for a host that composes no web
    /// app (the eval's agent host), each created over that host's services.
    /// </summary>
    public static IReadOnlyList<IWriteConfirmationFlow> InstalledWriteFlows(PluginSet installed, IServiceProvider services)
    {
        var types = Discover([]);
        return [.. installed.Plugins
            .Select(p => types.GetValueOrDefault(p.Name))
            .OfType<Type>()
            .Select(t => Activator.CreateInstance(t))
            .OfType<IContributesWriteConfirmation>()
            .Select(p => p.CreateFlow(services))];
    }

    /// <summary>A plugin installed in an environment its manifest does not allow stops the start, naming it (task 3.8).</summary>
    public static void RefuseDisallowed(PluginSet installed, string environment)
    {
        foreach (var plugin in installed.Plugins)
        {
            if (!plugin.Manifest.Environments.Contains(environment))
            {
                throw new InvalidOperationException(
                    $"plugin '{plugin.Name}' is not allowed in MAF_ENV={environment} (it allows {string.Join(", ", plugin.Manifest.Environments)})");
            }
        }
    }

    /// <summary>Plugin types by name: every assembly next to the api named Maf.Lab.Plugins.*, plus any a host adds (tests).</summary>
    public static IReadOnlyDictionary<string, Type> Discover(IEnumerable<Assembly> extraAssemblies)
    {
        var assemblies = new List<Assembly>(extraAssemblies);
        foreach (var file in Directory.EnumerateFiles(AppContext.BaseDirectory, AssemblyPrefix + "*.dll"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (name == typeof(IMafPlugin).Assembly.GetName().Name)
            {
                continue;
            }
            assemblies.Add(Assembly.Load(new AssemblyName(name)));
        }
        var found = new Dictionary<string, Type>(StringComparer.Ordinal);
        foreach (var type in assemblies.Distinct().SelectMany(a => a.GetTypes()))
        {
            if (type is { IsAbstract: false, IsInterface: false } && typeof(IMafPlugin).IsAssignableFrom(type) && type.GetConstructor(Type.EmptyTypes) is not null)
            {
                var name = ((IMafPlugin)Activator.CreateInstance(type)!).Name;
                if (!found.TryAdd(name, type))
                {
                    throw new InvalidOperationException($"two types claim plugin '{name}': {found[name].FullName} and {type.FullName}");
                }
            }
        }
        return found;
    }

    /// <summary>
    /// Maps each loaded plugin's routes in its own group behind the core's gate: while the plugin is not in the installed
    /// set the catalogue last read, every route of it answers 404, whatever the plugin itself does (decision 2).
    /// </summary>
    public static WebApplication MapMafPlugins(this WebApplication app)
    {
        var catalogue = app.Services.GetRequiredService<PluginCatalogue>();
        foreach (var plugin in app.Services.GetRequiredService<LoadedPlugins>().Plugins)
        {
            if (plugin is not IContributesEndpoints endpoints)
            {
                continue;
            }
            var name = plugin.Name;
            var group = app.MapGroup("");
            group.WithMetadata(new PluginRouteMetadata(name));
            group.AddEndpointFilter(async (context, next) =>
            {
                var manifest = catalogue.Current.Plugins.FirstOrDefault(p => p.Name == name)?.Manifest;
                if (manifest is null) return Results.NotFound();
                var protocolAccess = context.HttpContext.RequestServices.GetServices<IPluginRouteAccess>()
                    .FirstOrDefault(candidate => candidate.Plugin == name);
                if (protocolAccess is not null) return await protocolAccess.InvokeAsync(context, next);
                var inUse = manifest.Scope == PluginScopes.Installation
                    || PluginAccessContext.Current?.IsInUse(name) == true
                    || (manifest.Public && context.HttpContext.User.Identity?.IsAuthenticated != true);
                return inUse ? await next(context) : Results.NotFound();
            });
            group.AddEndpointFilter<Compliance.OperatorContentFilter>();
            endpoints.MapEndpoints(new Agent.AGUI.PluginEndpoints(group));
        }
        return app;
    }
}

/// <summary>Marks every endpoint a plugin mapped, so the contract suite can tell its routes from the core's.</summary>
public sealed record PluginRouteMetadata(string Plugin);

/// <summary>A plugin's open work, by its name, for `/api/plugins/{name}/open-work`.</summary>
public sealed record NamedOpenWork(string Plugin, IOpenWork Work);

/// <summary>One installed store's lifecycle. A null plugin identifies the core, without reserving a plugin name.</summary>
public sealed record NamedDataLifecycle(string? Plugin, IDataLifecycle Lifecycle);
