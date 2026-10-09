namespace Maf.Lab.Domain.SharedState;

/// <summary>An audited content grant for one validated operator session, with its original absolute expiry.</summary>
public sealed record ContentPermission(string SessionKey, string GrantId, DateTimeOffset ExpiresAt);

/// <summary>
/// Permission shared by every replica. Publication follows the durable grant/audit transaction; retries may
/// publish the same grant but may never extend it or resurrect an ended grant.
/// </summary>
public interface IBreakGlassPermissionStore
{
    Task<bool> ActivateAsync(ContentPermission permission, CancellationToken ct);
    Task RevokeAsync(string sessionKey, string grantId, DateTimeOffset expiresAt, CancellationToken ct);
    Task<ContentPermission?> ReadAsync(string sessionKey, CancellationToken ct);
}
