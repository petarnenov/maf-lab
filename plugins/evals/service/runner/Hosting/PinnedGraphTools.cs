using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Maf.Lab.Api.Agent;
using Maf.Lab.Domain.Graph;
using Microsoft.Extensions.AI;

namespace Maf.Lab.Eval.Hosting;

/// <summary>A comparison fixes its MCP trace depth through the official argument, retaining the pinned schema.</summary>
internal sealed class PinnedGraphTools(IToolSource source, int depth) : IToolSource
{
    public async Task<ToolSet> GetToolsAsync(string bearerToken, ConfirmationSink? confirmations, CancellationToken ct,
        IReadOnlySet<string>? domains = null)
    {
        var tools = await source.GetToolsAsync(bearerToken, confirmations, ct, domains);
        var origins = tools.Tools.ToDictionary(t => t.Name, t => new ToolOrigin(tools.DomainOf(t.Name), tools.ServerOf(t.Name)));
        return new ToolSet([.. tools.Tools.Select(t => t is AIFunction f && f.Name == GraphTools.TraceCodeSymbol
            ? (AITool)new PinnedTrace(f, depth) : t)], tools, tools.Confirm, origins, tools.Unavailable);
    }

    internal sealed class PinnedTrace(AIFunction function, int depth) : AIFunction
    {
        public override string Name => function.Name;
        public override string Description => Regex.Replace(function.Description ?? "", @"up to \d+ calls?", $"up to {depth} call{(depth == 1 ? "" : "s")}");
        public override JsonElement JsonSchema
        {
            get
            {
                var schema = JsonNode.Parse(function.JsonSchema.GetRawText())!;
                schema["properties"]?.AsObject().Remove("depth");
                if (schema["required"] is JsonArray required)
                    for (var i = required.Count - 1; i >= 0; i--)
                        if (required[i]?.GetValue<string>() == "depth") required.RemoveAt(i);
                return JsonSerializer.SerializeToElement(schema);
            }
        }

        protected override ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken ct)
        {
            var pinned = new AIFunctionArguments(arguments) { ["depth"] = depth };
            return function.InvokeAsync(pinned, ct);
        }
    }
}
