namespace Maf.Lab.Domain.Services;

/// <summary>Resolves every currently advertised service replica without choosing just one.</summary>
public interface IServiceResolver
{
    Task<IReadOnlyList<string>> ResolveAsync(string service, CancellationToken ct);
}
