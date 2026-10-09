using Maf.Lab.Api.Compliance;
using Maf.Lab.Domain.Configuration;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Retrieval.Auth;
using Microsoft.Extensions.Options;

namespace Maf.Lab.Api.Endpoints;

/// <summary>Content escalation belongs to the core and survives uninstalling either administration shell.</summary>
public static class ContentAccessEndpoints
{
    public sealed record IssueRequest(string? Reason, int DurationMinutes);

    public static IEndpointRouteBuilder MapContentAccess(this IEndpointRouteBuilder app)
    {
        var platform = app.MapGroup("/api/platform/content-access").RequireAuthorization(PolicyNames.PlatformAdmin);
        platform.WithMetadata(OperatorConfigurationAccess.Instance);
        platform.MapGet("", async (HttpContext context, IPrincipalAccessor principals, IOptions<AuthOptions> options,
            ContentAccessGrants grants, CancellationToken ct) =>
            TrySessionKey(context, principals.Current, options.Value, out var key)
                ? (IResult)Results.Ok(await grants.ReadAsync(principals.Current, key, ct)) : Results.Unauthorized());
        platform.MapPost("", async (IssueRequest request, HttpContext context, IPrincipalAccessor principals,
            IOptions<AuthOptions> options, ContentAccessGrants grants, CancellationToken ct) =>
            TrySessionKey(context, principals.Current, options.Value, out var key)
                ? Result(await grants.IssueAsync(principals.Current, key, request.Reason, request.DurationMinutes, ct))
                : Results.Unauthorized());
        platform.MapPost("/{id:long}/end", async (long id, HttpContext context, IPrincipalAccessor principals,
            IOptions<AuthOptions> options, ContentAccessGrants grants, CancellationToken ct) =>
            TrySessionKey(context, principals.Current, options.Value, out var key)
                ? Result(await grants.EndAsync(principals.Current, key, id, ct)) : Results.Unauthorized());
        app.MapGet("/api/admin/content-access", async (long? before, IPrincipalAccessor principals,
            ContentAccessGrants grants, CancellationToken ct) =>
            Results.Ok(await grants.ListAsync(principals.Current, before, ct)))
            .RequireAuthorization(PolicyNames.TenantAdmin).WithMetadata(OperatorConfigurationAccess.Instance);
        return app;
    }

    public static bool TrySessionKey(HttpContext context, Principal principal, AuthOptions options, out string key)
    {
        key = "";
        var company = !string.IsNullOrWhiteSpace(options.Authority);
        if (!OperatorSessionAudit.TryReadSession(context.User, principal, company, out var session)) return false;
        var issuer = company ? new Uri(options.Authority!).AbsoluteUri.TrimEnd('/') : options.Issuer;
        key = OperatorSessionKey.Create(issuer, session, principal);
        return true;
    }

    private static IResult Result(ContentGrantChange change) => change.Status switch
    {
        ContentGrantStatus.Created => Results.Created("/api/platform/content-access", change.Envelope),
        ContentGrantStatus.Conflict => Results.Conflict(change.Envelope),
        ContentGrantStatus.Invalid => Results.BadRequest(new { error = "Provide a ticket or incident reference and a duration of 1–60 minutes." }),
        ContentGrantStatus.Ended => Results.Ok(change.Envelope),
        ContentGrantStatus.NotFound => Results.NotFound(),
        _ => Results.Json(change.Envelope, statusCode: StatusCodes.Status503ServiceUnavailable),
    };
}
