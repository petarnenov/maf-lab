using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;
using Maf.Lab.Domain.Tracing;
using Microsoft.Extensions.AI;

namespace Maf.Lab.Api.Agent.Tracing;

/// <summary>
/// Records every real model call of a turn: the full request (messages, tools, tool mode, options) and the response
/// (text, tool calls, finish reason, token usage, latency). Sits directly above the provider client, so calls issued
/// on the model's behalf by <see cref="RequiredToolModeChatClient"/> are not counted as model calls.
/// </summary>
public sealed class TracingChatClient(IChatClient inner, TurnTrace trace, Action? beforeResponse = null) : DelegatingChatClient(inner)
{
    private int _iteration;

    public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var list = messages.ToList();
        var iteration = Request(list, options);
        var sw = Stopwatch.StartNew();
        var response = await base.GetResponseAsync(list, options, cancellationToken);
        Response(iteration, options, response.Text, response.Messages.SelectMany(m => m.Contents).OfType<FunctionCallContent>(),
            response.FinishReason, response.Usage, sw.ElapsedMilliseconds, response.ModelId);
        return response;
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var list = messages.ToList();
        var iteration = Request(list, options);
        var sw = Stopwatch.StartNew();
        var text = new StringBuilder();
        var calls = new List<FunctionCallContent>();
        ChatFinishReason? finish = null;
        UsageDetails? usage = null;
        string? model = null;

        await foreach (var update in base.GetStreamingResponseAsync(list, options, cancellationToken))
        {
            text.Append(update.Text);
            calls.AddRange(update.Contents.OfType<FunctionCallContent>());
            foreach (var u in update.Contents.OfType<UsageContent>())
            {
                usage = u.Details;
            }
            finish = update.FinishReason ?? finish;
            model = update.ModelId ?? model;
            yield return update;
        }
        Response(iteration, options, text.ToString(), calls, finish, usage, sw.ElapsedMilliseconds, model);
    }

    private int Request(IReadOnlyList<ChatMessage> messages, ChatOptions? options)
    {
        var iteration = Interlocked.Increment(ref _iteration);
        var meta = InnerClient.GetService<ChatClientMetadata>();
        trace.Add(TraceKinds.ModelRequest, $"Model call #{iteration} → {options?.ModelId ?? meta?.DefaultModelId ?? "model"}", new JsonObject
        {
            ["iteration"] = iteration,
            ["model"] = options?.ModelId ?? meta?.DefaultModelId,
            ["endpoint"] = meta?.ProviderUri?.Host,
            ["toolMode"] = TraceMapping.ToolMode(options?.ToolMode),
            ["temperature"] = options?.Temperature,
            ["think"] = options?.RawRepresentationFactory is null ? null : false,
            ["tools"] = new JsonArray((options?.Tools ?? []).Select(t => (JsonNode)JsonValue.Create(t.Name)!).ToArray()),
            ["messages"] = TraceMapping.Messages(messages),
        });
        return iteration;
    }

    private void Response(int iteration, ChatOptions? options, string text, IEnumerable<FunctionCallContent> calls, ChatFinishReason? finish,
        UsageDetails? usage, long latencyMs, string? model)
    {
        // Streams are pulled: every update yielded above has already reached the turn runner, so the answer chunks
        // recorded now are exactly the text delivered before this response ended.
        beforeResponse?.Invoke();
        var toolCalls = calls.ToList();
        var title = toolCalls.Count > 0
            ? $"Model #{iteration} asked for {string.Join(", ", toolCalls.Select(c => c.Name))}"
            : $"Model #{iteration} answered ({text.Length} chars)";
        trace.Add(TraceKinds.ModelResponse, title, new JsonObject
        {
            ["iteration"] = iteration,
            ["model"] = model ?? options?.ModelId,
            ["text"] = text,
            ["toolCalls"] = new JsonArray(toolCalls.Select(c => (JsonNode)new JsonObject
            {
                ["callId"] = c.CallId, ["name"] = c.Name, ["arguments"] = TraceMapping.Node(c.Arguments),
            }).ToArray()),
            ["finishReason"] = finish?.Value,
            ["usage"] = usage is null ? null : new JsonObject
            {
                ["inputTokens"] = usage.InputTokenCount, ["outputTokens"] = usage.OutputTokenCount, ["totalTokens"] = usage.TotalTokenCount,
            },
            ["latencyMs"] = latencyMs,
        }, latencyMs);
    }
}
