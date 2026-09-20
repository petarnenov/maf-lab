using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using A2A;
using Microsoft.Agents.AI;

namespace Maf.Lab.A2AProbe;

/// <summary>What a scenario has to work with: an unauthenticated client, an authenticated one, and the card.</summary>
public sealed class ProbeContext
{
    public required Uri BaseUri { get; init; }
    public required HttpClient Anonymous { get; init; }
    public required HttpClient Authenticated { get; init; }
    public required IA2AClient Client { get; init; }
    public required AgentCard PublicCard { get; init; }
    /// <summary>The host the *agent* should call back on. Not this process's own name when the agent is in a container.</summary>
    public required string PushHost { get; init; }
    public required string ReviewerUrl { get; init; }
    public required string ReviewerSecret { get; init; }
}

/// <summary>
/// One runner per scenario name in `evals/a2a-conformance.jsonl`. Nothing here decides *what* to check — the row
/// says that — and a name with no runner is reported as a failure, because a scenario nobody runs that reads as a
/// pass is worse than no scenario at all.
/// </summary>
public static class Scenarios
{
    public delegate Task<ConformanceOutcome> Runner(ProbeContext context, ConformanceRow row, CancellationToken ct);

    public static readonly IReadOnlyDictionary<string, Runner> All = new Dictionary<string, Runner>(StringComparer.Ordinal)
    {
        ["card-discovery"] = CardDiscoveryAsync,
        ["extended-card"] = ExtendedCardAsync,
        ["direct-message"] = DirectMessageAsync,
        ["streamed-task"] = StreamedTaskAsync,
        ["resubscribe"] = ResubscribeAsync,
        ["resume-input-required"] = ResumeAsync,
        ["cancel"] = CancelAsync,
        ["push-delivery"] = PushDeliveryAsync,
        ["foreign-firm-refused"] = ForeignFirmAsync,
        ["reviewer-consultation"] = ReviewerAsync,
    };

    /// <summary>
    /// Runs the scenario a row names. A name nothing implements is a failure, not a skip: a dataset that can
    /// grow a claim the probe never checks, and still report every scenario green, would be worse than no
    /// dataset at all.
    /// </summary>
    public static async Task<ConformanceOutcome> RunAsync(ProbeContext context, ConformanceRow row, CancellationToken ct)
    {
        if (!All.TryGetValue(row.Scenario, out var runner))
        {
            return new ConformanceOutcome(row.Id, row.Scenario, false, "",
                $"no runner answers to '{row.Scenario}' (known: {string.Join(", ", All.Keys)})");
        }
        try
        {
            return await runner(context, row, ct);
        }
        catch (Exception ex)
        {
            // An agent that is not there is a legitimate state — but the probe says so rather than passing quietly.
            return new ConformanceOutcome(row.Id, row.Scenario, false, "", $"{ex.GetType().Name}: {Trim(ex.Message)}");
        }
    }

    // ── the scenarios ────────────────────────────────────────────────────────────────────────────────────────

    private static Task<ConformanceOutcome> CardDiscoveryAsync(ProbeContext context, ConformanceRow row, CancellationToken ct)
    {
        var card = context.PublicCard;
        var ids = card.Skills?.Select(s => s.Id ?? "").ToHashSet(StringComparer.Ordinal) ?? [];
        var missing = Strings(row, "skills").Where(s => !ids.Contains(s)).ToList();
        var leaked = Strings(row, "absentSkills").Where(ids.Contains).ToList();

        var scheme = Text(row, "securityScheme");
        var schemeMissing = scheme is { Length: > 0 } && card.SecuritySchemes?.ContainsKey(scheme) != true;

        var advertised = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (card.Capabilities?.Streaming == true) { advertised.Add("streaming"); }
        if (card.Capabilities?.PushNotifications == true) { advertised.Add("pushNotifications"); }
        if (card.Capabilities?.ExtendedAgentCard == true) { advertised.Add("extendedAgentCard"); }
        var unadvertised = Strings(row, "capabilities").Where(c => !advertised.Contains(c)).ToList();

        var problems = new List<string>();
        if (missing.Count > 0) { problems.Add($"the card omits {string.Join(", ", missing)}"); }
        if (leaked.Count > 0) { problems.Add($"the public card names the private skill {string.Join(", ", leaked)}"); }
        if (schemeMissing) { problems.Add($"the card offers no '{scheme}' security scheme"); }
        if (unadvertised.Count > 0) { problems.Add($"the card claims neither {string.Join(" nor ", unadvertised)}"); }

        return Task.FromResult(Verdict(row, problems, $"{card.Name}: {string.Join(", ", ids)}"));
    }

