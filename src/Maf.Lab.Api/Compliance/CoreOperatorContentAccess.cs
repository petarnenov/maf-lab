using Maf.Lab.Domain.Configuration;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Hosting;
using Microsoft.Extensions.Options;

namespace Maf.Lab.Api.Compliance;

/// <summary>The API also checks its durable row, so a pending end cannot keep permitting content here.</summary>
public sealed class CoreOperatorContentAccess(IHttpContextAccessor http, IOptions<AuthOptions> options,
    ContentAccessGrants grants) : IOperatorContentAccess
{
    public async Task<bool> MayReadAsync(Principal principal, CancellationToken ct)
    {
        if (!principal.IsPlatformAdmin) return true;
        if (!OperatorContentIdentity.TryKey(principal, http.HttpContext?.User, options.Value, out var key)) return false;
        try { return await grants.IsAllowedAsync(principal, key, ct); }
        catch (Exception error) when (error is not OperationCanceledException) { return false; }
    }
}
