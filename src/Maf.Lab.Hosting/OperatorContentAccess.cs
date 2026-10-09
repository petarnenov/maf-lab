using System.Security.Claims;
using Maf.Lab.Domain.Configuration;
using Maf.Lab.Domain.SharedState;
using Maf.Lab.Domain.Tenancy;
using Microsoft.Extensions.Options;

namespace Maf.Lab.Hosting;

/// <summary>Every resource server consults the same permission plane; no issuer grant claim authorizes content.</summary>
public sealed class SharedOperatorContentAccess(IHttpContextAccessor http, IOptions<AuthOptions> options,
    TimeProvider clock, IBreakGlassPermissionStore? permissions = null) : IOperatorContentAccess
{
    public async Task<bool> MayReadAsync(Principal principal, CancellationToken ct)
    {
        if (!principal.IsPlatformAdmin) return true;
        if (permissions is null || !OperatorContentIdentity.TryKey(principal, http.HttpContext?.User, options.Value, out var key)) return false;
        try
        {
            var permission = await permissions.ReadAsync(key, ct);
            return permission is not null && permission.SessionKey == key && permission.ExpiresAt > clock.GetUtcNow();
        }
        catch (Exception error) when (error is not OperationCanceledException) { return false; }
    }
}

public static class OperatorContentIdentity
{
    public static bool TryKey(Principal principal, ClaimsPrincipal? user, AuthOptions options, out string key)
    {
        key = "";
        if (!principal.IsPlatformAdmin || user?.Identity?.IsAuthenticated != true) return false;
        var sessions = user.FindAll("sid").ToArray();
        if (sessions.Length != 1 || sessions[0].ValueType != ClaimValueTypes.String
            || string.IsNullOrWhiteSpace(sessions[0].Value)) return false;
        var issuer = string.IsNullOrWhiteSpace(options.Authority) ? options.Issuer : new Uri(options.Authority).AbsoluteUri.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(issuer)) return false;
        key = OperatorSessionKey.Create(issuer, sessions[0].Value, principal);
        return true;
    }
}
