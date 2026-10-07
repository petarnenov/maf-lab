using System.Text.Json;

namespace Maf.Lab.Tests;

/// <summary>One row of `evals/injection-a2a.jsonl`: what a reviewer sent, and what it must be treated as.</summary>
public sealed record HostileVerdict(string Id, string What, JsonElement Asked, JsonElement Verdict, JsonElement Expect)
{
    public string AskedAdjustmentId => Asked.GetProperty("adjustmentId").GetString()!;
    public string AskedAccountId => Asked.GetProperty("accountId").GetString()!;
    public decimal AskedAmount => Asked.GetProperty("amount").GetDecimal();
    public string ExpectedResult => Expect.GetProperty("result").GetString()!;
    public override string ToString() => $"{Id} — {What}";
}

/// <summary>The rows of `evals/injection-a2a.jsonl`: what a broken or hostile reviewer might send.</summary>
public static class HostileVerdicts
{
    public static List<HostileVerdict> Load()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        return [.. File.ReadLines(Path.Combine(CorpusLoaderTests.RepoRoot(), "evals", "injection-a2a.jsonl"))
            .Where(line => line.Trim().Length > 0)
            .Select(line => JsonSerializer.Deserialize<HostileVerdict>(line, options)!)];
    }

    public static TheoryData<HostileVerdict> Fixtures()
    {
        var data = new TheoryData<HostileVerdict>();
        foreach (var row in Load())
        {
            data.Add(row);
        }
        return data;
    }
}
