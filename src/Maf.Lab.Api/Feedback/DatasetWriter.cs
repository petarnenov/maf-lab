using System.Text.Json;
using System.Text.Json.Nodes;

namespace Maf.Lab.Api.Feedback;

/// <summary>Appends labeled rows to the eval JSONL datasets. Idempotent by row id.</summary>
public sealed class DatasetWriter(IConfiguration configuration)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public string Root { get; } = ResolveRoot(configuration["Evals:Root"]);

    public async Task<bool> AppendAsync(string dataset, JsonObject row, CancellationToken ct)
    {
        var id = row["id"]?.GetValue<string>() ?? throw new ArgumentException("row needs an id");
        var path = Path.Combine(Root, $"{dataset}.jsonl");
        await Gate.WaitAsync(ct);
        try
        {
            if (File.Exists(path))
            {
                foreach (var line in await File.ReadAllLinesAsync(path, ct))
                {
                    if (line.Contains($"\"id\":\"{id}\"", StringComparison.Ordinal))
                    {
                        return false;
                    }
                }
            }
            Directory.CreateDirectory(Root);
            await File.AppendAllTextAsync(path, row.ToJsonString(Json) + "\n", ct);
            return true;
        }
        finally
        {
            Gate.Release();
        }
    }

    public static string ResolveRoot(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Path.GetFullPath(configured);
        }
        for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "maf-lab.sln")))
            {
                return Path.Combine(dir.FullName, "evals");
            }
        }
        return Path.GetFullPath("evals");
    }
}
