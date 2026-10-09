using System.Text;
using System.Text.Json;
using Maf.Lab.TestGen;
using Microsoft.EntityFrameworkCore;

namespace Maf.Lab.Plugins.Coverage;

/// <summary>
/// The record of what the agent did during a run (add-run-activity-view): every entry once, in the agent's order,
/// chunks of model text joined to their entry, and capped per run. It is content — shown to the browser over the run's
/// AG-UI stream, never logged or traced.
/// </summary>
public sealed class RunActivityStore(IDbContextFactory<DbContext> dbFactory)
{
    public const int MaxEntries = 2_000;
    public const int MaxBytes = 1024 * 1024;

    /// <summary>Stores the entries not stored yet; returns whether anything changed.</summary>
    public async Task<bool> AddAsync(string runId, IReadOnlyList<TestGenActivity> entries, CancellationToken ct)
    {
        if (entries.Count == 0)
        {
            return false;
        }
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await TryAddAsync(runId, entries, ct);
            }
            catch (DbUpdateException) when (attempt < 2)
            {
                // Another replica stored some of them first: look again, and store only what is still missing.
            }
        }
    }

    private async Task<bool> TryAddAsync(string runId, IReadOnlyList<TestGenActivity> entries, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var seqs = entries.Select(e => e.Continues ?? e.Seq).Concat(entries.Select(e => e.Seq)).Distinct().ToList();
        var rows = await db.Set<TestGenRunActivityRow>().Where(a => a.RunId == runId && seqs.Contains(a.Seq))
            .ToDictionaryAsync(a => a.Seq, ct);
        var changed = false;
        foreach (var entry in entries.OrderBy(e => e.Seq))
        {
            if (entry.Continues is { } root && rows.TryGetValue(root, out var row))
            {
                // A chunk of text already stored, or an older one arriving late: nothing to do.
                if (entry.Seq <= row.LastSeq)
                {
                    continue;
                }
                var (text, truncated) = Joined(row.Text, entry.Text);
                row.Text = text;
                row.Truncated |= truncated || entry.Truncated;
                row.LastSeq = entry.Seq;
                changed = true;
                continue;
            }
            if (rows.ContainsKey(entry.Seq))
            {
                continue;
            }
            // A chunk whose first part never arrived is kept as an entry of its own rather than lost.
            var added = new TestGenRunActivityRow
            {
                RunId = runId,
                Seq = entry.Continues ?? entry.Seq,
                LastSeq = entry.Seq,
                At = entry.At.UtcDateTime,
                Attempt = entry.Attempt,
                Type = entry.Type,
                Phase = entry.Phase,
                DataJson = entry.Tool is { } tool ? JsonSerializer.Serialize(tool, TestGenKinds.Json)
                    : entry.Result is { } result ? JsonSerializer.Serialize(result, TestGenKinds.Json)
                    : entry.Stop is { } stop ? JsonSerializer.Serialize(stop, TestGenKinds.Json)
                    : null,
                Text = entry.Text,
                Truncated = entry.Truncated,
            };
            db.Set<TestGenRunActivityRow>().Add(added);
            rows[added.Seq] = added;
            changed = true;
        }
        if (!changed)
        {
            return false;
        }
        await db.SaveChangesAsync(ct);
        await CapAsync(db, runId, ct);
        return true;
    }

    /// <summary>Past the cap, the oldest entries go and the run says so.</summary>
    private static async Task CapAsync(DbContext db, string runId, CancellationToken ct)
    {
        var sizes = await db.Set<TestGenRunActivityRow>().Where(a => a.RunId == runId).OrderByDescending(a => a.Seq)
            .Select(a => new { a.Seq, Bytes = (a.Text ?? "").Length + (a.DataJson ?? "").Length + 64 })
            .ToListAsync(ct);
        long total = 0;
        long? oldestKept = null;
        var kept = 0;
        foreach (var entry in sizes)
        {
            if (kept == MaxEntries || total + entry.Bytes > MaxBytes)
            {
                break;
            }
            total += entry.Bytes;
            kept++;
            oldestKept = entry.Seq;
        }
        if (kept == sizes.Count || oldestKept is null)
        {
            return;
        }
        var floor = oldestKept.Value;
        await db.Set<TestGenRunActivityRow>().Where(a => a.RunId == runId && a.Seq < floor).ExecuteDeleteAsync(ct);
        await db.Set<TestGenRunRow>().Where(r => r.Id == runId).ExecuteUpdateAsync(s => s.SetProperty(r => r.ActivityDropped, true), ct);
    }

    /// <summary>The run's entries whose content changed after <paramref name="afterSeq"/>, in order.</summary>
    public async Task<IReadOnlyList<TestGenRunActivityRow>> ChangedSinceAsync(string runId, long afterSeq, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Set<TestGenRunActivityRow>().AsNoTracking().Where(a => a.RunId == runId && a.LastSeq > afterSeq)
            .OrderBy(a => a.Seq).ToListAsync(ct);
    }

    private static (string? Text, bool Truncated) Joined(string? text, string? more)
    {
        var joined = (text ?? "") + (more ?? "");
        if (Encoding.UTF8.GetByteCount(joined) <= TestGenActivity.MaxTextBytes)
        {
            return (joined, false);
        }
        var length = Math.Min(joined.Length, TestGenActivity.MaxTextBytes);
        while (length > 0 && Encoding.UTF8.GetByteCount(joined.AsSpan(0, length)) > TestGenActivity.MaxTextBytes)
        {
            length--;
        }
        return (joined[..length], true);
    }
}