    private static async Task<ConformanceOutcome> ExtendedCardAsync(ProbeContext context, ConformanceRow row, CancellationToken ct)
    {
        var card = await context.Client.GetExtendedAgentCardAsync(new GetExtendedAgentCardRequest(), ct);
        var ids = card.Skills?.Select(s => s.Id ?? "").ToHashSet(StringComparer.Ordinal) ?? [];
        var missing = Strings(row, "skills").Where(s => !ids.Contains(s)).ToList();

        var problems = new List<string>();
        if (missing.Count > 0) { problems.Add($"authenticating did not reveal {string.Join(", ", missing)}"); }
        return Verdict(row, problems, string.Join(", ", ids));
    }

    private static async Task<ConformanceOutcome> DirectMessageAsync(ProbeContext context, ConformanceRow row, CancellationToken ct)
    {
        var response = await context.Client.SendMessageAsync(Ask(row.Message ?? ""), ct);
        var said = Said(response);
        var missing = Strings(row, "contains")
            .Where(s => !said.Contains(s, StringComparison.OrdinalIgnoreCase)).ToList();

        var problems = new List<string>();
        if (missing.Count > 0) { problems.Add($"the answer never mentions {string.Join(", ", missing)}"); }
        return Verdict(row, problems, Trim(said));
    }

    private static async Task<ConformanceOutcome> StreamedTaskAsync(ProbeContext context, ConformanceRow row, CancellationToken ct)
    {
        var seen = new List<string>();
        AgentTask? task = null;
        Artifact? artifact = null;

        await foreach (var frame in context.Client.SendStreamingMessageAsync(Ask(row.Message ?? ""), ct))
        {
            task = frame.Task ?? task;
            Note(seen, frame);
            artifact ??= frame.ArtifactUpdate?.Artifact;
            if (Terminal(frame)) { break; }
        }
        foreach (var each in task?.Artifacts ?? []) { artifact ??= each; }

        var problems = Missing(seen, Strings(row, "states"));
        var wanted = Text(row, "artifact");
        if (wanted is { Length: > 0 })
        {
            if (artifact is null)
            {
                problems.Add($"no '{wanted}' artifact was produced");
            }
            else
            {
                if (!string.Equals(artifact.Name, wanted, StringComparison.Ordinal))
                {
                    problems.Add($"the artifact is named '{artifact.Name}', not '{wanted}'");
                }
                var data = artifact.Parts?.Select(p => p.Data).FirstOrDefault(d => d is not null);
                var absent = Strings(row, "artifactFields")
                    .Where(f => data?.TryGetProperty(f, out _) != true).ToList();
                if (absent.Count > 0) { problems.Add($"the artifact carries no {string.Join(", ", absent)}"); }
            }
        }
        return Verdict(row, problems, string.Join(" → ", seen));
    }

    private static async Task<ConformanceOutcome> ResubscribeAsync(ProbeContext context, ConformanceRow row, CancellationToken ct)
    {
        // Read just far enough to have a running task, then walk away from the stream the way a dropped
        // connection does. The run belongs to the task, not to this socket.
        string? taskId = null;
        using (var dropped = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            await foreach (var frame in context.Client.SendStreamingMessageAsync(Ask(row.Message ?? ""), dropped.Token))
            {
                taskId ??= frame.Task?.Id ?? frame.StatusUpdate?.TaskId;
                if (State(frame) is "Working" && taskId is not null)
                {
                    await dropped.CancelAsync();
                    break;
                }
            }
        }
        if (taskId is null)
        {
            return Verdict(row, ["the stream never named a task"], "");
        }

        var seen = new List<string>();
        var first = "";
        await foreach (var frame in context.Client.SubscribeToTaskAsync(new SubscribeToTaskRequest { Id = taskId }, ct))
        {
            first = first is { Length: > 0 } ? first : frame.Task is not null ? "task" : "update";
            Note(seen, frame);
            if (Terminal(frame)) { break; }
        }

        var problems = Missing(seen, Strings(row, "states"));
        var wanted = Text(row, "firstEvent");
        if (wanted is { Length: > 0 } && !string.Equals(first, wanted, StringComparison.Ordinal))
        {
            problems.Add($"a resubscription opened with '{first}', not the task as it stands");
        }
        return Verdict(row, problems, $"{taskId[..8]}… {string.Join(" → ", seen)}");
    }

