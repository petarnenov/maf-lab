using Maf.Lab.Domain.Tenancy;
using Microsoft.AspNetCore.Http;

namespace Maf.Lab.Retrieval.Auth;

public sealed class HttpPrincipalAccessor(IHttpContextAccessor http) : IPrincipalAccessor
{
    public Principal Current =>
        PrincipalClaims.TryCreate(http.HttpContext?.User, out var principal)
            ? principal
            : throw new UnauthorizedAccessException("No authenticated principal.");
}

/// <summary>Fixed principal for background jobs, evals and tests.</summary>
public sealed class FixedPrincipalAccessor(Principal principal) : IPrincipalAccessor
{
    public Principal Current => principal;
}
