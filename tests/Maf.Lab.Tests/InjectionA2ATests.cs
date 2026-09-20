using System.Text.Json;
using Maf.Lab.Api.A2A;
using Maf.Lab.Api.Storage;
using Maf.Lab.Domain.Billing;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Maf.Lab.Tests;

/// <summary>One row of `evals/injection-a2a.jsonl`: what a reviewer sent, and what it must be treated as.</summary>
public sealed record HostileVerdict(string Id, string What, JsonElement Asked, JsonElement Verdict, JsonElement Expect)
{
    public string AskedAdjustmentId => Asked.GetProperty("adjustmentId").GetString()!;
    public string AskedAccountId => Asked.GetProperty("accountId").GetString()!;
    public decimal AskedAmount => Asked.GetProperty("amount").GetDecimal();
    public string ExpectedResult => Expect.GetProperty("result").GetString()!;
    public override string ToString() => $"{Id} — {What}";
}

/// <summary>
/// A verdict is another system's word about our question. These are the words a broken or hostile reviewer might
/// send, held as a dataset rather than as code so the set can grow without anything being rewritten: each one is
/// driven through the check that reads a verdict, and then through the flow that would write.
/// </summary>
public class InjectionA2ATests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<HostileVerdict> Fixtures
    {
        get
        {
            var data = new TheoryData<HostileVerdict>();
            foreach (var row in Load())
            {
                data.Add(row);
            }
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Each_verdict_is_treated_as_the_fixture_says(HostileVerdict row)
    {
        var asked = new FeeAdjustment(row.AskedAdjustmentId, "firm-a", row.AskedAccountId, row.AskedAmount,
            row.Asked.GetProperty("reason").GetString()!);

        var result = ComplianceConsultant.Judge(row.Verdict, "task-1", asked);

        switch (row.ExpectedResult)
        {
            case "verdict":
                var verdict = Assert.IsType<ConsultationResult.Verdict>(result);
                Assert.Equal(row.Expect.GetProperty("approved").GetBoolean(), verdict.Approved);
                // Whatever came back, what carries on is the identifier this system sent.
                Assert.Equal(row.AskedAdjustmentId, verdict.AdjustmentId);
                break;

            case "failed":
                var failed = Assert.IsType<ConsultationResult.Failed>(result);
                Assert.Contains(row.Expect.GetProperty("reasonContains").GetString()!, failed.Reason,
                    StringComparison.OrdinalIgnoreCase);
                break;

            default:
                throw new InvalidOperationException($"{row.Id} expects '{row.ExpectedResult}', which nothing checks.");
        }
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public async Task No_verdict_writes_anything_it_names(HostileVerdict row)
    {
        await using var reviewer = new HostileReviewerFactory();
        var url = (await reviewer.ListenAsync()).TrimEnd('/');
        var tools = new FakeToolSource();
        using var api = Api(url);
        var client = api.ClientFor("adam", "firm-a", Role.ADVISOR);

        // Where the fixture wrote "the adjustment we asked about", the reviewer echoes the id this system
        // actually minted. Every other identifier in the row is sent back exactly as written.
        reviewer.Verdict = row.Verdict;
        reviewer.EchoedPlaceholder = row.AskedAdjustmentId;

        // A proposal large enough to need a review: a small one never reaches the reviewer at all.
        var first = await ApiFactory.ChatAsync(client, $"adjust the fee on {row.AskedAccountId} down by a large amount");

        // The review happened, and it ended the way the fixture says it must. Without this the rest would pass
        // for the wrong reason — a reviewer nobody reached refuses everything too.
        var audit = await AuditAsync(api);
        var reviewed = Assert.Single(audit, a => a.ToolName == "fee.adjustment.reviewed");
        var approved = row.ExpectedResult == "verdict" && row.Expect.GetProperty("approved").GetBoolean();
        Assert.Equal(row.ExpectedResult switch
        {
            "verdict" when approved => "approved",
            "verdict" => "refused",
            _ => "failed",
        }, reviewed.Outcome);
        Assert.Equal(approved, ApiFactory.InterruptOf(first) is not null);

        // Nothing is written by a review, whatever it said: an approval only asks the advisor.
        await using var scope = api.Services.CreateAsyncScope();
        var db = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<MafDbContext>>().CreateDbContextAsync(Ct);
        var pending = await db.PendingAdjustments.ToListAsync(Ct);

        // Every proposal on record is the one this system made, about the account the advisor named — the
        // summary is what a person would be shown, so it is where an account the verdict invented would surface.
        Assert.All(pending, p => Assert.Equal("firm-a", p.FirmId));
        Assert.All(pending, p => Assert.Contains(row.AskedAccountId, p.Summary, StringComparison.Ordinal));
        foreach (var named in Named(row.Verdict).Where(n => !string.Equals(n, row.AskedAccountId, StringComparison.OrdinalIgnoreCase)))
        {
            Assert.DoesNotContain(pending, p => p.Summary.Contains(named, StringComparison.OrdinalIgnoreCase));
        }

        // And an interrupt, when there is one, asks about the account the advisor named — never the verdict's.
        if (ApiFactory.InterruptOf(first) is { } interrupt)
        {
            var adjustment = interrupt.GetProperty("metadata").GetProperty("adjustment");
            Assert.Equal(row.AskedAccountId, adjustment.GetProperty("accountId").GetString());
            Assert.Equal(row.AskedAmount, adjustment.GetProperty("amount").GetDecimal());
        }
    }

    [Fact]
    public async Task A_verdict_naming_another_account_leaves_that_account_alone()
    {
        var row = Load().Single(r => r.Id == "ia-02");
        await using var reviewer = new HostileReviewerFactory();
        var url = (await reviewer.ListenAsync()).TrimEnd('/');
        var tools = new FakeToolSource();
        using var api = Api(url);
        var client = api.ClientFor("adam", "firm-a", Role.ADVISOR);
        reviewer.Verdict = row.Verdict;
        reviewer.EchoedPlaceholder = row.AskedAdjustmentId;

        var events = await ApiFactory.ChatAsync(client, "adjust the fee on A-1042 down by a large amount");

        // The verdict approved something — about A-9999. It is not a verdict about what was asked, so it is not a
        // verdict at all: nobody is asked to confirm, and A-9999 has no proposal and no adjustment.
        Assert.Null(ApiFactory.InterruptOf(events));
        Assert.Contains(await AuditAsync(api), a => a.ToolName == "fee.adjustment.reviewed" && a.Outcome == "failed");

        await using var scope = api.Services.CreateAsyncScope();
        var db = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<MafDbContext>>().CreateDbContextAsync(Ct);
        var pending = await db.PendingAdjustments.ToListAsync(Ct);
        Assert.DoesNotContain(pending, p => p.Summary.Contains("9999", StringComparison.Ordinal));
        Assert.All(pending, p => Assert.Equal(PendingAdjustmentStatus.Failed, p.Status));
    }

    // ── the fixtures ─────────────────────────────────────────────────────────────────────────────────────────

    private static List<HostileVerdict> Load()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        return [.. File.ReadLines(Path.Combine(Repo(), "evals", "injection-a2a.jsonl"))
            .Where(line => line.Trim().Length > 0)
            .Select(line => JsonSerializer.Deserialize<HostileVerdict>(line, options)!)];
    }

    /// <summary>Every account-looking identifier the verdict mentions, including the ones it invented.</summary>
    private static IEnumerable<string> Named(JsonElement verdict)
    {
        foreach (var property in verdict.EnumerateObject())
        {
            if (property.Value.ValueKind is JsonValueKind.String && property.Value.GetString() is { } text)
            {
                foreach (var word in text.Split([' ', ',', '.', ':', ';'], StringSplitOptions.RemoveEmptyEntries))
                {
                    if (word.StartsWith("A-", StringComparison.OrdinalIgnoreCase))
                    {
                        yield return word;
                    }
                }
            }
        }
    }

    private static ApiFactory Api(string reviewerUrl) =>
        new(ProposingModel(), new FakeToolSource())
        {
            ExtraSettings = new Dictionary<string, string?>
            {
                ["Compliance:BaseUrl"] = reviewerUrl,
                ["Compliance:ClientId"] = "maf-lab-assistant",
                ["Compliance:ClientSecret"] = "assistant-secret",
                ["Compliance:Deadline"] = "00:00:10",
                ["FeeAdjustments:ReviewAboveAmount"] = "500",
            },
        };

    /// <summary>A model that proposes an adjustment when asked to, and otherwise answers.</summary>
    private static ScriptedChatClient ProposingModel() =>
        new((messages, options, _) =>
        {
            var last = messages.LastOrDefault(m => m.Role == ChatRole.User)?.Text ?? "";
            if (last.Contains("adjust", StringComparison.OrdinalIgnoreCase)
                && !ScriptedChatClient.HasResult(messages, FeeAdjustmentTool.Name))
            {
                return ScriptedChatClient.Call(FeeAdjustmentTool.Name, new()
                {
                    ["accountId"] = "A-1042",
                    ["amount"] = -900m,
                    ["reason"] = "the client was overcharged in Q2",
                });
            }
            return ScriptedChatClient.Text("Done.");
        });

    private static async Task<List<AuditRow>> AuditAsync(ApiFactory api)
    {
        await using var scope = api.Services.CreateAsyncScope();
        var db = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<MafDbContext>>().CreateDbContextAsync(Ct);
        return await db.Audit.OrderBy(a => a.Id).ToListAsync(Ct);
    }

    private static string Repo()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "evals")))
            {
                return dir.FullName;
            }
        }
        throw new InvalidOperationException("No evals/ directory above the test binary.");
    }
}