    private static async Task<ConformanceOutcome> ResumeAsync(ProbeContext context, ConformanceRow row, CancellationToken ct)
    {
        var asked = await context.Client.SendMessageAsync(Ask(row.Message ?? ""), ct);
        var task = asked.Task;
        var problems = new List<string>();
        var wanted = Text(row, "askedState");
        if (wanted is { Length: > 0 } && $"{task?.Status?.State}" != wanted)
        {
            problems.Add($"the task went to {task?.Status?.State}, not {wanted}, so there was nothing to resume");
            return Verdict(row, problems, Trim(Said(asked)));
        }

        // The answer goes back under the same task: that is what makes it a continuation rather than a new run.
        var resumed = await context.Client.SendMessageAsync(
            Ask(row.Reply ?? "", task?.Id, task?.ContextId), ct);
        var seen = new List<string> { $"{resumed.Task?.Status?.State}" };

        problems.AddRange(Missing(seen, Strings(row, "states")));
        if (Flag(row, "sameTaskId") && resumed.Task?.Id != task?.Id)
        {
            problems.Add("the continuation started a new task instead of finishing the one that asked");
        }
        return Verdict(row, problems, $"{task?.Status?.State} → {resumed.Task?.Status?.State}");
    }

    private static async Task<ConformanceOutcome> CancelAsync(ProbeContext context, ConformanceRow row, CancellationToken ct)
    {
        string? taskId = null;
        using (var dropped = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            await foreach (var frame in context.Client.SendStreamingMessageAsync(Ask(row.Message ?? ""), dropped.Token))
            {
                taskId ??= frame.Task?.Id ?? frame.StatusUpdate?.TaskId;
                if (State(frame) is "Working" && taskId is not null)
                {
                    await dropped.CancelAsync();
                    break;
                }
            }
        }
        if (taskId is null)
        {
            return Verdict(row, ["the stream never named a task"], "");
        }

        var cancelled = await context.Client.CancelTaskAsync(new CancelTaskRequest { Id = taskId }, ct);
        var seen = new List<string> { $"{cancelled.Status?.State}" };
        return Verdict(row, Missing(seen, Strings(row, "states")), $"{taskId[..8]}… {cancelled.Status?.State}");
    }

    private static async Task<ConformanceOutcome> PushDeliveryAsync(ProbeContext context, ConformanceRow row, CancellationToken ct)
    {
        using var receiver = new PushReceiver();
        var problems = new List<string>();

        // A configuration belongs to a task, so there has to be one first. A run that stops to ask for its period
        // is a task that exists and is not finished — exactly the moment to register a webhook.
        var asked = await context.Client.SendMessageAsync(Ask(row.Message ?? ""), ct);
        var task = asked.Task;
        if (task?.Id is not { Length: > 0 } taskId)
        {
            return Verdict(row, ["no task to register a webhook against"], Trim(Said(asked)));
        }

        var token = Guid.NewGuid().ToString("N");
        var url = $"http://{context.PushHost}:{receiver.Port}/push";
        var created = await context.Client.CreateTaskPushNotificationConfigAsync(new CreateTaskPushNotificationConfigRequest
        {
            TaskId = taskId,
            Config = new PushNotificationConfig { Url = url, Token = token },
        }, ct);

        if (Flag(row, "configRoundTrip"))
        {
            var listed = await context.Client.ListTaskPushNotificationConfigAsync(
                new ListTaskPushNotificationConfigRequest { TaskId = taskId }, ct);
            if (listed.Configs?.Any(c => c.Id == created.Id) != true)
            {
                problems.Add("a registered webhook is not listed back");
            }
        }

        // Finishing the run is what there is to be told about.
        await context.Client.SendMessageAsync(Ask(row.Reply ?? "", taskId, task.ContextId), ct);

        var deliveries = await receiver.AwaitAsync(Strings(row, "deliveredStates").Count, TimeSpan.FromSeconds(30), ct);
        foreach (var state in Strings(row, "deliveredStates"))
        {
            if (!deliveries.Any(d => d.Body.Contains($"\"{state}\"", StringComparison.OrdinalIgnoreCase)))
            {
                problems.Add($"nothing was delivered for '{state}'");
            }
        }
        if (Flag(row, "tokenEchoed") && !deliveries.Any(d => d.Token == token))
        {
            problems.Add("the delivery did not carry the caller's own token back");
        }
        if (deliveries.Any(d => !d.Body.Contains(taskId, StringComparison.Ordinal)))
        {
            problems.Add("a delivery named a different task");
        }

        await context.Client.DeleteTaskPushNotificationConfigAsync(
            new DeleteTaskPushNotificationConfigRequest { TaskId = taskId, Id = created.Id }, ct);

        return Verdict(row, problems, $"{deliveries.Count} delivery/-ies at {url}");
    }

