using System.Text.Json.Nodes;
using Maf.Lab.Api.Feedback;
using Maf.Lab.Api.Storage;
using Microsoft.EntityFrameworkCore;

namespace Maf.Lab.Eval;

/// <summary>
/// Copies reviewer labels from the API database into the eval datasets (idempotent by row id). The label endpoint
/// already appends when it shares the datasets folder; this covers labels made against another copy of the datasets.
/// </summary>
public static class FeedbackImporter
{
    public static async Task<int> ImportAsync(string apiConnectionString, DatasetWriter datasets, CancellationToken ct)
    {
        var options = new DbContextOptionsBuilder<MafDbContext>().UseSqlite(apiConnectionString).Options;
        await using var db = new MafDbContext(options);
        var labels = await db.Labels.AsNoTracking().OrderBy(l => l.CreatedAt).ToListAsync(ct);
        var added = 0;
        foreach (var label in labels)
        {
            if (JsonNode.Parse(label.RowJson) is JsonObject row && await datasets.AppendAsync(label.Dataset, row, ct))
            {
                added++;
            }
        }
        return added;
    }
}
