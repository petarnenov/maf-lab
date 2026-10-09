using Maf.Lab.Domain.Tenancy;

namespace Maf.Lab.Retrieval.Auth;

/// <summary>The only way request-handling code obtains the caller's principal. Reads validated token claims only.</summary>
public interface IPrincipalAccessor
{
    Principal Current { get; }
}
