using System.Text.Json;
using Maf.Lab.Api.Agent.Tracing;
using Maf.Lab.Api.Storage;
using Maf.Lab.Domain.Tracing;
using Maf.Lab.Eval.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maf.Lab.Eval;

/// <summary>
/// One question through the production agent path, its trace printed as a timeline — the terminal's view of the
/// monitor. Made for watching a question cross from one domain into another (add-portfolio-domain): the domain verdict,
/// each call with its domain and server, and every boundary the turn crossed.
/// </summary>
public static class AskCommand
{
    /// <summary>The kinds worth a line; deltas and the per-iteration model payloads would drown the path.</summary>
    private static readonly HashSet<string> Shown =
    [
        TraceKinds.TurnStart, TraceKinds.Intent, TraceKinds.Domain, TraceKinds.Guardrail, TraceKinds.Prompt, TraceKinds.ToolForced,
        TraceKinds.ToolCall, TraceKinds.ToolResult, TraceKinds.Boundary, TraceKinds.ModelResponse, TraceKinds.Sources, TraceKinds.TurnEnd,
    ];

    public static async Task<int> RunAsync(EvalAgentHost host, string tenantId, string question, string? jsonPath, CancellationToken ct)
    {
        var turn = await host.AskAsync(tenantId, question, ct);
        // The whole trace, from the eval's own observer (the core record holds only its subset).
        var events = host.Services.GetRequiredService<EvalTraceCapture>().Of(turn.TurnId);

        Console.WriteLine($"Q ({tenantId}): {question}");
        foreach (var e in events.Where(e => Shown.Contains(e.Kind)))
        {
            var marker = e.Kind == TraceKinds.Boundary ? "  ⇢ " : "    ";
            Console.WriteLine($"{marker}#{e.Seq,-3} +{e.AtMs,6} ms  {e.Kind,-15} {e.Title}");
        }
        Console.WriteLine();
        Console.WriteLine(turn.Error ?? turn.Answer);
        if (jsonPath is { Length: > 0 })
        {
            await File.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(events, TurnTrace.Json), ct);
            Console.WriteLine($"\ntrace → {jsonPath}");
        }
        return turn.Error is null ? 0 : 1;
    }
}
