namespace Maf.Lab.A2A;

/// <summary>
/// Where an agent keeps the webhooks its callers registered. It is an interface because the two agents in this lab
/// keep them in different places — one in the database its replicas share, one in memory for the length of a
/// review — and neither choice belongs in the protocol code.
/// </summary>
public interface IPushConfigStore
{
    /// <summary>Adds the configuration, or replaces the one already under that id for that task.</summary>
    Task SaveAsync(PushConfigRecord config, CancellationToken ct);

    /// <summary>The configurations registered for a task, oldest first.</summary>
    Task<IReadOnlyList<PushConfigRecord>> ListAsync(string taskId, CancellationToken ct);

    Task DeleteAsync(string taskId, string id, CancellationToken ct);
}

/// <param name="Token">The caller's own token, echoed back so its receiver can tell a delivery is genuine.</param>
public sealed record PushConfigRecord(string Id, string TaskId, string Url, string? Token);
