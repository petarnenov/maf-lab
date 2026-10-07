using Maf.Lab.Plugins.Abstractions;
using System.Text.Json;
using Maf.Lab.Api.Storage;
using Maf.Lab.Domain.Billing;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Domain.Tracing;
using Maf.Lab.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Maf.Lab.Tests;

/// <summary>
/// A verdict is another system's word about our question. These are the words a broken or hostile reviewer might send,
/// held as a dataset (`evals/injection-a2a.jsonl`). How each is read off the wire is the compliance plugin's to test;
/// here, billing's flow is given the result each one is read as (at the reviewer-consultation port, through
/// <see cref="ScriptedReviewer"/>), and nothing it would write may name anything the verdict invented.
/// </summary>
public class InjectionA2ATests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<HostileVerdict> Fixtures => HostileVerdicts.Fixtures();

    /// <summary>The result the compliance plugin reads each row as (its own tests hold it to the fixture's expectation).</summary>
    private static ConsultationResult ReadAs(HostileVerdict row, ReviewRequest asked) => row.ExpectedResult switch
    {
        "verdict" => new ConsultationResult.Verdict(ScriptedReviewer.TaskId, asked.AdjustmentId,
            row.Expect.GetProperty("approved").GetBoolean(), ReasonOf(row.Verdict)),
        "failed" => new ConsultationResult.Failed(ScriptedReviewer.TaskId, row.Expect.GetProperty("reasonContains").GetString()!),
        _ => throw new InvalidOperationException($"{row.Id} expects '{row.ExpectedResult}', which nothing checks."),
    };

    private static string ReasonOf(JsonElement verdict) =>
        verdict.TryGetProperty("reason", out var reason) && reason.ValueKind == JsonValueKind.String ? reason.GetString()! : "";

    [Theory]
    [MemberData(nameof(Fixtures))]
    public async Task No_verdict_writes_anything_it_names(HostileVerdict row)
    {
        using var api = Api(new ScriptedReviewer { Answer = asked => ReadAs(row, asked) });
        var client = api.ClientFor("adam", "firm-a", Role.USER);

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
        var pending = await db.PendingWrites.ToListAsync(Ct);

        // Every proposal on record is the one this system made, about the account the advisor named — the
        // summary is what a person would be shown, so it is where an account the verdict invented would surface.
        Assert.All(pending, p => Assert.Equal("firm-a", p.TenantId));
        Assert.All(pending, p => Assert.Contains(row.AskedAccountId, p.Summary, StringComparison.Ordinal));
        foreach (var named in Named(row.Verdict).Where(n => !string.Equals(n, row.AskedAccountId, StringComparison.OrdinalIgnoreCase)))
        {
            Assert.DoesNotContain(pending, p => p.Summary.Contains(named, StringComparison.OrdinalIgnoreCase));
        }

        // And an interrupt, when there is one, asks about the account the advisor named — never the verdict's.
        if (ApiFactory.InterruptOf(first) is { } interrupt)
        {
            var adjustment = interrupt.GetProperty("metadata").GetProperty("summary");
            Assert.Equal(row.AskedAccountId, adjustment.GetProperty("accountId").GetString());
            Assert.Equal(row.AskedAmount, adjustment.GetProperty("amount").GetDecimal());
        }
    }

    [Fact]
    public async Task A_verdict_naming_another_account_leaves_that_account_alone()
    {
        var row = HostileVerdicts.Load().Single(r => r.Id == "ia-02");
        using var api = Api(new ScriptedReviewer { Answer = asked => ReadAs(row, asked) });
        var client = api.ClientFor("adam", "firm-a", Role.USER);

        var events = await ApiFactory.ChatAsync(client, "adjust the fee on A-1042 down by a large amount");

        // The verdict approved something — about A-9999. It is not a verdict about what was asked, so it is not a
        // verdict at all: nobody is asked to confirm, and A-9999 has no proposal and no adjustment.
        Assert.Null(ApiFactory.InterruptOf(events));
        Assert.Contains(await AuditAsync(api), a => a.ToolName == "fee.adjustment.reviewed" && a.Outcome == "failed");

        await using var scope = api.Services.CreateAsyncScope();
        var db = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<MafDbContext>>().CreateDbContextAsync(Ct);
        var pending = await db.PendingWrites.ToListAsync(Ct);
        Assert.DoesNotContain(pending, p => p.Summary.Contains("9999", StringComparison.Ordinal));
        Assert.All(pending, p => Assert.Equal(PendingWriteStatus.Failed, p.Status));
    }

    [Fact]
    public async Task A_verdict_whose_words_carry_an_instruction_is_not_believed()
    {
        // ia-01 is structurally a valid approval; only its words give it away. The content guard reads them.
        var row = HostileVerdicts.Load().Single(r => r.Id == "ia-01");
        using var api = Api(new ScriptedReviewer { Answer = asked => ReadAs(row, asked) });
        api.Jev.Guard = (text, id) => id == "guard_to_ai" && text.Contains("SYSTEM:", StringComparison.Ordinal) ? 0.97 : 0.01;
        var client = api.ClientFor("adam", "firm-a", Role.USER);

        var events = await ApiFactory.ChatAsync(client, "adjust the fee on A-1042 down by a large amount");

        // Not believed: nobody is asked to confirm, the review is recorded as failed, nothing exists for A-9999.
        Assert.Null(ApiFactory.InterruptOf(events));
        Assert.Contains(await AuditAsync(api), a => a.ToolName == "fee.adjustment.reviewed" && a.Outcome == "failed");
        await using var scope = api.Services.CreateAsyncScope();
        var db = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<MafDbContext>>().CreateDbContextAsync(Ct);
        var pending = await db.PendingWrites.ToListAsync(Ct);
        Assert.All(pending, p => Assert.Equal(PendingWriteStatus.Failed, p.Status));
        Assert.DoesNotContain(pending, p => p.Summary.Contains("9999", StringComparison.Ordinal));

        var trace = ApiFactory.TracesOf(events).ToList();
        var guard = trace.Single(t => t.GetProperty("kind").GetString() == TraceKinds.Guardrail
            && t.GetProperty("data").GetProperty("check").GetString() == Maf.Lab.Api.Agent.Guardrail.CheckReviewer).GetProperty("data");
        Assert.Equal("withheld", guard.GetProperty("decision").GetString());
        // And the reviewer's words never reached the model.
        var modelSaw = string.Join("\n", api.Chat.Requests.SelectMany(r => r.Messages).Select(m => m.Text));
        Assert.DoesNotContain("A-9999", modelSaw);
    }

    [Fact]
    public async Task A_refusal_whose_words_cannot_be_screened_stands_and_its_reason_stays_away_from_the_model()
    {
        using var api = Api(new ScriptedReviewer { Answer = asked => new ConsultationResult.Verdict(ScriptedReviewer.TaskId,
            asked.AdjustmentId, false, "Refused: REASON-CANARY-551 exceeds the firm's credit limit.") });
        // Jev is down for everything: the turn fails open, the reviewer's words fail closed.
        api.Jev.Status = System.Net.HttpStatusCode.ServiceUnavailable;
        var client = api.ClientFor("adam", "firm-a", Role.USER);

        var events = await ApiFactory.ChatAsync(client, "adjust the fee on A-1042 down by a large amount");

        Assert.Null(ApiFactory.InterruptOf(events));
        Assert.Contains(await AuditAsync(api), a => a.ToolName == "fee.adjustment.reviewed" && a.Outcome == "refused");
        var modelSaw = string.Join("\n", api.Chat.Requests.SelectMany(r => r.Messages).SelectMany(m => m.Contents)
            .OfType<FunctionResultContent>().Select(r => r.Result?.ToString()));
        Assert.Contains("refused", modelSaw);
        Assert.Contains(Maf.Lab.Api.Agent.Guardrail.ReviewerWordsWithheld, modelSaw);
        Assert.DoesNotContain("REASON-CANARY-551", modelSaw);
    }

    [Fact]
    public async Task With_the_circuit_open_a_refusals_words_are_still_withheld()
    {
        using var api = Api(new ScriptedReviewer { Answer = asked => new ConsultationResult.Verdict(ScriptedReviewer.TaskId,
            asked.AdjustmentId, false, "Refused: REASON-CANARY-552 exceeds the firm's credit limit.") }, ApiFactory.OpensOnFirstFailure);
        // The first Jev failure opens the circuit: the reviewer's words are skipped, not screened, and still fail closed.
        api.Jev.Status = System.Net.HttpStatusCode.ServiceUnavailable;
        var client = api.ClientFor("adam", "firm-a", Role.USER);

        var events = await ApiFactory.ChatAsync(client, "adjust the fee on A-1042 down by a large amount");

        var modelSaw = string.Join("\n", api.Chat.Requests.SelectMany(r => r.Messages).SelectMany(m => m.Contents)
            .OfType<FunctionResultContent>().Select(r => r.Result?.ToString()));
        Assert.Contains(Maf.Lab.Api.Agent.Guardrail.ReviewerWordsWithheld, modelSaw);
        Assert.DoesNotContain("REASON-CANARY-552", modelSaw);
        var guard = ApiFactory.TracesOf(events).Single(t => t.GetProperty("kind").GetString() == TraceKinds.Guardrail
            && t.GetProperty("data").GetProperty("check").GetString() == Maf.Lab.Api.Agent.Guardrail.CheckReviewer).GetProperty("data");
        Assert.Equal("unscreened", guard.GetProperty("decision").GetString());
        Assert.Equal("circuit open", guard.GetProperty("reason").GetString());
        Assert.Equal(0, guard.GetProperty("requests").GetInt32());
    }

    // ── the fixtures ─────────────────────────────────────────────────────────────────────────────────────────

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

    /// <summary>Billing's own flow for the fakes' write tool, consulting the given reviewer double at the port.</summary>
    private static ApiFactory Api(ScriptedReviewer reviewer, IReadOnlyDictionary<string, string?>? extra = null) =>
        new(ProposingModel(), new FakeToolSource())
        {
            ExtraSettings = new Dictionary<string, string?>
            {
                ["FeeAdjustments:ReviewAboveAmount"] = "500",
            }.Concat(extra ?? new Dictionary<string, string?>()).ToDictionary(),
            ConfigureTestServices = services =>
            {
                Maf.Lab.Plugins.Billing.FeeAdjustmentFlow.Install(services);
                services.AddSingleton<IReviewerConsultation>(reviewer);
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

}