    private static async Task<ConformanceOutcome> ForeignFirmAsync(ProbeContext context, ConformanceRow row, CancellationToken ct)
    {
        var response = await context.Client.SendMessageAsync(Ask(row.Message ?? ""), ct);
        var said = Said(response);
        var problems = Strings(row, "absent")
            .Where(s => said.Contains(s, StringComparison.OrdinalIgnoreCase))
            .Select(s => $"the refusal repeats '{s}'")
            .ToList();
        if (Flag(row, "noArtifact") && response.Task?.Artifacts is { Count: > 0 })
        {
            problems.Add("a firm the partner may not see still produced an artifact");
        }
        return Verdict(row, problems, Trim(said));
    }

    /// <summary>The second agent, reached the way the assistant reaches it: card, own credentials, one review.</summary>
    private static async Task<ConformanceOutcome> ReviewerAsync(ProbeContext context, ConformanceRow row, CancellationToken ct)
    {
        var problems = new List<string>();

        // The reviewer is served under a prefix, and the resolver appends the well-known path to the origin: ask
        // for the card by its full path or the billing agent's card comes back.
        var reviewerUri = new Uri(context.ReviewerUrl);
        var origin = new Uri(reviewerUri.GetLeftPart(UriPartial.Authority));
        var cardPath = $"{reviewerUri.AbsolutePath.TrimEnd('/')}/.well-known/agent-card.json";
        using var anonymous = new HttpClient { BaseAddress = new Uri(context.ReviewerUrl) };
        var card = await new A2ACardResolver(origin, anonymous, cardPath).GetAgentCardAsync(ct);
        if (row.Expect.TryGetProperty("skills", out var count) && count.ValueKind is JsonValueKind.Number
            && card.Skills?.Count != count.GetInt32())
        {
            problems.Add($"the reviewer's card offers {card.Skills?.Count ?? 0} skills, not {count.GetInt32()}");
        }

        // An absolute path would drop the prefix and reach the *other* agent's token endpoint.
        var issued = await anonymous.PostAsJsonAsync($"{context.ReviewerUrl.TrimEnd('/')}/a2a/token",
            new { clientId = "maf-lab-assistant", clientSecret = context.ReviewerSecret }, ct);
        issued.EnsureSuccessStatusCode();
        var access = (await issued.Content.ReadFromJsonAsync<TokenResponse>(ct))!.AccessToken;

        using var asAssistant = new HttpClient { BaseAddress = new Uri(context.ReviewerUrl) };
        asAssistant.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", access);
        var agent = await new A2ACardResolver(origin, asAssistant, cardPath).GetAIAgentAsync(asAssistant, cancellationToken: ct);
        if (agent.GetService(typeof(IA2AClient)) is not IA2AClient client)
        {
            return Verdict(row, ["the reviewer's card did not become an A2A client"], agent.Name ?? "");
        }

        var review = await client.SendMessageAsync(new SendMessageRequest
        {
            Message = new Message
            {
                MessageId = Guid.NewGuid().ToString("N"),
                Role = A2A.Role.User,
                Parts =
                [
                    new Part
                    {
                        Data = JsonSerializer.SerializeToElement(new
                        {
                            adjustmentId = "ADJ-PROBE",
                            firmId = "firm-a",
                            accountId = "ACC-1042",
                            amount = 250m,
                            reason = "Overcharged in Q2",
                        }),
                    },
                ],
            },
        }, ct);

        var state = $"{review.Task?.Status?.State}";
        var allowed = Strings(row, "states");
        if (allowed.Count > 0 && !allowed.Contains(state))
        {
            problems.Add($"the review ended in {state}, which is neither {string.Join(" nor ", allowed)}");
        }
        else if (state == "Completed")
        {
            var field = Text(row, "verdictField") ?? "decision";
            var verdict = review.Task!.Artifacts?.SelectMany(a => a.Parts ?? []).Select(p => p.Data)
                .FirstOrDefault(d => d is not null && d.Value.TryGetProperty(field, out _));
            if (verdict is null)
            {
                problems.Add($"the review carried no structured '{field}'");
            }
            else if (Flag(row, "simulated") && verdict.Value.TryGetProperty("simulated", out var simulated)
                && !simulated.GetBoolean())
            {
                problems.Add("the verdict does not say it is simulated");
            }
        }
        return Verdict(row, problems, state);
    }

