using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Retrieval.Auth;
using Microsoft.AspNetCore.Authorization;

namespace Maf.Lab.Api.Compliance;

/// <summary>Authenticated operator endpoints are content by default; configuration exceptions are server metadata.</summary>
public sealed class OperatorContentFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        if (http.GetEndpoint()?.Metadata.GetMetadata<OperatorConfigurationAccess>() is not null
            || http.GetEndpoint()?.Metadata.GetMetadata<IAllowAnonymous>() is not null
            || !PrincipalClaims.TryCreate(http.User, out var principal) || !principal.IsPlatformAdmin) return await next(context);
        var access = http.RequestServices.GetRequiredService<IOperatorContentAccess>();
        return await access.MayReadAsync(principal, http.RequestAborted)
            ? await next(context) : Results.Forbid();
    }
}
