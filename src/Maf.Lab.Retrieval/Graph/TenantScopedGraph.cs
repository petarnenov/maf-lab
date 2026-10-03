using System.Diagnostics;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Hosting;
using Microsoft.Extensions.Options;
using Neo4j.Driver;

namespace Maf.Lab.Retrieval.Graph;

/// <summary>What graph tools read through; implemented only by <see cref="TenantScopedGraph"/>.</summary>
public interface IGraphReader
{
    Task<TResult> ReadAsync<TResult>(Principal principal, GraphQuery<TResult> query, CancellationToken ct);
}

/// <summary>
/// THE graph read path. Every read runs one of the named queries in <see cref="GraphTemplates"/>, with
/// <c>$readable</c> bound here from the principal (its firm and shared) and nowhere else. There is deliberately no
/// tenant parameter and no way to pass query text.
/// </summary>
public sealed class TenantScopedGraph(IDriver driver, IOptions<GraphOptions> options) : IGraphReader
{
    internal const string ReadableParameter = "readable";
    internal const string LimitParameter = "limit";

    private readonly string _database = options.Value.Database;

    public async Task<TResult> ReadAsync<TResult>(Principal principal, GraphQuery<TResult> query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(query);
        var parameters = Parameters(principal, query);

        using var span = LabTelemetry.Source.StartActivity("graph.read");
        span?.SetTag("graph.query", query.Name);
        var started = Stopwatch.GetTimestamp();
        var outcome = "ok";
        var rowCount = 0;
        var truncated = false;
        Exception? failure = null;
        try
        {
            var result = await driver.ExecutableQuery(query.Cypher)
                .WithParameters(parameters)
                .WithConfig(new QueryConfig(RoutingControl.Readers, _database))
                .ExecuteAsync(ct);
            var rows = result.Result.Select(r => (IGraphRow)new RecordRow(r)).ToList();
            truncated = rows.Count > query.Limit;
            if (truncated)
            {
                rows.RemoveRange(query.Limit, rows.Count - query.Limit);
            }
            rowCount = rows.Count;
            span?.SetTag("graph.rows", rows.Count);
            span?.SetTag("graph.truncated", truncated);
            return query.Map(rows, truncated);
        }
        catch (Exception ex)
        {
            outcome = "error";
            failure = ex;
            span?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);
            throw;
        }
        finally
        {
            var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            span?.SetTag("graph.duration_ms", Math.Round(elapsed, 1));
            LabTelemetry.Instruments.GraphQueryDuration.Record(elapsed,
                new KeyValuePair<string, object?>("query", query.Name), new KeyValuePair<string, object?>("outcome", outcome));
            // The turn trace's graph event (add-graph-trace-event): the span's numbers and the tenants bound here, for
            // a call whose tool asked. The exception's type name only — a driver message can name the host.
            GraphReadLog.Current?.Add(
                new GraphReadRecord(query.Name, query.Limit, rowCount, truncated, Math.Round(elapsed, 1),
                    GraphReadLog.OutcomeOf(failure), failure?.GetType().Name),
                principal.ReadableTenants.Select(t => t.Value).ToList());
        }
    }

    /// <summary>The query's own arguments plus the two this path owns. A query may not name either of those.</summary>
    internal static Dictionary<string, object> Parameters<TResult>(Principal principal, GraphQuery<TResult> query)
    {
        var parameters = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var (name, value) in query.Arguments)
        {
            if (name is ReadableParameter or LimitParameter)
            {
                throw new InvalidOperationException($"Graph query '{query.Name}' may not set the reserved parameter '{name}'.");
            }
            parameters[name] = value!;
        }
        parameters[ReadableParameter] = principal.ReadableTenants.Select(t => t.Value).ToList();
        parameters[LimitParameter] = query.Limit + 1;
        return parameters;
    }

    private sealed class RecordRow(IRecord record) : IGraphRow
    {
        public object? this[string column] => record.TryGet<object>(column, out var value) ? value : null;
    }
}
