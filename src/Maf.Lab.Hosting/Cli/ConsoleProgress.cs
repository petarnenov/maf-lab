using System.Globalization;
using System.Text;

namespace Maf.Lab.Hosting.Cli;

/// <summary>
/// The progress bar a CLI tool shows for its work (progress-feedback). On a terminal it redraws one line on stderr —
/// determinate (<c>done/total</c> and a percentage) once the total is known, indeterminate (step and elapsed time)
/// before — at least once a second; redirected or in CI it prints plain lines, at most one every 5 seconds. It ends in
/// exactly one final line saying whether the work succeeded, failed or was cancelled, with the count and elapsed time.
/// Callers pass counts and paths, never message content or secrets.
/// </summary>
public sealed class ConsoleProgress : IDisposable
{
    public static readonly TimeSpan RedrawInterval = TimeSpan.FromMilliseconds(250);
    public static readonly TimeSpan PlainLineInterval = TimeSpan.FromSeconds(5);
    private const int BarWidth = 28;
    private const int DetailWidth = 48;

    private readonly TextWriter _out;
    private readonly bool _interactive;
    private readonly TimeProvider _time;
    private readonly string _label;
    private readonly DateTimeOffset _started;
    private readonly ITimer? _timer;
    private readonly Lock _gate = new();
    private int? _total;
    private int _done;
    private string? _step;
    private string? _detail;
    private DateTimeOffset _lastWrite = DateTimeOffset.MinValue;
    private int _drawnWidth;
    private bool _ended;

    /// <param name="interactive">Null detects it: a terminal on stderr, outside CI, with a capable TERM.</param>
    /// <param name="heartbeat">Redraws on a timer so the elapsed time moves while one item is slow; tests turn it off.</param>
    public ConsoleProgress(string label, TextWriter? output = null, bool? interactive = null, TimeProvider? time = null, bool heartbeat = true)
    {
        _label = label;
        _out = output ?? Console.Error;
        _interactive = interactive ?? DetectInteractive();
        _time = time ?? TimeProvider.System;
        _started = _time.GetUtcNow();
        if (heartbeat)
        {
            var period = _interactive ? RedrawInterval : PlainLineInterval;
            _timer = _time.CreateTimer(_ => Tick(), null, period, period);
        }
    }

    public static bool DetectInteractive() =>
        !Console.IsErrorRedirected
        && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI"))
        && Environment.GetEnvironmentVariable("TERM") is not "dumb";

    /// <summary>Names what runs while the total is unknown (indeterminate), or what the next items belong to.</summary>
    public void Step(string step)
    {
        lock (_gate)
        {
            _step = step;
            _detail = null;
            WriteLocked(force: true);
        }
    }

    /// <summary>Makes the bar determinate.</summary>
    public void SetTotal(int total)
    {
        lock (_gate)
        {
            _total = total;
            WriteLocked(force: true);
        }
    }

    /// <summary>Shows the item being worked on (a path, never content) without counting it.</summary>
    public void Working(string detail)
    {
        lock (_gate)
        {
            _detail = detail;
            WriteLocked(force: false);
        }
    }

    /// <summary>Counts finished items.</summary>
    public void Advance(int by = 1)
    {
        lock (_gate)
        {
            _done += by;
            _detail = null;
            WriteLocked(force: false);
        }
    }

    public void Succeed(string? summary = null) => End("✓", "done", summary);

    public void Fail(string? reason = null) => End("✗", "failed", reason);

    public void Cancel() => End("✗", "cancelled", null);

    /// <summary>Redraws for the elapsed time (terminal) or prints a keep-alive line (plain), as the timer does.</summary>
    public void Tick()
    {
        lock (_gate)
        {
            WriteLocked(force: _interactive);
        }
    }

    public void Dispose()
    {
        _timer?.Dispose();
        lock (_gate)
        {
            if (!_ended && _interactive && _drawnWidth > 0)
            {
                // Work that ended without an outcome leaves no half-drawn bar behind.
                _out.Write('\r' + new string(' ', _drawnWidth) + '\r');
                _out.Flush();
            }
            _ended = true;
        }
    }

    private void End(string mark, string outcome, string? note)
    {
        _timer?.Dispose();
        lock (_gate)
        {
            if (_ended)
            {
                return;
            }
            _ended = true;
            var line = new StringBuilder().Append(mark).Append(' ').Append(_label).Append(": ").Append(outcome);
            if (_total is { } total)
            {
                line.Append(' ').Append(_done).Append('/').Append(total);
            }
            else if (_done > 0)
            {
                line.Append(' ').Append(_done);
            }
            line.Append(" in ").Append(FormatElapsed(Elapsed));
            if (!string.IsNullOrWhiteSpace(note))
            {
                line.Append(" — ").Append(note);
            }
            if (_interactive && _drawnWidth > 0)
            {
                _out.Write('\r' + new string(' ', _drawnWidth) + '\r');
            }
            _out.WriteLine(line.ToString());
            _out.Flush();
        }
    }

    private TimeSpan Elapsed => _time.GetUtcNow() - _started;

    private void WriteLocked(bool force)
    {
        if (_ended)
        {
            return;
        }
        var now = _time.GetUtcNow();
        var interval = _interactive ? RedrawInterval : PlainLineInterval;
        // A terminal redraws at once when forced; plain output stays bounded even when steps come fast.
        if (now - _lastWrite < interval && !(force && _interactive))
        {
            return;
        }
        _lastWrite = now;
        var text = Render();
        if (_interactive)
        {
            var padded = text.Length < _drawnWidth ? text + new string(' ', _drawnWidth - text.Length) : text;
            _out.Write('\r' + padded);
            _drawnWidth = text.Length;
        }
        else
        {
            _out.WriteLine(text);
        }
        _out.Flush();
    }

    /// <summary>One line: label, bar, counts and percentage (or step), elapsed time, current item.</summary>
    public string Render()
    {
        var line = new StringBuilder(_label).Append(' ');
        if (_total is { } total)
        {
            var fraction = total == 0 ? 1.0 : Math.Clamp((double)_done / total, 0, 1);
            var filled = (int)Math.Round(fraction * BarWidth);
            line.Append('[').Append(Bar('#', filled)).Append(Bar('-', BarWidth - filled)).Append("] ")
                .Append(_done).Append('/').Append(total).Append(' ')
                .Append(((int)Math.Floor(fraction * 100)).ToString(CultureInfo.InvariantCulture)).Append('%');
        }
        else
        {
            // Indeterminate: a marker that moves with time, so a stalled step is visible as a still clock, not a still bar.
            var position = (int)(Elapsed.TotalSeconds * 4) % (BarWidth - 2);
            line.Append('[').Append(Bar('-', position)).Append("<=>").Append(Bar('-', BarWidth - position - 3)).Append(']');
        }
        line.Append(' ').Append(FormatElapsed(Elapsed));
        if (_step is not null)
        {
            line.Append(" · ").Append(_step);
        }
        if (_detail is not null)
        {
            line.Append(" · ").Append(Shorten(_detail));
        }
        return line.ToString();
    }

    private static string Bar(char c, int count) => count <= 0 ? "" : new string(c, count);

    private static string Shorten(string detail) =>
        detail.Length <= DetailWidth ? detail : "…" + detail[^(DetailWidth - 1)..];

    public static string FormatElapsed(TimeSpan elapsed) =>
        elapsed.TotalHours >= 1 ? elapsed.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
        : elapsed.TotalMinutes >= 1 ? elapsed.ToString(@"m\:ss", CultureInfo.InvariantCulture)
        : elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + "s";
}
