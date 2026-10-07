namespace Maf.Lab.Plugins.Abstractions;

/// <summary>
/// Where a trace can be opened (extract-observability-plugin): the link a turn's start carries, so a person reading the
/// turn can open its whole trace. The plugin that keeps the traces implements it; the core holds <see cref="NoTraceLink"/>
/// until one does, so a turn offers a link only when something can open it.
/// </summary>
public interface ITraceLink
{
    /// <summary>The trace's address, or null when there is nowhere to open it.</summary>
    string? UrlFor(string traceId);
}

/// <summary>No trace store (Null Object): every turn goes without a link.</summary>
public sealed class NoTraceLink : ITraceLink
{
    public string? UrlFor(string traceId) => null;
}
