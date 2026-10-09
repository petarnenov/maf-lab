using Maf.Lab.Api.Plugins;
using Maf.Lab.Plugins.Abstractions;
using Microsoft.Extensions.Options;

namespace Maf.Lab.Api.Agent;

/// <summary>
/// The domains in use, as data (introduce-plugins decision 6): every domain the deployment has, read the same way, none
/// first, default or special (decision 5g). Built from every installed plugin's <c>[domain]</c> table: since
/// extract-portfolio no domain is built in, so outside any scope there is none. Rebuilt whenever the installed set the
/// plugin catalogue last read changes, so a remote domain plugin applies from the next turn with no restart.
/// </summary>
public sealed class DomainCatalogue
{
    private static readonly AsyncLocal<DomainCatalogue?> Ambient = new();

    /// <summary>No domain: what <see cref="Current"/> answers outside any scope, since every domain is a plugin's.</summary>
    public static DomainCatalogue Empty { get; } = Of([]);

    /// <summary>
    /// The catalogue of the request or turn being served, for the static facades only (<see cref="Domains"/>,
    /// <c>DataToolRouter</c>, <c>DataCards</c>, <c>SystemPrompt</c>, <c>OutOfScope</c>, the classifier's and the turn
    /// runner's static helpers): a frozen view of the host's, which a middleware puts in scope for every request and
    /// the turn runner for every turn (<see cref="Use"/>, an Ambient Context like <c>Activity.Current</c>). Anything
    /// built by DI takes the catalogue by its constructor instead. Ambient rather than global, so two hosts in one process
    /// (tests) never see each other's.
    /// </summary>
    public static DomainCatalogue Current => Ambient.Value ?? Empty;

    /// <summary>Puts a catalogue in scope for the current async flow, until the returned handle is disposed.</summary>
    public static IDisposable Use(DomainCatalogue catalogue)
    {
        var previous = Ambient.Value;
        Ambient.Value = catalogue;
        return new Restore(previous);
    }

    private sealed class Restore(DomainCatalogue? previous) : IDisposable
    {
        public void Dispose() => Ambient.Value = previous;
    }

    private DomainCatalogue? _owner;
    private readonly IReadOnlyList<DomainDescriptor> _fixed;
    private readonly IReadOnlyList<IDomainBehaviour> _behaviours;
    private readonly PluginCatalogue? _plugins;
    private readonly string? _pluginsRoot;
    private readonly object _gate = new();
    private (PluginSet? Set, Snapshot Snapshot)? _cached;

    /// <summary>The installed plugins' domains, with their behaviours, rebuilt when the installed set changes.</summary>
    public DomainCatalogue(IEnumerable<IDomainBehaviour> pluginBehaviours, PluginCatalogue? plugins = null, IOptions<PluginOptions>? pluginOptions = null)
    {
        _fixed = [];
        _behaviours = [.. pluginBehaviours];
        _plugins = plugins;
        _pluginsRoot = pluginOptions?.Value.Root;
    }

    /// <summary>A catalogue of exactly these descriptors and behaviours: for tests and for hosts with no plugins.</summary>
    public static DomainCatalogue Of(IEnumerable<DomainDescriptor> descriptors, IEnumerable<IDomainBehaviour>? behaviours = null) =>
        new(descriptors, behaviours ?? []);

    private DomainCatalogue(IEnumerable<DomainDescriptor> descriptors, IEnumerable<IDomainBehaviour> behaviours)
    {
        _fixed = [.. descriptors];
        _behaviours = [.. behaviours];
    }

    /// <summary>
    /// A view of the domains in use now that never changes: what a request or a turn reads from start to end, so a
    /// plugin switched on or off mid-turn applies from the next one (the lifecycle's per-turn snapshot).
    /// </summary>
    public DomainCatalogue Freeze() => _plugins is null ? this : Of(All, _behaviours);

    /// <summary>The caller's domains; injected host readers and static readers see the same frozen scope.</summary>
    public DomainCatalogue For(PluginAccessSnapshot access)
    {
        if (_plugins is null) return Freeze();
        var owners = _plugins.Current.Plugins.Where(p => access.IsInUse(p.Name) && p.Manifest.Domain is not null)
            .Select(p => p.Manifest.Domain!.Id).ToHashSet(StringComparer.Ordinal);
        return Of(ReadAll().Domains.Where(d => owners.Contains(d.Id)), _behaviours).WithOwner(this);
    }

