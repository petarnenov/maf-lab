using System.Text.Json;
using Maf.Lab.Plugins.Abstractions;

namespace Maf.Lab.Api.Agent.Writes;

/// <summary>
/// The write-confirmation flows of the installed plugins, by their write tool's name (the Strategy the core chooses by
/// tool). The core keeps no flow of its own: a write tool no installed plugin has a flow for cannot be confirmed. Two flows
/// for one tool: the last registered is used, as the container resolves a service.
/// </summary>
public sealed class WriteFlows(IEnumerable<IWriteConfirmationFlow> flows)
{
    private readonly Dictionary<string, IWriteConfirmationFlow> _byTool =
        flows.GroupBy(f => f.ToolName, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal);

    public IWriteConfirmationFlow? For(string toolName) => _byTool.GetValueOrDefault(toolName);

    /// <summary>The schema a tool's flow describes its summary with; none when no installed plugin has a flow for it.</summary>
    public JsonElement? SchemaFor(string toolName) => For(toolName)?.SummarySchema;
}
