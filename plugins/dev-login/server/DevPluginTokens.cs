using System.Security.Claims;
using Maf.Lab.Domain.Configuration;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Retrieval.Auth;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Maf.Lab.Plugins.DevLogin;

/// <summary>Dev/qa only: validate the API credential and reissue it for the enabled plugin audience.</summary>
public sealed class DevPluginTokens(IOptions<AuthOptions> options, IPluginAccess access, IInstalledPlugins installed, TimeProvider time) : IPluginTokens
{
    public async Task<string> ForAsync(Principal principal, string plugin, string subjectToken, CancellationToken ct)
    {
        var snapshot = PluginAccessContext.For(principal) ?? await access.For(principal, ct);
        if (!installed.IsInstalled(plugin) || !snapshot.IsInUse(plugin)) throw new UnauthorizedAccessException("The plugin is not in use.");
        var result = await new JsonWebTokenHandler().ValidateTokenAsync(subjectToken, DevJwt.ValidationParameters(options.Value));
        if (!result.IsValid || !PrincipalClaims.TryCreate(new ClaimsPrincipal(result.ClaimsIdentity), out var caller) || caller != principal)
            throw new UnauthorizedAccessException("The subject token does not identify this API caller.");
        ct.ThrowIfCancellationRequested();
        var sessions = result.ClaimsIdentity.FindAll("sid").ToArray();
        var session = sessions.Length == 1 && sessions[0].ValueType == ClaimValueTypes.String ? sessions[0].Value : null;
        if (principal.IsPlatformAdmin && string.IsNullOrWhiteSpace(session))
            throw new UnauthorizedAccessException("The operator subject token has no validated session.");
        return DevJwt.Issue(options.Value, principal.UserId, principal.TenantId, principal.Role, time.GetUtcNow(),
            result.ClaimsIdentity.FindAll(PrincipalClaims.DomainRoles).Select(c => c.Value),
            result.ClaimsIdentity.FindAll(PrincipalClaims.AdvisorIds).Select(c => c.Value), audience: plugin, groups: principal.GroupIds,
            sessionId: session).Token;
    }
}
