using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using Neo4j.Driver;

namespace Maf.Lab.Retrieval.Graph;

/// <summary>
/// One read the graph read path made: the template's name, its limit, what came back and how it went. Structure only —
/// never an argument value, a node property or an exception message (graph-store: graph logs carry structure only).
/// </summary>
public sealed record GraphReadRecord(string Query, int Limit, int Rows, bool Truncated, double DurationMs, string Outcome, string? ErrorType);

/// <summary>
/// The reads of one graph tool call, for the turn trace's <c>graph</c> event (add-graph-trace-event). A tool opens one
/// with <see cref="Begin"/> when the caller asked for diagnostics; <see cref="TenantScopedGraph"/> — and only it —
/// appends to the open one, from the same numbers its span carries. A tool can open, read and attach a log, never add
/// to it.
/// </summary>
public sealed class GraphReadLog : IDisposable
{
    /// <summary>The result <c>_meta</c> key the reads travel under, beside <c>maf-lab/instance</c>.</summary>
    public const string MetaKey = "maf-lab/graph";

    public const string Ok = "ok";
    public const string Unavailable = "unavailable";
    public const string Cancelled = "cancelled";
    public const string Error = "error";

    private static readonly AsyncLocal<GraphReadLog?> Ambient = new();

    private readonly GraphReadLog? _outer;
    private readonly List<GraphReadRecord> _reads = [];
    private readonly Lock _gate = new();
    private IReadOnlyList<string> _tenantScope = [];
    private bool _closed;

    private GraphReadLog(GraphReadLog? outer) => _outer = outer;

    /// <summary>The log of the call in progress, or null when nobody asked.</summary>
    public static GraphReadLog? Current => Ambient.Value;

    /// <summary>Opens a log for this call; disposing it closes it and restores whatever was open before.</summary>
    public static GraphReadLog Begin()
    {
        var log = new GraphReadLog(Ambient.Value);
        Ambient.Value = log;
        return log;
    }

    /// <summary><see cref="Begin"/> when <paramref name="requested"/>, otherwise nothing to record into.</summary>
    public static GraphReadLog? BeginIf(bool requested) => requested ? Begin() : null;

    public IReadOnlyList<GraphReadRecord> Reads
    {
        get
        {
            lock (_gate)
            {
                return [.. _reads];
            }
        }
    }

    /// <summary>The tenants the read path bound for the caller (its firm and shared).</summary>
    public IReadOnlyList<string> TenantScope
    {
        get
        {
            lock (_gate)
            {
                return _tenantScope;
            }
        }
    }

    /// <summary>Appended by the read path only.</summary>
    internal void Add(GraphReadRecord read, IReadOnlyList<string> tenantScope)
    {
        lock (_gate)
        {
            if (_closed)
            {
                return;
            }
            _reads.Add(read);
            _tenantScope = tenantScope;
        }
    }

    /// <summary>What travels in the result's <c>_meta</c>: <c>{ instance, tenantScope, reads }</c>.</summary>
    public JsonObject ToJson(string instance)
    {
        var reads = Reads;
        var array = new JsonArray();
        foreach (var r in reads)
        {
            array.Add(new JsonObject
            {
                ["query"] = r.Query,
                ["limit"] = r.Limit,
                ["rows"] = r.Rows,
                ["truncated"] = r.Truncated,
                ["durationMs"] = r.DurationMs,
                ["outcome"] = r.Outcome,
                ["errorType"] = r.ErrorType,
            });
        }
        return new JsonObject
        {
            ["instance"] = instance,
            ["tenantScope"] = new JsonArray([.. TenantScope.Select(t => (JsonNode?)JsonValue.Create(t))]),
            ["reads"] = array,
        };
    }

    /// <summary>
    /// Puts the call's reads in the result's <c>_meta</c>, success and error alike. A call that read nothing — no log,
    /// or one refused before it reached the graph — keeps its result untouched.
    /// </summary>
    public static CallToolResult Attach(CallToolResult result, GraphReadLog? log)
    {
        if (log is null || log.Reads.Count == 0)
        {
            return result;
        }
        var instance = Hosting.InstanceIdentity.Name;
        result.Meta ??= [];
        result.Meta[MetaKey] = log.ToJson(instance);
        result.Meta["maf-lab/instance"] = instance;
        return result;
    }

    /// <summary>
    /// How a read ended: <c>unavailable</c> for the failures the tools report as "temporarily unavailable" (the same set
    /// as <c>ToolErrors.ForException</c>), <c>cancelled</c>, <c>error</c> for anything else, <c>ok</c> without one.
    /// </summary>
    public static string OutcomeOf(Exception? exception) => exception switch
    {
        null => Ok,
        ServiceUnavailableException or SessionExpiredException or TransientException or SecurityException => Unavailable,
        OperationCanceledException => Cancelled,
        _ => Error,
    };

    public void Dispose()
    {
        lock (_gate)
        {
            _closed = true;
        }
        if (ReferenceEquals(Ambient.Value, this))
        {
            Ambient.Value = _outer;
        }
    }
}
