// A partner's client, built from nothing but the agent card and the Agent Framework's A2A client. It shares no
// code with the service: if this works, an outside system's would too.
//
//   dotnet run --project tools/Maf.Lab.A2AProbe -- http://localhost:7171 acme-portal <secret>
using System.Net.Http.Headers;
using System.Net.Http.Json;
using A2A;
using Microsoft.Agents.AI;

var baseUrl = args.Length > 0 ? args[0] : "http://localhost:7171";
var clientId = args.Length > 1 ? args[1] : "acme-portal";
var secret = args.Length > 2 ? args[2] : "acme-portal-dev-secret";
var failures = new List<string>();

void Check(bool ok, string what, string detail = "")
{
    Console.WriteLine($"{(ok ? "  ok " : "FAIL")}  {what}{(detail.Length > 0 ? " — " + detail : "")}");
    if (!ok)
    {
        failures.Add(what);
    }
}

using var anonymous = new HttpClient { BaseAddress = new Uri(baseUrl) };

// Discovery first: the card is how a partner learns where to talk and how to authenticate.
var card = await new A2ACardResolver(new Uri(baseUrl), anonymous).GetAgentCardAsync();
Check(card.Name is { Length: > 0 }, "the card is discoverable", card.Name ?? "");
Check(card.Skills is { Count: > 0 }, "the card lists skills", string.Join(", ", card.Skills?.Select(s => s.Id) ?? []));
Check(card.SecuritySchemes is { Count: > 0 }, "the card says how to authenticate");

// …and the card's security scheme is client credentials, so that is what this client does.
var tokenResponse = await anonymous.PostAsJsonAsync("/a2a/token", new { clientId, clientSecret = secret });
tokenResponse.EnsureSuccessStatusCode();
var token = (await tokenResponse.Content.ReadFromJsonAsync<TokenResponse>())!.AccessToken;
Check(token is { Length: > 0 }, "a partner token is issued");

using var authenticated = new HttpClient { BaseAddress = new Uri(baseUrl) };
authenticated.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

// The remote agent as an AIAgent: from here on this is ordinary Agent Framework code.
AIAgent agent = await new A2ACardResolver(new Uri(baseUrl), authenticated).GetAIAgentAsync(authenticated);
Check(agent.Name is { Length: > 0 }, "the card became an agent", agent.Name ?? "");

var session = await agent.CreateSessionAsync();
var answer = await agent.RunAsync("status of run 4417", session);
Check(answer.Text.Contains("4417", StringComparison.Ordinal), "a question is answered", Trim(answer.Text));

var started = await agent.RunAsync("start a billing run for firm-a 2026-06", session);
Check(started.Text.Length > 0 || started.Messages.Count > 0, "a run can be started", Trim(started.Text));

var refused = await agent.RunAsync("start a billing run for firm-b 2026-06", await agent.CreateSessionAsync());
Check(!refused.Text.Contains("firm-b", StringComparison.OrdinalIgnoreCase), "another firm's run tells us nothing",
    Trim(refused.Text));

Console.WriteLine();
if (failures.Count > 0)
{
    Console.WriteLine($"{failures.Count} check(s) failed:");
    failures.ForEach(f => Console.WriteLine($"  - {f}"));
    return 1;
}
Console.WriteLine("every check passed");
return 0;

static string Trim(string text) => text.Length > 70 ? text[..70] + "…" : text;

internal sealed record TokenResponse(string AccessToken, string TokenType, int ExpiresIn, string Scope);
