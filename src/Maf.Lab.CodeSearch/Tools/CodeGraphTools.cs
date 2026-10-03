using System.ComponentModel;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Maf.Lab.Domain.Code;
using Maf.Lab.Domain.Graph;
using Maf.Lab.Hosting;
using Maf.Lab.Retrieval.Auth;
using Maf.Lab.Retrieval.Graph;
using Maf.Lab.Retrieval.Tools;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Maf.Lab.CodeSearch.Tools;

[JsonConverter(typeof(JsonStringEnumConverter<CallDirection>))]
public enum CallDirection
{
    callers,
    callees,
}

/// <summary>
/// The code graph's tools: structure, not text. Who calls a method, what it calls, and which code and tests reach a
/// file. The code graph is shared like the codebase corpus; the tenant still comes from the principal.
/// </summary>
[McpServerToolType]
public sealed partial class CodeGraphTools(IGraphReader graph, IPrincipalAccessor principals, ILogger<CodeGraphTools> logger)
{
    public const int MaxTraceDepth = 3;
    /// <summary>How far change_impact follows callers: deep enough for a test that reaches the code through two helpers.</summary>
    public const int ImpactDepth = CallTrace.MaxDepth;

    public const string TraceDescription =
        "Traces the maf-lab code graph from a C# method or type: its callers (who calls it) or its callees (what it calls), " +
        "through up to 3 calls, each with file path and line range. Built from the compiler's view of the code, so a call " +
        "means the method that is actually invoked, not one with a similar name.\n" +
        "Use when: the user asks who calls something, what depends on a method, or what a method ends up calling, e.g. " +
        "'who calls TenantScopedSearch.QueryAsync'.\n" +
        "Do not use for: what code says or how it works — use search_codebase for the code itself and ask_codebase for an " +
        "explanation. For what a change to a file affects, use change_impact.\n" +
        "Pass 'Type.Member' (e.g. 'TenantScopedSearch.QueryAsync'), a type name, or a member name; an ambiguous name returns " +
        "the candidates to choose from.";

    public const string ImpactDescription =
        "Shows what a change to one C# file of the maf-lab repository can affect, from the code graph: the methods the file " +
        "declares, the methods that reach them through calls, and the tests among those, grouped by test file.\n" +
        "Use when: the user asks what a change to a file affects or which tests cover it, e.g. 'what tests cover " +
        "src/Maf.Lab.Retrieval/Store/TenantScopedSearch.cs' — also after search_codebase: a snippet that mentions a file " +
        "is not a test that exercises it, so only this tool answers which tests cover a file.\n" +
        "Do not use for: reading or explaining the code — use search_codebase or ask_codebase. For one method's callers, use " +
        "trace_code_symbol.\n" +
        "Pass the path from the repository root, e.g. 'src/Maf.Lab.Retrieval/Store/TenantScopedSearch.cs'.";

    [McpServerTool(Name = GraphTools.TraceCodeSymbol, Title = "Trace a code symbol", ReadOnly = true, Idempotent = true, Destructive = false,
        OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(CodeTrace))]
    [Description(TraceDescription)]
    public async Task<CallToolResult> TraceAsync(
        [Description("The symbol: 'Type.Member' (e.g. 'TenantScopedSearch.QueryAsync'), a type name, or a member name.")]
        string symbol,
        [Description("'callers' (default) for who calls it, 'callees' for what it calls.")]
        CallDirection? direction = null,
        [Description("How many calls to follow, 1-3 (default 2).")]
        int? depth = null,
        CancellationToken cancellationToken = default,
        RequestContext<CallToolRequestParams>? context = null)
    {
        // Asked for diagnostics: the read path records each read for the turn trace's graph event, structure only.
        using var reads = GraphReadLog.BeginIf(SearchDocumentsTool.TraceRequested(context));
        return GraphReadLog.Attach(await TraceCoreAsync(symbol, direction, depth, cancellationToken), reads);
    }

