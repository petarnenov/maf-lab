using System.Net;
using System.Net.Sockets;
using Maf.Lab.Domain.Configuration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.AspNetCore.Authentication;
using ModelContextProtocol.Authentication;
using IPNetwork = System.Net.IPNetwork;

namespace Maf.Lab.Retrieval.Auth;

/// <summary>Native MCP authorization metadata with configuration-pinned public URLs and explicit ingress trust.</summary>
public static class CompanyMcpAuthentication
{
    public static IServiceCollection AddLabMcpAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var auth = configuration.GetSection(AuthOptions.Section).Get<AuthOptions>() ?? new();
        if (string.IsNullOrWhiteSpace(auth.Authority)) return services;
        var development = (configuration["MAF_ENV"] ?? "dev") is "dev" or "qa";
        var authority = TrustedUri(auth.Authority, "Auth:Authority", development);
        var resource = TrustedUri(auth.ResourceUri, "Auth:ResourceUri", development);
        if (!resource.AbsolutePath.EndsWith("/mcp", StringComparison.Ordinal))
            throw new InvalidOperationException("Auth:ResourceUri must name the canonical public MCP endpoint ending in /mcp.");
        var metadataUri = new UriBuilder(resource) { Path = "/.well-known/oauth-protected-resource" + resource.AbsolutePath }.Uri;
        var networks = auth.TrustedProxyNetworks.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value =>
        {
            if (!IPNetwork.TryParse(value, out var network) || network.PrefixLength == 0)
                throw new InvalidOperationException("Auth:TrustedProxyNetworks must contain explicit ingress CIDRs; all-address networks are refused.");
            return network;
        }).ToArray();
        services.AddSingleton(new ProxyConfiguration(resource, networks));
        services.AddAuthentication(options => options.DefaultChallengeScheme = McpAuthenticationDefaults.AuthenticationScheme)
            .AddMcp(options =>
            {
                options.ResourceMetadataUri = metadataUri;
                options.ResourceMetadata = new ProtectedResourceMetadata
                {
                    Resource = resource.AbsoluteUri,
                    AuthorizationServers = [authority.AbsoluteUri.TrimEnd('/')],
                    BearerMethodsSupported = ["header"],
                };
            });
        return services;
    }

    public static WebApplication UseLabMcpForwarding(this WebApplication app)
    {
        var proxy = app.Services.GetService<ProxyConfiguration>();
        if (proxy is null || proxy.Networks.Length == 0) return app;
        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedHost | ForwardedHeaders.XForwardedProto,
            ForwardLimit = 1,
            RequireHeaderSymmetry = true,
        };
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();
        foreach (var network in proxy.Networks) options.KnownIPNetworks.Add(network);
        options.AllowedHosts.Add(proxy.Resource.IdnHost);
        // In-process/unknown transports may have no peer IP. They never acquire forwarding trust from headers.
        app.UseWhen(context => IsTrusted(context.Connection.RemoteIpAddress, proxy.Networks),
            branch => branch.UseForwardedHeaders(options));
        return app;
    }

    private static bool IsTrusted(IPAddress? address, IPNetwork[] networks)
    {
        if (address is null) return false;
        return networks.Any(network => network.Contains(address)
            || address.IsIPv4MappedToIPv6 && network.BaseAddress.AddressFamily == AddressFamily.InterNetwork
                && network.Contains(address.MapToIPv4()));
    }

    private static Uri TrustedUri(string? value, string setting, bool development)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || !(uri.Scheme == "https" || development && uri.Scheme == "http" && uri.IsLoopback)
            || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new InvalidOperationException($"{setting} must be a trusted HTTPS URL (loopback HTTP is permitted for dev/qa fixtures).");
        return uri;
    }

    private sealed record ProxyConfiguration(Uri Resource, IPNetwork[] Networks);
}
