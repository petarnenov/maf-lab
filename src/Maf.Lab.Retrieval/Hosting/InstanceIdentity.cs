using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Maf.Lab.Retrieval.Hosting;

/// <summary>
/// Makes load balancing observable: every response carries X-Instance (the container hostname) and /health reports it.
/// </summary>
public static class InstanceIdentity
{
    public const string Header = "X-Instance";

    public static string Name { get; } = Environment.MachineName;

    public static IApplicationBuilder UseInstanceHeader(this IApplicationBuilder app) =>
        app.Use((context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                context.Response.Headers[Header] = Name;
                return Task.CompletedTask;
            });
            return next(context);
        });

    public static IEndpointRouteBuilder MapInstanceHealth(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health", () => Results.Ok(new { status = "ok", instance = Name })).AllowAnonymous();
        return app;
    }
}
