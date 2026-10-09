using System.Text;
using Maf.Lab.TestGen;

namespace Maf.Lab.TestAgent;

/// <summary>
/// Tells the api what the agent is doing, as it does it: phases, tool calls, attempt results, and the model's text and
/// reasoning in chunks. Reporting never slows or fails the run — an entry that cannot be sent is counted and dropped,
/// and later entries still go. What it sends is content, so none of it is logged or traced here.
/// </summary>
public sealed class ActivityReporter(
    Func<IReadOnlyList<TestGenActivity>, CancellationToken, Task> send,
    TimeProvider time,
    ILogger logger,
    long startAfter = 0)
{
    /// <summary>A chunk goes once it holds this much text, or once it is this old.</summary>
    public const int ChunkBytes = 1024;
    public static readonly TimeSpan ChunkAge = TimeSpan.FromSeconds(2);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, TextStream> _streams = new()
    {
        [ActivityType.Text] = new TextStream(),
        [ActivityType.Reasoning] = new TextStream(),
    };
    // A task taken over after a restart numbers on from the last entry recorded before it.
    private long _seq = startAfter;

    /// <summary>The attempt entries are recorded under.</summary>
    public int Attempt { get; set; }

    /// <summary>The last sequence number handed out.</summary>
    public long Seq => Interlocked.Read(ref _seq);

    /// <summary>Entries that could not be sent.</summary>
    public int Failures { get; private set; }

    public Task PhaseAsync(string phase, CancellationToken ct) =>
        EntryAsync(seq => Entry(seq, ActivityType.Phase) with { Phase = phase }, ct);

    public Task ToolAsync(ToolActivity tool, CancellationToken ct) =>
        EntryAsync(seq => Entry(seq, ActivityType.Tool) with { Tool = tool }, ct);

    public Task AttemptAsync(AttemptActivity result, CancellationToken ct) =>
        EntryAsync(seq => Entry(seq, ActivityType.Attempt) with
        {
            Result = result with { Errors = result.Errors.Take(TestGenActivity.MaxErrors).ToList() },
        }, ct);

    /// <summary>The agent's work is over: the last entry of a completed task, saying why it stopped.</summary>
    public Task StoppedAsync(StoppedActivity stop, CancellationToken ct) =>
        EntryAsync(seq => Entry(seq, ActivityType.Stopped) with { Stop = stop }, ct);

    /// <summary>The agent took the task over after a restart, at <see cref="Attempt"/>.</summary>
    public Task ResumedAsync(CancellationToken ct) => EntryAsync(seq => Entry(seq, ActivityType.Resumed), ct);

    /// <summary>Model text or reasoning as it streams; it goes out in chunks, each continuing the reply's entry.</summary>
    public async Task TextAsync(string type, string delta, CancellationToken ct)
    {
        if (delta.Length == 0 || !_streams.TryGetValue(type, out var stream) || stream.Capped)
        {
            return;
        }
        await _gate.WaitAsync(ct);
        try
        {
            if (stream.Buffer.Length == 0)
            {
                stream.Since = time.GetUtcNow();
            }
            stream.Buffer.Append(delta);
            if (Encoding.UTF8.GetByteCount(stream.Buffer.ToString()) >= ChunkBytes || time.GetUtcNow() - stream.Since >= ChunkAge)
            {
                await FlushAsync(type, stream, ct);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>The reply is over: what is buffered goes out, and the next text starts a new entry.</summary>
    public async Task EndTextAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await EndTextLockedAsync(ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EntryAsync(Func<long, TestGenActivity> entry, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            // Whatever the model said before this belongs before it.
            await EndTextLockedAsync(ct);
            await SendAsync(entry(++_seq), ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EndTextLockedAsync(CancellationToken ct)
    {
        foreach (var (type, stream) in _streams)
        {
            await FlushAsync(type, stream, ct);
            stream.Reset();
        }
    }

    private async Task FlushAsync(string type, TextStream stream, CancellationToken ct)
    {
        if (stream.Buffer.Length == 0 || stream.Capped)
        {
            stream.Buffer.Clear();
            return;
        }
        var chunk = stream.Buffer.ToString();
        stream.Buffer.Clear();
        var room = TestGenActivity.MaxTextBytes - stream.Sent;
        var truncated = false;
        if (Encoding.UTF8.GetByteCount(chunk) > room)
        {
            chunk = Fit(chunk, room);
            truncated = true;
            stream.Capped = true;
        }
        stream.Sent += Encoding.UTF8.GetByteCount(chunk);
        var seq = ++_seq;
        var entry = Entry(seq, type) with { Continues = stream.Root, Text = chunk, Truncated = truncated };
        stream.Root ??= seq;
        await SendAsync(entry, ct);
    }

    private async Task SendAsync(TestGenActivity entry, CancellationToken ct)
    {
        try
        {
            await send([entry], ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            Failures++;
            logger.LogWarning("activity entry {Seq} not sent ({ErrorType})", entry.Seq, ex.GetType().Name);
        }
    }

    private TestGenActivity Entry(long seq, string type) =>
        new(TestGenKinds.Activity, seq, time.GetUtcNow(), Attempt, type);

    /// <summary>The longest prefix of the text that fits in the bytes, without splitting a character.</summary>
    private static string Fit(string text, int bytes)
    {
        if (bytes <= 0)
        {
            return "";
        }
        var length = Math.Min(text.Length, bytes);
        while (length > 0 && Encoding.UTF8.GetByteCount(text.AsSpan(0, length)) > bytes)
        {
            length--;
        }
        if (length > 0 && char.IsHighSurrogate(text[length - 1]))
        {
            length--;
        }
        return text[..length];
    }

    private sealed class TextStream
    {
        public StringBuilder Buffer { get; } = new();
        public DateTimeOffset Since { get; set; }
        public long? Root { get; set; }
        public int Sent { get; set; }
        public bool Capped { get; set; }

        public void Reset()
        {
            Buffer.Clear();
            Root = null;
            Sent = 0;
            Capped = false;
        }
    }
}