    private async Task<CallToolResult> TraceCoreAsync(string? symbol, CallDirection? direction, int? depth, CancellationToken cancellationToken)
    {
        var name = symbol?.Trim() ?? "";
        if (name.Length is 0 or > 300)
        {
            return ToolErrors.Error("symbol is required: pass 'Type.Member', a type name or a member name.");
        }
        var hops = depth ?? 2;
        if (hops is < 1 or > MaxTraceDepth)
        {
            return ToolErrors.Error($"depth must be between 1 and {MaxTraceDepth}.");
        }
        var dir = direction ?? CallDirection.callers;

        using var span = LabTelemetry.Source.StartActivity("mcp.tool");
        span?.SetTag("tool.name", GraphTools.TraceCodeSymbol);
        try
        {
            var principal = principals.Current;
            var candidates = await graph.ReadAsync(principal, new SymbolCandidates(name), cancellationToken);
            if (candidates.Count == 0)
            {
                return SearchDocumentsTool.Structured(new CodeTrace(name, dir.ToString(), hops, [], [], [], false,
                    $"No method or type named '{name}' is in the code graph. Use {CodeTools.Search} to find it by text."));
            }
            var types = candidates.GroupBy(c => c.TypeFullName).ToList();
            if (types.Count > 1)
            {
                return SearchDocumentsTool.Structured(new CodeTrace(name, dir.ToString(), hops, [],
                    [.. types.Select(t => Symbol(t.First()))], [], candidates.Count >= new SymbolCandidates(name).Limit,
                    $"'{name}' matches {types.Count} types. Trace again with one of the candidates' 'Type.Member' names."));
            }
            var matched = candidates;
            var trace = await graph.ReadAsync(principal, new CallTrace([.. matched.Select(m => m.Key)],
                dir == CallDirection.callers ? TraceDirection.Callers : TraceDirection.Callees, hops), cancellationToken);
            span?.SetTag("graph.reached", trace.Hits.Count);
            return SearchDocumentsTool.Structured(new CodeTrace(name, dir.ToString(), hops, [.. matched.Select(Symbol)], [], trace.Hits,
                trace.Truncated, trace.Hits.Count == 0 ? $"Nothing in the repository {(dir == CallDirection.callers ? "calls" : "is called by")} it." : null));
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogError("{Tool} failed: {ErrorType}", GraphTools.TraceCodeSymbol, ex.GetType().Name);
            return ToolErrors.Error(ToolErrors.ForException(ex, "Graph lookup"));
        }
    }

    [McpServerTool(Name = GraphTools.ChangeImpact, Title = "Impact of changing a file", ReadOnly = true, Idempotent = true, Destructive = false,
        OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ChangeImpact))]
    [Description(ImpactDescription)]
    public async Task<CallToolResult> ImpactAsync(
        [Description("The C# file's path from the repository root, e.g. 'src/Maf.Lab.Retrieval/Store/TenantScopedSearch.cs'.")]
        string path,
        CancellationToken cancellationToken = default,
        RequestContext<CallToolRequestParams>? context = null)
    {
        using var reads = GraphReadLog.BeginIf(SearchDocumentsTool.TraceRequested(context));
        return GraphReadLog.Attach(await ImpactCoreAsync(path, cancellationToken), reads);
    }

    private async Task<CallToolResult> ImpactCoreAsync(string? path, CancellationToken cancellationToken)
    {
        if (NormalizePath(path) is not { } file)
        {
            return ToolErrors.Error("path must be a file path from the repository root, e.g. 'src/Maf.Lab.Retrieval/Store/TenantScopedSearch.cs', without '..'.");
        }

        using var span = LabTelemetry.Source.StartActivity("mcp.tool");
        span?.SetTag("tool.name", GraphTools.ChangeImpact);
        try
        {
            var principal = principals.Current;
            var declared = await graph.ReadAsync(principal, new FileMethods(file), cancellationToken);
            if (!declared.FileFound)
            {
                return ToolErrors.Error($"'{file}' is not a C# file of the code graph. Check the path, or use {CodeTools.Search} to find the file.");
            }
            if (declared.Methods.Count == 0)
            {
                return SearchDocumentsTool.Structured(new ChangeImpact(file, [], [], [], declared.Truncated));
            }
            var trace = await graph.ReadAsync(principal,
                new CallTrace([.. declared.Methods.Select(m => m.Key)], TraceDirection.Callers, ImpactDepth, limit: 300), cancellationToken);
            var tests = trace.Hits.Where(h => h.IsTest)
                .GroupBy(h => h.Path)
                .OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => new TestFileImpact(g.Key, [.. g]))
                .ToList();
            span?.SetTag("graph.reached", trace.Hits.Count);
            return SearchDocumentsTool.Structured(new ChangeImpact(file, [.. declared.Methods.Select(Symbol)],
                [.. trace.Hits.Where(h => !h.IsTest)], tests, declared.Truncated || trace.Truncated));
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogError("{Tool} failed: {ErrorType}", GraphTools.ChangeImpact, ex.GetType().Name);
            return ToolErrors.Error(ToolErrors.ForException(ex, "Graph lookup"));
        }
    }

    /// <summary>A repository-relative path with forward slashes, or null for an absolute path or one that leaves the repository.</summary>
    internal static string? NormalizePath(string? path)
    {
        var p = path?.Trim().Replace('\\', '/') ?? "";
        while (p.StartsWith("./", StringComparison.Ordinal))
        {
            p = p[2..];
        }
        return p.Length is > 0 and <= 400 && RepositoryPath().IsMatch(p) ? p : null;
    }

    private static CodeSymbol Symbol(SymbolCandidate c) => new(c.Symbol, c.Path, c.StartLine, c.EndLine);

    /// <summary>Relative (no leading slash or drive), no '..' segment, ordinary path characters only.</summary>
    [GeneratedRegex(@"^(?![A-Za-z]:)(?!/)(?!(?:.*/)?\.\.(?:/|$))[A-Za-z0-9_.\-/]+$")]
    private static partial Regex RepositoryPath();
}
