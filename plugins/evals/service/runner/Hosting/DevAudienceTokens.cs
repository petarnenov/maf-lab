using System.Net.Http.Json;
using System.Text.Json;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Plugins.Abstractions;
using Microsoft.Extensions.Configuration;

namespace Maf.Lab.Eval.Hosting;

/// <summary>Dev/qa harness credentials come from the dev-login endpoint, one audience at a time.</summary>
internal sealed class DevAudienceTokens(IHttpClientFactory clients, IConfiguration configuration) : IPluginTokens, IPluginAccess
{
    private string Gateway => (configuration["Evals:GatewayBaseUrl"] ?? "http://localhost:7171").TrimEnd('/');
    public async Task<string> ForAsync(Principal principal, string plugin, string subjectToken, CancellationToken ct) =>
        await MintAsync(principal, plugin, ct);

    public async Task<string> MintAsync(Principal principal, string audience, CancellationToken ct)
    {
        using var response = await clients.CreateClient("dev-audience-tokens").PostAsJsonAsync(
            configuration["Evals:DevTokenEndpoint"] ?? Gateway + "/dev/token",
            new { userId = principal.UserId, tenantId = principal.TenantId.Value, role = principal.Role.ToString(), audience }, ct);
        if (!response.IsSuccessStatusCode) throw new UnauthorizedAccessException("The development issuer refused the audience token.");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        if (body.ValueKind != JsonValueKind.Object || !body.TryGetProperty("token", out var value)
            || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
            throw new InvalidOperationException("The development issuer returned no token.");
        return value.GetString()!;
    }

    public async Task<PluginAccessSnapshot> For(Principal principal, CancellationToken ct)
    {
        var token = await MintAsync(principal, "api", ct);
        using var request = new HttpRequestMessage(HttpMethod.Get, Gateway + "/api/plugins");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        using var response = await clients.CreateClient("dev-audience-tokens").SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        if (body.ValueKind != JsonValueKind.Object || !body.TryGetProperty("plugins", out var plugins)
            || plugins.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("The gateway returned no plugin snapshot.");
        var names = new List<string>();
        foreach (var plugin in plugins.EnumerateArray())
        {
            if (plugin.ValueKind != JsonValueKind.Object || !plugin.TryGetProperty("name", out var name)
                || name.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(name.GetString()))
                throw new InvalidOperationException("The gateway returned an invalid plugin name.");
            names.Add(name.GetString()!);
        }
        return new(names);
    }
}
