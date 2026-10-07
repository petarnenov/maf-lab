using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
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

    /// <summary>
    /// This process, unique per start: the hostname plus a fresh id. `docker restart` keeps a container's hostname, so
    /// the hostname alone would let a restarted replica's heartbeat vouch for the runs of the process before it
    /// (introduce-plugins decision 2). It names a run's owner, never anything a client sees.
    /// </summary>
    public static string ProcessId { get; } = $"{Environment.MachineName}-{Guid.NewGuid():N}";

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
    /// The health checks this process reports (ASP.NET Core Health Checks). Every service that maps
    /// <see cref="MapInstanceHealth"/> registers them; the shared state adds its own check (<see cref="SharedStateHealth"/>).
    /// </summary>
    public static IHealthChecksBuilder AddInstanceHealth(this IServiceCollection services) => services.AddHealthChecks();

    /// <summary>
    /// Health is what this replica can actually serve. A replica that cannot reach the shared state answers some
    /// requests correctly and loses others, so it says it is not healthy and the balancer routes around it. Mapped on
    /// ASP.NET Core's health checks, in the shape the balancer and the topology have always read:
    /// <c>{ status: "ok", instance }</c>, or <c>{ status: "degraded", instance, reason }</c> with 503.
    /// </summary>
    public static IEndpointRouteBuilder MapInstanceHealth(this IEndpointRouteBuilder app)
    {
        app.MapHealthChecks("/health", new HealthCheckOptions
        {
            ResultStatusCodes =
            {
                [HealthStatus.Healthy] = StatusCodes.Status200OK,
                [HealthStatus.Degraded] = StatusCodes.Status503ServiceUnavailable,
                [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable,
            },
            ResponseWriter = WriteAsync,
        }).AllowAnonymous();
        return app;
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static Task WriteAsync(HttpContext http, HealthReport report)
    {
        http.Response.ContentType = "application/json; charset=utf-8";
        if (report.Status == HealthStatus.Healthy)
        {
            return http.Response.WriteAsync(JsonSerializer.Serialize(new { status = "ok", instance = Name }, Json));
        }
        var reason = report.Entries.Values.Where(e => e.Status != HealthStatus.Healthy).Select(e => e.Description).FirstOrDefault(d => d is not null);
        return http.Response.WriteAsync(JsonSerializer.Serialize(new { status = "degraded", instance = Name, reason }, Json));
    }
}
