using Microsoft.Extensions.DependencyInjection;

namespace Maf.Lab.Hosting;

/// <summary>
/// Makes load balancing observable: every response carries X-Instance (the container hostname) and /health reports
/// it. Every service in the lab uses it, which is why it sits in a library none of them owns.
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

    /// <summary>
    /// Health is what this replica can actually serve. A replica that cannot reach the shared state answers some
    /// requests correctly and loses others, so it says it is not healthy and the balancer routes around it.
    /// </summary>
    public static IEndpointRouteBuilder MapInstanceHealth(this IEndpointRouteBuilder app)
    {
        // Resolved from the request rather than taken as a parameter, which a minimal API would read as a body.
        app.MapGet("/health", async (HttpContext http, CancellationToken ct) =>
        {
            if (http.RequestServices.GetService<SharedStateHealth>() is not { } shared)
            {
                return Results.Ok(new { status = "ok", instance = Name });
            }
            var (ok, reason) = await shared.CheckAsync(ct);
            return ok
                ? Results.Ok(new { status = "ok", instance = Name })
                : Results.Json(new { status = "degraded", instance = Name, reason }, statusCode: 503);
        }).AllowAnonymous();
        return app;
    }
}
