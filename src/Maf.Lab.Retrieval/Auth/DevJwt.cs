using System.Security.Claims;
using System.Text;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Domain.Configuration;
using Maf.Lab.Retrieval.Configuration;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Maf.Lab.Retrieval.Auth;

/// <summary>
/// Local dev token issuer and the matching bearer validation used by both the API and the MCP server.
/// </summary>
public static class DevJwt
{
    public static IServiceCollection AddDevJwtAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var auth = configuration.GetSection(AuthOptions.Section).Get<AuthOptions>() ?? new AuthOptions();
        services.Configure<AuthOptions>(configuration.GetSection(AuthOptions.Section));

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(o =>
            {
                o.MapInboundClaims = false;
                o.TokenValidationParameters = ValidationParameters(auth);
            });
        services.AddAuthorization();
        services.AddHttpContextAccessor();
        services.AddScoped<IPrincipalAccessor, HttpPrincipalAccessor>();
        return services;
    }

    public static TokenValidationParameters ValidationParameters(AuthOptions auth) => new()
    {
        ValidIssuer = auth.Issuer,
        ValidAudience = auth.Audience,
        IssuerSigningKey = Key(auth),
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ClockSkew = TimeSpan.FromSeconds(30),
        NameClaimType = PrincipalClaims.UserId,
        RoleClaimType = PrincipalClaims.Role,
    };

    public static (string Token, DateTimeOffset ExpiresAt) Issue(AuthOptions auth, string userId, TenantId firm, Role role, IEnumerable<string> advisorIds, DateTimeOffset? now = null)
    {
        var issuedAt = now ?? DateTimeOffset.UtcNow;
        var expires = issuedAt + auth.TokenLifetime;
        var claims = new List<Claim>
        {
            new(PrincipalClaims.UserId, userId),
            new(PrincipalClaims.FirmId, firm.Value),
            new(PrincipalClaims.Role, role.ToString()),
        };
        claims.AddRange(advisorIds.Select(a => new Claim(PrincipalClaims.AdvisorId, a)));

        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = auth.Issuer,
            Audience = auth.Audience,
            Subject = new ClaimsIdentity(claims),
            IssuedAt = issuedAt.UtcDateTime,
            NotBefore = issuedAt.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = new SigningCredentials(Key(auth), SecurityAlgorithms.HmacSha256),
        });
        return (token, expires);
    }

    private static SymmetricSecurityKey Key(AuthOptions auth)
    {
        var bytes = Encoding.UTF8.GetBytes(auth.SigningKey);
        if (bytes.Length < 32)
        {
            throw new InvalidOperationException("Auth:SigningKey must be at least 32 bytes.");
        }
        return new SymmetricSecurityKey(bytes);
    }
}
