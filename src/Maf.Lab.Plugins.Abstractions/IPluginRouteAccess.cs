using Microsoft.AspNetCore.Http;

namespace Maf.Lab.Plugins.Abstractions;

/// <summary>
/// An alternate protocol identity boundary for a plugin's routes. Implementations validate their protocol principal,
/// derive tenants from its server-side registration, and take immutable access snapshots before invoking the endpoint.
/// Ordinary user and admin routes continue through the core's user-principal filter.
/// </summary>
public interface IPluginRouteAccess
{
    string Plugin { get; }
    ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next);
}
