namespace Maf.Lab.Plugins.Abstractions;

/// <summary>
/// The core record of one turn (introduce-plugins 5.3): when it was asked, and the allow-listed trace events the core
/// keeps for it as JSON. Numbers and labels only — never the question, the passages or the model's messages.
/// </summary>
public sealed record StoredTurnRecord(DateTime CreatedAt, string RecordJson);

/// <summary>
/// A read port onto the turns' core records (extract-insights-plugin), for a plugin that aggregates them. Like
/// <see cref="IConversationStore"/> it takes no tenant, user or principal: the core reads the caller's tenant from the
/// request, and who may read them is the core's rule (a tenant admin of that tenant).
/// </summary>
public interface ITurnRecords
{
    /// <summary>The core records of the caller's tenant's turns created at or after <paramref name="from"/>.</summary>
    Task<IReadOnlyList<StoredTurnRecord>> SinceAsync(DateTimeOffset from, CancellationToken ct);
}

/// <summary>The prompt and content guard's configuration as the running service applies it.</summary>
/// <param name="CrossTenantAt">The prompt guard's threshold for the cross-tenant question.</param>
public readonly record struct GuardSettings(bool Enabled, double PromptBlockAt, double ContentWithholdAt, double CrossTenantAt);

/// <summary>
/// A read port onto the guard's effective configuration (extract-insights-plugin): what the core applies, read-only,
/// without the section it is bound from — the shape of <see cref="IInstalledPlugins.McpEndpoint"/>.
/// </summary>
public interface IGuardSettings
{
    GuardSettings Current { get; }
}
