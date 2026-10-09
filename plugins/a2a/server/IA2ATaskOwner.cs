using Maf.Lab.Domain.Tenancy;

namespace Maf.Lab.Plugins.A2A;

/// <summary>The registered reader a persisted task belongs to, after the current partner's access check.</summary>
public interface IA2ATaskOwner
{
    Task<Principal?> OwnerAsync(string taskId, CancellationToken ct);
}
