using System.Text.Json.Nodes;

namespace Maf.Lab.Plugins.Abstractions;

/// <summary>Optional export of a labelled row; core feedback remains persisted without an evaluation contributor.</summary>
public interface IAppendEvalDataset
{
    Task<bool> AppendAsync(string dataset, JsonObject row, CancellationToken ct);
}

public sealed class NoEvalDataset : IAppendEvalDataset
{
    public Task<bool> AppendAsync(string dataset, JsonObject row, CancellationToken ct) => Task.FromResult(false);
}
