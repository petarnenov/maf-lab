// A partner's client, built from nothing but the agent card and the A2A client. It shares no code with the
// service: if this works, an outside system's would too. That independence is the whole point, so the scenarios
// it runs come from `evals/a2a-conformance.jsonl` rather than from a list in here, and the report it writes has
// the same shape every eval suite writes.
//
//   dotnet run --project tools/Maf.Lab.A2AProbe -- http://localhost:7171 acme-portal <secret>
using System.Net.Http.Headers;
using System.Net.Http.Json;
using A2A;
using Maf.Lab.A2AProbe;

var baseUrl = Arg(0, "http://localhost:7171");
var clientId = Arg(1, "acme-portal");
var secret = Arg(2, "acme-portal-dev-secret");
var reviewerUrl = Arg(3, $"{baseUrl.TrimEnd('/')}/compliance");
var reviewerSecret = Arg(4, "assistant-dev-secret");
var evalsRoot = Environment.GetEnvironmentVariable("MAF_EVALS_ROOT") ?? FindEvals();
var dataset = Path.Combine(evalsRoot, "a2a-conformance.jsonl");

// A callback has to name a host the *agent* can reach. In the stack the agent is in a container and this probe is
// not, so it is the container's name for the host — overridable for a local run, where it is just localhost.
var pushHost = Environment.GetEnvironmentVariable("MAF_PUSH_HOST") ?? "host.docker.internal";

using var anonymous = new HttpClient { BaseAddress = new Uri(baseUrl) };

// Discovery first: the card is how a partner learns where to talk and how to authenticate.
var card = await new A2ACardResolver(new Uri(baseUrl), anonymous).GetAgentCardAsync();

// …and the card's security scheme is client credentials, so that is what this client does.
var issued = await anonymous.PostAsJsonAsync("/a2a/token", new { clientId, clientSecret = secret });
issued.EnsureSuccessStatusCode();
var token = (await issued.Content.ReadFromJsonAsync<TokenResponse>())!.AccessToken;

using var authenticated = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromMinutes(5) };
authenticated.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

var endpoint = card.SupportedInterfaces?.FirstOrDefault(i => i.ProtocolBinding == ProtocolBindingNames.JsonRpc)?.Url
    ?? $"{baseUrl.TrimEnd('/')}/a2a";
using var client = new A2AClient(new Uri(endpoint), authenticated);

var context = new ProbeContext
{
    BaseUri = new Uri(baseUrl),
    Anonymous = anonymous,
    Authenticated = authenticated,
    Client = client,
    PublicCard = card,
    PushHost = pushHost,
    ReviewerUrl = reviewerUrl,
    ReviewerSecret = reviewerSecret,
};

var rows = Conformance.Load(dataset);
Console.WriteLine($"{rows.Count} scenario(s) from {dataset}");
Console.WriteLine($"against {endpoint} as {clientId}");
Console.WriteLine();

var startedAt = DateTimeOffset.UtcNow;
var outcomes = new List<ConformanceOutcome>();
foreach (var row in rows)
{
    outcomes.Add(await Scenarios.RunAsync(context, row, CancellationToken.None));
    var last = outcomes[^1];
    Console.WriteLine($"{(last.Passed ? "  ok " : "FAIL")}  {row.Id} {row.Scenario} — {row.What}");
    if (last.Detail is { Length: > 0 })
    {
        Console.WriteLine($"          {Scenarios.Trim(last.Detail)}");
    }
    if (!last.Passed)
    {
        Console.WriteLine($"          {last.Reason}");
    }
}

var report = Conformance.Build(outcomes, new Dictionary<string, string>
{
    ["baseUrl"] = baseUrl,
    ["partner"] = clientId,
    ["reviewer"] = reviewerUrl,
    ["agentVersion"] = card.Version ?? "",
    ["dataset"] = Path.GetFileName(dataset),
}, startedAt, DateTimeOffset.UtcNow);

var written = await Conformance.WriteAsync(evalsRoot, report, CancellationToken.None);
Console.WriteLine();
Console.WriteLine($"report: {written}");

var failed = outcomes.Where(o => !o.Passed).ToList();
if (failed.Count > 0)
{
    Console.WriteLine($"{failed.Count} scenario(s) failed:");
    failed.ForEach(f => Console.WriteLine($"  - {f.Id} {f.Scenario}: {f.Reason}"));
    return 1;
}
Console.WriteLine("every scenario passed");
return 0;

string Arg(int index, string fallback) => args.Length > index ? args[index] : fallback;

/// <summary>The repository's `evals/`, found from wherever the probe was started.</summary>
static string FindEvals()
{
    for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
    {
        var candidate = Path.Combine(dir.FullName, "evals");
        if (Directory.Exists(candidate))
        {
            return candidate;
        }
    }
    throw new InvalidOperationException("No evals/ directory found; set MAF_EVALS_ROOT.");
}

internal sealed record TokenResponse(string AccessToken, string TokenType, int ExpiresIn, string Scope);