    // ── the small shared pieces ──────────────────────────────────────────────────────────────────────────────

    private static SendMessageRequest Ask(string text, string? taskId = null, string? contextId = null) => new()
    {
        Message = new Message
        {
            MessageId = Guid.NewGuid().ToString("N"),
            Role = A2A.Role.User,
            TaskId = taskId,
            ContextId = contextId,
            Parts = [new Part { Text = text }],
        },
    };

    /// <summary>Everything the agent said, wherever it put it.</summary>
    private static string Said(SendMessageResponse response)
    {
        var parts = new List<string?>();
        parts.AddRange(response.Message?.Parts?.Select(p => p.Text) ?? []);
        parts.AddRange(response.Task?.Status?.Message?.Parts?.Select(p => p.Text) ?? []);
        parts.AddRange(response.Task?.History?.SelectMany(m => m.Parts ?? []).Select(p => p.Text) ?? []);
        return string.Join(" ", parts.Where(p => p is { Length: > 0 }));
    }

    private static string? State(StreamResponse frame) =>
        frame.StatusUpdate?.Status?.State.ToString() ?? frame.Task?.Status?.State.ToString();

    private static void Note(List<string> seen, StreamResponse frame)
    {
        if (State(frame) is { Length: > 0 } state && (seen.Count == 0 || seen[^1] != state))
        {
            seen.Add(state);
        }
    }

    private static bool Terminal(StreamResponse frame) => State(frame) is
        "Completed" or "Canceled" or "Failed" or "Rejected" or "InputRequired" or "AuthRequired";

    private static List<string> Missing(IReadOnlyList<string> seen, IReadOnlyList<string> wanted) =>
        wanted.Where(w => !seen.Contains(w, StringComparer.Ordinal))
            .Select(w => $"the task never reached {w} (it went {string.Join(" → ", seen)})")
            .ToList();

    private static ConformanceOutcome Verdict(ConformanceRow row, IReadOnlyList<string> problems, string detail) =>
        new(row.Id, row.Scenario, problems.Count == 0, detail,
            problems.Count == 0 ? null : string.Join("; ", problems));

    private static IReadOnlyList<string> Strings(ConformanceRow row, string key) =>
        row.Expect.ValueKind is JsonValueKind.Object && row.Expect.TryGetProperty(key, out var value)
            && value.ValueKind is JsonValueKind.Array
            ? [.. value.EnumerateArray().Select(v => v.GetString() ?? "")]
            : [];

    private static string? Text(ConformanceRow row, string key) =>
        row.Expect.ValueKind is JsonValueKind.Object && row.Expect.TryGetProperty(key, out var value)
            && value.ValueKind is JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool Flag(ConformanceRow row, string key) =>
        row.Expect.ValueKind is JsonValueKind.Object && row.Expect.TryGetProperty(key, out var value)
            && value.ValueKind is JsonValueKind.True;

    internal static string Trim(string text) => text.Length > 70 ? text[..70] + "…" : text;
}

/// <summary>A webhook the agent can call back on, for exactly as long as one scenario needs it.</summary>
public sealed class PushReceiver : IDisposable
{
    private readonly HttpListener listener = new();
    private readonly List<Delivery> received = [];
    private readonly Lock gate = new();

    public sealed record Delivery(string Body, string? Token);

    public PushReceiver()
    {
        Port = FreePort();
        listener.Prefixes.Add($"http://+:{Port}/");
        listener.Start();
        _ = Task.Run(AcceptAsync);
    }

    public int Port { get; }

    /// <summary>Waits for the expected number of deliveries, then a moment longer in case more follow.</summary>
    public async Task<IReadOnlyList<Delivery>> AwaitAsync(int expected, TimeSpan within, CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow + within;
        while (DateTimeOffset.UtcNow < deadline && Count < expected)
        {
            await Task.Delay(200, ct);
        }
        lock (gate)
        {
            return [.. received];
        }
    }

    private int Count
    {
        get { lock (gate) { return received.Count; } }
    }

    private async Task AcceptAsync()
    {
        while (listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync();
            }
            catch (Exception)
            {
                return; // the listener was disposed; the scenario is over
            }

            using var reader = new StreamReader(context.Request.InputStream);
            var body = await reader.ReadToEndAsync();
            lock (gate)
            {
                received.Add(new Delivery(body, context.Request.Headers["X-A2A-Notification-Token"]));
            }
            context.Response.StatusCode = 200;
            context.Response.Close();
        }
    }

    private static int FreePort()
    {
        using var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    public void Dispose()
    {
        listener.Close();
    }
}
