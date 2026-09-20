using A2A;
using A2A.AspNetCore;
using Maf.Lab.Retrieval.Configuration;
using Microsoft.Extensions.Options;

namespace Maf.Lab.Api.A2A;

/// <summary>
/// The A2A surface: discovery, the token a partner obtains, and the protocol endpoints themselves. The public card
/// is anonymous by design — discovery is how a partner learns it needs a token at all.
/// </summary>
public static class A2AEndpoints
{
    public static IEndpointRouteBuilder MapA2ASurface(this IEndpointRouteBuilder app)
    {
        // Served with the protocol's own serializer options, so the document a partner receives is member for
        // member the document the signature was computed over.
        app.MapGet(AgentCardFactory.WellKnownPath, (IOptions<A2AOptions> a2a, IOptions<AuthOptions> auth) =>
            Results.Json(AgentCardFactory.Signed(AgentCardFactory.Public(a2a.Value), auth.Value),
                A2AJsonUtilities.DefaultOptions))
            .AllowAnonymous();

        // Client credentials, as the card advertises. The lab's stand-in for a real token endpoint.
        app.MapPost("/a2a/token", (TokenRequest request, IOptions<A2AOptions> a2a, IOptions<AuthOptions> auth) =>
        {
            if (!a2a.Value.Partners.TryGetValue(request.ClientId, out var registration)
                || !string.Equals(registration.Secret, request.ClientSecret, StringComparison.Ordinal))
            {
                return Results.Unauthorized();
            }
            var requested = string.IsNullOrWhiteSpace(request.Scope)
                ? registration.Scopes
                : request.Scope.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(registration.Scopes.Contains).ToList();
            var (token, expires) = PartnerJwt.Issue(auth.Value, a2a.Value, request.ClientId, requested);
            return Results.Ok(new TokenResponse(token, "Bearer",
                (int)(expires - DateTimeOffset.UtcNow).TotalSeconds, string.Join(' ', requested)));
        }).AllowAnonymous();

        return app;
    }

    /// <summary>
    /// Both transports the SDK maps, behind partner authentication. gRPC is not offered (see DECISIONS.md).
    /// </summary>
    public static IEndpointRouteBuilder MapA2AProtocol(this IEndpointRouteBuilder app)
    {
        var handler = app.ServiceProvider.GetRequiredService<IA2ARequestHandler>();
        var card = app.ServiceProvider.GetRequiredService<IOptions<A2AOptions>>().Value;

        app.MapA2A(handler, AgentCardFactory.A2APath)
            .RequireAuthorization(PartnerAuthentication.Policy);
        app.MapHttpA2A(handler, AgentCardFactory.Public(card), AgentCardFactory.A2APath)
            .RequireAuthorization(PartnerAuthentication.Policy);
        return app;
    }

    /// <param name="GrantType">Accepted for shape; the lab issues only client credentials.</param>
    public sealed record TokenRequest(string ClientId, string ClientSecret, string? Scope = null, string GrantType = "client_credentials");

    public sealed record TokenResponse(string AccessToken, string TokenType, int ExpiresIn, string Scope);
}
