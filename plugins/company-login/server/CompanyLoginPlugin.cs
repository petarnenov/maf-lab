using Maf.Lab.Domain.Configuration;
using Maf.Lab.Plugins.Abstractions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Maf.Lab.Plugins.CompanyLogin;

/// <summary>Public OIDC settings only; absent authority leaves the dev/qa installation inactive.</summary>
public sealed class CompanyLoginPlugin : IMafPlugin, IContributesServices, IContributesEndpoints
{
    public const string PluginName = "company-login";
    public string Name => PluginName;
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        var auth = configuration.GetSection(AuthOptions.Section).Get<AuthOptions>() ?? new();
        if (!string.IsNullOrWhiteSpace(auth.Authority) && string.IsNullOrWhiteSpace(auth.WebClientId))
            throw new InvalidOperationException("Company sign-in requires Auth:WebClientId.");
    }

    public void MapEndpoints(IMafEndpoints endpoints)
    {
        endpoints.Routes.MapGet("/api/identity/configuration", (IOptions<AuthOptions> options) =>
        {
            var auth = options.Value;
            if (string.IsNullOrWhiteSpace(auth.Authority)) return Results.NotFound();
            // These settings are public OIDC metadata. Neither the API's exchange secret nor a bearer is projected.
            return Results.Ok(new CompanySignInConfiguration(new Uri(auth.Authority).AbsoluteUri.TrimEnd('/'),
                auth.WebClientId, "code", "openid profile organization domain-claims", "/auth/callback", "/auth/signed-out"));
        }).AllowAnonymous();
    }
}

public sealed record CompanySignInConfiguration(string Authority, string ClientId, string ResponseType,
    string Scope, string CallbackPath, string SignedOutPath);
