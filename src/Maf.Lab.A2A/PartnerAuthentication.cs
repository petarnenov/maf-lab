using Maf.Lab.Domain.Configuration;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Maf.Lab.A2A;

/// <summary>Adds the partner bearer scheme beside the user one. The two never overlap: different audiences.</summary>
public static class PartnerAuthentication
{
    /// <summary>Authorization policy for the A2A surface: a valid partner token and the read scope.</summary>
    public const string Policy = "a2a-partner";

    public static IServiceCollection AddA2APartnerAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<A2AOptions>(configuration.GetSection(A2AOptions.Section));

        // Options are read when a request is authorized, not when the pipeline is built: a partner added to
        // configuration afterwards is a partner the running host already knows.
        services.AddAuthentication().AddJwtBearer(PartnerJwt.Scheme, _ => { });
        services.AddOptions<JwtBearerOptions>(PartnerJwt.Scheme)
            .Configure<IOptions<AuthOptions>, IOptions<A2AOptions>>((options, auth, a2a) =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = PartnerJwt.ValidationParameters(auth.Value, a2a.Value);
            });

        services.AddOptions<AuthorizationOptions>()
            .Configure<IOptionsMonitor<A2AOptions>>((options, a2a) =>
                options.AddPolicy(Policy, policy => policy
                    .AddAuthenticationSchemes(PartnerJwt.Scheme)
                    .RequireAssertion(context => PartnerJwt.Resolve(context.User, a2a.CurrentValue) is { } partner
                        && (a2a.CurrentValue.RequiredScope is { Length: > 0 } scope
                            ? partner.Has(scope)
                            : partner.Scopes.Count > 0))));

        // Singleton: it reads the current HttpContext on every call, so it is safe outside a scope.
        services.AddSingleton<IPartnerAccessor, HttpPartnerAccessor>();
        return services;
    }
}

/// <summary>The partner behind the current request.</summary>
public interface IPartnerAccessor
{
    PartnerPrincipal Current { get; }
}

public sealed class HttpPartnerAccessor(IHttpContextAccessor http, IOptions<A2AOptions> options) : IPartnerAccessor
{
    public PartnerPrincipal Current =>
        PartnerJwt.Resolve(http.HttpContext?.User, options.Value)
        ?? throw new InvalidOperationException("No partner on this request.");
}
