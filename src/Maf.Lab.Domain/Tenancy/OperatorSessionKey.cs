using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Maf.Lab.Domain.Tenancy;

/// <summary>Opaque identity of the validated issuer/session/user/selected-organization tuple.</summary>
public static class OperatorSessionKey
{
    public static string Create(string issuer, string sid, Principal principal)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(issuer);
        ArgumentException.ThrowIfNullOrWhiteSpace(sid);
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentException.ThrowIfNullOrWhiteSpace(principal.UserId);
        ArgumentException.ThrowIfNullOrWhiteSpace(principal.TenantId.Value);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new[] { issuer, sid, principal.UserId, principal.TenantId.Value }))));
    }
}
