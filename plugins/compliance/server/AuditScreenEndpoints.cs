using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Plugins.Abstractions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Maf.Lab.Plugins.Compliance;

/// <summary>
/// The audit screen's routes (extract-compliance-plugin), at the paths they always had: what an authorised person can be
/// handed when they ask, and how they can check the record was not altered afterwards. Everything they serve comes from
/// the core's <see cref="IAuditTrail"/>; the tenant is the caller's, never a parameter.
/// </summary>
public static class AuditScreenEndpoints
{
    public static IEndpointRouteBuilder Map(IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/admin/compliance").RequireAuthorization(PolicyNames.TenantAdmin);

        api.MapGet("/verify", async (IAuditTrail trail, CancellationToken ct) => Results.Ok(await trail.VerifyAsync(ct)));

        // Reading the record is deliberately not recorded: browsing the log must not grow the log.
        api.MapGet("/actions", async (DateTimeOffset? from, DateTimeOffset? to, string? userId, string? kind, int? limit,
            long? before, IAuditTrail trail, CancellationToken ct) =>
            Results.Ok(await trail.PageAsync(new AuditFilter(from, to, userId, kind, limit ?? 50, before), ct)));

        api.MapGet("/export", async (DateTimeOffset? from, DateTimeOffset? to, string? userId, IAuditTrail trail, TimeProvider time,
            CancellationToken ct) =>
        {
            var now = time.GetUtcNow();
            var (start, end) = (from ?? now.AddYears(-1), to ?? now);
            if (end < start)
            {
                return Results.BadRequest(new { error = "'to' is before 'from'" });
            }
            return Results.Ok(await trail.ExportAsync(new ExportRange(start, end, userId), ct));
        });

        return app;
    }
}
