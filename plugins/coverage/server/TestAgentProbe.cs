using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using Maf.Lab.A2A;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Maf.Lab.Plugins.Coverage;

/// <summary>The test agent's one skill as its card states it.</summary>
public sealed record TestAgentSkillDto(string Id, string Name, string Description, IReadOnlyList<string> Tags);

/// <summary>What the test agent's public card says about it: who it is, what it does, where it answers, what it needs.</summary>
public sealed record TestAgentCardDto(string Name, string Description, string? Version, IReadOnlyList<TestAgentSkillDto> Skills,
    string? Endpoint, string? ProtocolVersion, IReadOnlyList<string> RequiredScopes, bool Streaming, bool PushNotifications);

/// <summary>
/// Whether the test agent answered its card just now. Reachable means the card answered, not that a run would succeed;
/// the reason is a short sentence, never a stack trace or an exception message.
/// </summary>
public sealed record TestAgentStatusDto(bool Configured, bool Reachable, string? Reason, long LatencyMs, DateTimeOffset CheckedAt);

public sealed record TestAgentCheck(TestAgentStatusDto Status, TestAgentCardDto? Card);

/// <summary>
/// Asks the test agent for its public card on the internal network, anonymously, within a short timeout, and reuses the
/// answer for a few seconds. A page view is not an operation on the agent: no token is fetched and nothing is audited.
/// </summary>
public sealed class TestAgentProbe(IHttpClientFactory http, IMemoryCache cache, IOptions<TestAgentOptions> options, TimeProvider time)
{
    public const string HttpClientName = "testagent-probe";

    public async Task<TestAgentCheck> CheckAsync(CancellationToken ct)
    {
        var opts = options.Value;
        if (string.IsNullOrWhiteSpace(opts.BaseUrl))
        {
            return new TestAgentCheck(new TestAgentStatusDto(false, false, "No test agent is configured.", 0, time.GetUtcNow()), null);
        }
        var key = $"testagent-probe:{opts.BaseUrl}";
        if (cache.TryGetValue(key, out TestAgentCheck? cached) && cached is not null)
        {
            return cached;
        }
        var check = await FetchAsync(opts, ct);
        cache.Set(key, check, opts.ProbeCacheFor);
        return check;
    }

    private async Task<TestAgentCheck> FetchAsync(TestAgentOptions opts, CancellationToken ct)
    {
        var watch = Stopwatch.StartNew();
        TestAgentCheck Unreachable(string reason) =>
            new(new TestAgentStatusDto(true, false, reason, watch.ElapsedMilliseconds, time.GetUtcNow()), null);

        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(ct);
        bounded.CancelAfter(opts.ProbeTimeout);
        try
        {
            using var response = await http.CreateClient(HttpClientName)
                .GetAsync($"{opts.BaseUrl.TrimEnd('/')}{AgentCardFactory.WellKnownPath}", bounded.Token);
            if (!response.IsSuccessStatusCode)
            {
                return Unreachable($"The card answered HTTP {(int)response.StatusCode}.");
            }
            await using var body = await response.Content.ReadAsStreamAsync(bounded.Token);
            using var document = await JsonDocument.ParseAsync(body, cancellationToken: bounded.Token);
            if (Parse(document.RootElement) is not { } card)
            {
                return Unreachable("The answer is not an agent card.");
            }
            return new TestAgentCheck(new TestAgentStatusDto(true, true, null, watch.ElapsedMilliseconds, time.GetUtcNow()), card);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Unreachable($"No answer within {opts.ProbeTimeout.TotalSeconds:0.#} s.");
        }
        catch (HttpRequestException ex)
        {
            return Unreachable(ex.HttpRequestError switch
            {
                HttpRequestError.NameResolutionError => "The agent's host name does not resolve.",
                HttpRequestError.ConnectionError => "The connection was refused.",
                _ => "The request for the card failed.",
            });
        }
        catch (JsonException)
        {
            return Unreachable("The answer is not an agent card.");
        }
    }

    /// <summary>The card in the specification's wire format, read leniently: a missing optional member is left empty.</summary>
    internal static TestAgentCardDto? Parse(JsonElement card)
    {
        if (card.ValueKind != JsonValueKind.Object || Text(card, "name") is not { Length: > 0 } name)
        {
            return null;
        }
        var skills = Array(card, "skills")
            .Select(s => new TestAgentSkillDto(Text(s, "id") ?? "", Text(s, "name") ?? "", Text(s, "description") ?? "",
                [.. Array(s, "tags").Where(t => t.ValueKind == JsonValueKind.String).Select(t => t.GetString()!)]))
            .ToList();
        var interfaces = Array(card, "supportedInterfaces").ToList();
        var rpc = interfaces.FirstOrDefault(i => string.Equals(Text(i, "protocolBinding"), "JSONRPC", StringComparison.OrdinalIgnoreCase));
        if (rpc.ValueKind != JsonValueKind.Object && interfaces.Count > 0)
        {
            rpc = interfaces[0];
        }
        var scopes = Array(card, "securityRequirements")
            .Where(r => r.ValueKind == JsonValueKind.Object && r.TryGetProperty("schemes", out var s) && s.ValueKind == JsonValueKind.Object)
            .SelectMany(r => r.GetProperty("schemes").EnumerateObject())
            .SelectMany(scheme => Array(scheme.Value, "list"))
            .Where(s => s.ValueKind == JsonValueKind.String)
            .Select(s => s.GetString()!)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var capabilities = card.TryGetProperty("capabilities", out var c) && c.ValueKind == JsonValueKind.Object ? c : default;
        return new TestAgentCardDto(name, Text(card, "description") ?? "", Text(card, "version"), skills,
            rpc.ValueKind == JsonValueKind.Object ? Text(rpc, "url") : null,
            rpc.ValueKind == JsonValueKind.Object ? Text(rpc, "protocolVersion") : null,
            scopes, Flag(capabilities, "streaming"), Flag(capabilities, "pushNotifications"));
    }

    private static string? Text(JsonElement e, string property) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static IEnumerable<JsonElement> Array(JsonElement e, string property) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray()
            : [];

    private static bool Flag(JsonElement e, string property) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.True;
}