    private DomainCatalogue WithOwner(DomainCatalogue owner) { _owner = owner; return this; }

    /// <summary>Every domain in use, in order.</summary>
    public IReadOnlyList<DomainDescriptor> All => Read().Domains;

    /// <summary>The domains' ids, in order.</summary>
    public IReadOnlyList<string> Ids => Read().Ids;

    public DomainDescriptor? Get(string id) => Read().ById.GetValueOrDefault(id);

    /// <summary>The behaviour of a domain in use; null when it has none.</summary>
    public IDomainBehaviour? Behaviour(string? domain) =>
        domain is not null && Read().ById.ContainsKey(domain) ? _behaviours.LastOrDefault(b => b.Domain == domain) : null;

    /// <summary>The behaviours of the domains in use, in the domains' order.</summary>
    public IEnumerable<IDomainBehaviour> Behaviours => Ids.Select(Behaviour).OfType<IDomainBehaviour>();

    /// <summary>The one behaviour that owns the conversation's focus, if a domain in use has one.</summary>
    public IDomainBehaviour? FocusOwner => Behaviours.FirstOrDefault(b => b.OwnsFocus);

    /// <summary>The domain that names a tool (its search, a read, a write, a graph tool or its allow-list); null for none.</summary>
    public string? OfTool(string tool) => Read().ToolDomain.GetValueOrDefault(tool);

    /// <summary>Each domain's documentation search, by domain.</summary>
    public IReadOnlyDictionary<string, string> SearchTools => Read().SearchTools;

    /// <summary>The graph tools, by the domain whose server offers them.</summary>
    public IReadOnlyDictionary<string, string> GraphTools => Read().GraphTools;

    /// <summary>A domain's order: its place in traces and in the assembled prompt.</summary>
    public int Order(string domain) => Read().Ids is var ids && ids.ToList().IndexOf(domain) is var i && i >= 0 ? i : int.MaxValue;

    private Snapshot Read() => Ambient.Value is { } active && ReferenceEquals(active._owner, this)
        ? active.ReadAll() : ReadAll();

    private Snapshot ReadAll()
    {
        var set = _plugins?.Current;
        lock (_gate)
        {
            if (_cached is { } c && ReferenceEquals(c.Set, set))
            {
                return c.Snapshot;
            }
            var byId = new Dictionary<string, DomainDescriptor>(StringComparer.Ordinal);
            foreach (var d in _fixed)
            {
                byId[d.Id] = d;
            }
            foreach (var plugin in set?.Plugins ?? [])
            {
                if (plugin.Manifest.Domain is not { Id.Length: > 0 } table)
                {
                    continue;
                }
                var prompt = table.Prompt is { Length: > 0 } p && _pluginsRoot is not null
                    && File.Exists(Path.Combine(_pluginsRoot, plugin.Name, p)) ? File.ReadAllText(Path.Combine(_pluginsRoot, plugin.Name, p)) : null;
                byId[table.Id] = table.ToDescriptor(prompt);
            }
            var snapshot = new Snapshot([.. byId.Values.OrderBy(d => d.Order).ThenBy(d => d.Id, StringComparer.Ordinal)]);
            _cached = (set, snapshot);
            return snapshot;
        }
    }

    private sealed class Snapshot
    {
        public Snapshot(IReadOnlyList<DomainDescriptor> domains)
        {
            Domains = domains;
            Ids = [.. domains.Select(d => d.Id)];
            ById = domains.ToDictionary(d => d.Id, StringComparer.Ordinal);
            SearchTools = domains.Where(d => d.SearchTool is not null).ToDictionary(d => d.Id, d => d.SearchTool!, StringComparer.Ordinal);
            GraphTools = domains.SelectMany(d => d.GraphTools.Select(t => KeyValuePair.Create(t, d.Id)))
                .DistinctBy(kv => kv.Key).ToDictionary(StringComparer.Ordinal);
            var tools = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var d in domains)
            {
                foreach (var t in d.NamedTools)
                {
                    tools.TryAdd(t, d.Id);
                }
            }
            ToolDomain = tools;
        }

        public IReadOnlyList<DomainDescriptor> Domains { get; }
        public IReadOnlyList<string> Ids { get; }
        public IReadOnlyDictionary<string, DomainDescriptor> ById { get; }
        public IReadOnlyDictionary<string, string> SearchTools { get; }
        public IReadOnlyDictionary<string, string> GraphTools { get; }
        public IReadOnlyDictionary<string, string> ToolDomain { get; }
    }
}
