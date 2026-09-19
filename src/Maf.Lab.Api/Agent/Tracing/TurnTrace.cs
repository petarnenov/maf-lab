using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Maf.Lab.Domain.Chat;
using Maf.Lab.Domain.Tracing;

namespace Maf.Lab.Api.Agent.Tracing;

/// <summary>
/// Collects the ordered trace of one chat turn. Each event is streamed immediately as an SSE "trace" event and kept for
/// persistence at turn end. Strings longer than <see cref="MaxFieldChars"/> are cut; once the trace would exceed
/// <see cref="MaxTraceBytes"/>, later events keep kind and timing but lose their data. Both cases set Truncated.
/// </summary>
public sealed class TurnTrace(ChannelWriter<ChatEvent>? events)
{
    public const int MaxFieldChars = 20_000;
    public const int MaxTraceBytes = 1_000_000;
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly List<TraceEvent> _events = [];
    private readonly Lock _gate = new();
    private long _bytes;

    public IReadOnlyList<TraceEvent> Events
    {
        get { lock (_gate) { return _events.ToList(); } }
    }

    public long ElapsedMs => _clock.ElapsedMilliseconds;

    public TraceEvent Add(string kind, string title, object? data, long? durationMs = null)
    {
        var node = data as JsonNode ?? JsonSerializer.SerializeToNode(data, Json) ?? new JsonObject();
        var truncated = Cap(node);
        var element = JsonSerializer.SerializeToElement(node, Json);
        var size = element.GetRawText().Length;

        TraceEvent ev;
        lock (_gate)
        {
            if (_bytes + size > MaxTraceBytes)
            {
                element = JsonSerializer.SerializeToElement(new { truncated = true, reason = "trace size limit reached" }, Json);
                truncated = true;
                size = element.GetRawText().Length;
            }
            _bytes += size;
            ev = new TraceEvent(_events.Count + 1, _clock.ElapsedMilliseconds, kind, title, durationMs, element, truncated);
            _events.Add(ev);
        }
        events?.TryWrite(new TraceChatEvent(ev));
        return ev;
    }

    /// <summary>Cuts long strings in place; returns true when anything was cut.</summary>
    internal static bool Cap(JsonNode? node)
    {
        var cut = false;
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(p => p.Key).ToList())
                {
                    if (obj[key] is JsonValue v && v.TryGetValue<string>(out var s) && s.Length > MaxFieldChars)
                    {
                        obj[key] = s[..MaxFieldChars] + " …[truncated]";
                        cut = true;
                    }
                    else
                    {
                        cut |= Cap(obj[key]);
                    }
                }
                break;
            case JsonArray arr:
                for (var i = 0; i < arr.Count; i++)
                {
                    if (arr[i] is JsonValue v && v.TryGetValue<string>(out var s) && s.Length > MaxFieldChars)
                    {
                        arr[i] = s[..MaxFieldChars] + " …[truncated]";
                        cut = true;
                    }
                    else
                    {
                        cut |= Cap(arr[i]);
                    }
                }
                break;
        }
        return cut;
    }
}
