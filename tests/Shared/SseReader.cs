using System.Text.Json;

namespace Maf.Lab.TestSupport;

public sealed record SseEvent(string Name, JsonElement Data);

/// <summary>Minimal SSE frame reader for tests; invokes a callback as each event arrives.</summary>
public static class SseReader
{
    public static async Task<List<SseEvent>> ReadAllAsync(Stream stream, Action<SseEvent>? onEvent = null, CancellationToken ct = default)
    {
        var events = new List<SseEvent>();
        using var reader = new StreamReader(stream);
        string? name = null;
        var data = new List<string>();
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (line.Length == 0)
            {
                if (data.Count > 0)
                {
                    var payload = JsonDocument.Parse(string.Join("\n", data)).RootElement.Clone();
                    // An AG-UI event names itself in its payload; the official server leaves the frame's own name out.
                    var named = name ?? (payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("type", out var type)
                        && type.ValueKind == JsonValueKind.String ? type.GetString() : null);
                    var ev = new SseEvent(named ?? "message", payload);
                    events.Add(ev);
                    onEvent?.Invoke(ev);
                }
                name = null;
                data.Clear();
                continue;
            }
            if (line.StartsWith("event:"))
            {
                name = line[6..].Trim();
            }
            else if (line.StartsWith("data:"))
            {
                data.Add(line[5..].TrimStart());
            }
        }
        return events;
    }
}
