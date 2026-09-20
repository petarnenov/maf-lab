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

    public static IEndpointRouteBuilder MapInstanceHealth(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health", () => Results.Ok(new { status = "ok", instance = Name })).AllowAnonymous();
        return app;
    }
}
