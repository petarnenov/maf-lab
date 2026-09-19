using Maf.Lab.Domain.Tenancy;
using Microsoft.AspNetCore.Http;

namespace Maf.Lab.Retrieval.Auth;

/// <summary>The only way request-handling code obtains the caller's principal. Reads validated token claims only.</summary>
public interface IPrincipalAccessor
{
    Principal Current { get; }
}

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
