using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Maf.Lab.A2A;
using Maf.Lab.ComplianceAgent;
using Maf.Lab.Domain.Configuration;

namespace Maf.Lab.Tests;

/// <summary>
/// The reviewer as a caller meets it: one skill that says it is simulated, a verdict that is structured, a
/// question when it wants one, and a door that is shut to anyone without the right token.
/// </summary>
public class ComplianceAgentTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<HttpClient> CallerAsync(ComplianceFactory agent)
    {
        var client = agent.CreateClient();
        var response = await client.PostAsJsonAsync("/a2a/token",
            new A2AEndpoints.TokenRequest("maf-lab-assistant", "assistant-secret"), Ct);
        response.EnsureSuccessStatusCode();
        var token = (await response.Content.ReadFromJsonAsync<A2AEndpoints.TokenResponse>(Ct))!.AccessToken;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static object Review(decimal amount = 250m, string adjustmentId = "ADJ-1", string? taskId = null) => new
    {
        message = new
        {
            kind = "message",
            messageId = Guid.NewGuid().ToString("N"),
            role = "user",
            parts = new object[]
            {
                new { kind = "data", data = new { adjustmentId, firmId = "firm-a", accountId = "ACC-1042", amount, reason = "Overcharged in Q2" } },
            },
            taskId,
        },
    };

    private static async Task<JsonElement> RpcAsync(HttpClient client, string method, object @params)
    {
        var response = await client.PostAsJsonAsync("/a2a", new { jsonrpc = "2.0", id = 1, method, @params }, Ct);
        var body = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync(Ct));
        Assert.False(body.TryGetProperty("error", out var error), error.ToString());
        return body.GetProperty("result");
    }

    private static JsonElement Verdict(JsonElement task) =>
        task.GetProperty("artifacts")[0].GetProperty("parts")[0].GetProperty("data");

    [Fact]
    public async Task The_card_offers_exactly_one_skill_and_says_it_is_simulated()
    {
        await using var agent = new ComplianceFactory();

        var card = await agent.CreateClient().GetFromJsonAsync<JsonElement>(AgentCardFactory.WellKnownPath, Ct);

        var skill = Assert.Single(card.GetProperty("skills").EnumerateArray());
        Assert.Equal(ComplianceAgentCard.SkillId, skill.GetProperty("id").GetString());
        var description = skill.GetProperty("description").GetString()!;
        Assert.Contains("simulated", description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Do not use", description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("maf-lab compliance reviewer", card.GetProperty("name").GetString()!);
    }

    [Fact]
    public async Task Only_a_caller_with_a_token_for_this_agent_gets_in()
    {
        await using var agent = new ComplianceFactory();
        var anonymous = agent.CreateClient();

        var refused = await anonymous.PostAsJsonAsync("/a2a",
            new { jsonrpc = "2.0", id = 1, method = "message/send" }, Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);

        // A token minted for the billing assistant's own A2A surface is for a different audience.
        var billing = new A2AOptions { Audience = "maf-lab-a2a", Partners = { ["acme-portal"] = new PartnerRegistration() } };
        var (foreign, _) = PartnerJwt.Issue(new AuthOptions(), billing, "acme-portal", [A2AScopes.BillingRead]);
        anonymous.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", foreign);
        var wrongAudience = await anonymous.PostAsJsonAsync("/a2a",
            new { jsonrpc = "2.0", id = 1, method = "message/send" }, Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, wrongAudience.StatusCode);
    }

    [Fact]
    public async Task A_review_completes_with_a_structured_verdict()
    {
        await using var agent = new ComplianceFactory();
        var client = await CallerAsync(agent);

        var task = await RpcAsync(client, "message/send", Review(amount: 250m));

        Assert.Equal("completed", task.GetProperty("status").GetProperty("state").GetString());
        var verdict = Verdict(task);
        Assert.Equal("approved", verdict.GetProperty("decision").GetString());
        Assert.Equal("ADJ-1", verdict.GetProperty("adjustmentId").GetString());
        Assert.False(string.IsNullOrWhiteSpace(verdict.GetProperty("reason").GetString()));
        Assert.True(verdict.GetProperty("simulated").GetBoolean());
        Assert.Equal(ReviewAgentHandler.ArtifactName, task.GetProperty("artifacts")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task A_review_reports_progress_before_it_answers()
    {
        await using var agent = new ComplianceFactory { ReviewMs = 40 };
        var client = await CallerAsync(agent);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/a2a")
        {
            Content = JsonContent.Create(new { jsonrpc = "2.0", id = 1, method = "message/stream", @params = Review() }),
        };
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, Ct);
        var states = new List<string>();
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(Ct));
        while (await reader.ReadLineAsync(Ct) is { } line)
        {
            if (!line.StartsWith("data: ", StringComparison.Ordinal))
            {
                continue;
            }
            var @event = JsonDocument.Parse(line[6..]).RootElement.GetProperty("result");
            if (@event.TryGetProperty("status", out var status))
            {
                states.Add(status.GetProperty("state").GetString()!);
            }
        }

        Assert.Equal("submitted", states[0]);
        Assert.Contains("working", states);
        Assert.Equal("completed", states[^1]);
        // Progress, not one silent jump: the caller sees the review happening.
        Assert.True(states.Count(s => s == "working") > 1, string.Join(" → ", states));
    }

    [Fact]
    public async Task An_adjustment_over_the_threshold_is_refused_with_the_reason_why()
    {
        await using var agent = new ComplianceFactory { RefuseAbove = 100m };
        var client = await CallerAsync(agent);

        var task = await RpcAsync(client, "message/send", Review(amount: 2_500m, adjustmentId: "ADJ-BIG"));

        var verdict = Verdict(task);
        Assert.Equal("refused", verdict.GetProperty("decision").GetString());
        Assert.Contains("100", verdict.GetProperty("reason").GetString()!);
        Assert.Equal("ADJ-BIG", verdict.GetProperty("adjustmentId").GetString());
    }

    [Fact]
    public async Task Anything_that_is_not_a_fee_adjustment_is_declined_without_a_verdict()
    {
        await using var agent = new ComplianceFactory();
        var client = await CallerAsync(agent);

        var answer = await RpcAsync(client, "message/send", new
        {
            message = new
            {
                kind = "message",
                messageId = Guid.NewGuid().ToString("N"),
                role = "user",
                parts = new[] { new { kind = "text", text = "What is the status of billing run 4417?" } },
            },
        });

        Assert.Equal("message", answer.GetProperty("kind").GetString());
        Assert.Equal(ReviewAgentHandler.OutOfScope, answer.GetProperty("parts")[0].GetProperty("text").GetString());
        Assert.False(answer.TryGetProperty("artifacts", out _));
    }

    [Fact]
    public async Task The_reviewer_can_ask_for_a_justification_and_then_decide()
    {
        await using var agent = new ComplianceFactory { AskRate = 1 };
        var client = await CallerAsync(agent);

        var asked = await RpcAsync(client, "message/send", Review());
        Assert.Equal("input-required", asked.GetProperty("status").GetProperty("state").GetString());
        Assert.Contains("justification",
            asked.GetProperty("status").GetProperty("message").GetProperty("parts")[0].GetProperty("text").GetString()!,
            StringComparison.OrdinalIgnoreCase);

        var taskId = asked.GetProperty("id").GetString()!;
        var answered = await RpcAsync(client, "message/send", Review(taskId: taskId));

        // The same review, continued — not a second one, and not asked again.
        Assert.Equal(taskId, answered.GetProperty("id").GetString());
        Assert.Equal("completed", answered.GetProperty("status").GetProperty("state").GetString());
        Assert.Equal("approved", Verdict(answered).GetProperty("decision").GetString());
    }

    [Fact]
    public async Task With_the_rate_at_zero_it_never_asks()
    {
        await using var agent = new ComplianceFactory { AskRate = 0 };
        var client = await CallerAsync(agent);

        for (var i = 0; i < 5; i++)
        {
            var task = await RpcAsync(client, "message/send", Review(adjustmentId: $"ADJ-{i}"));
            Assert.Equal("completed", task.GetProperty("status").GetProperty("state").GetString());
        }
    }
}
