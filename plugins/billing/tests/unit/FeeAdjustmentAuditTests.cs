using Maf.Lab.Api.Storage;
using Maf.Lab.Domain.Billing;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Plugins.Billing;
using Maf.Lab.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Maf.Lab.Tests;

/// <summary>
/// Billing's record of a fee adjustment's steps, through the write-confirmation seam (generalize-write-confirmation): the
/// kinds and outcomes exactly as they were before the seam, now recorded by billing's own flow.
/// </summary>
public class FeeAdjustmentAuditTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ScriptedChatClient ProposingModel() =>
        new((messages, options, _) =>
        {
            var last = messages.LastOrDefault(m => m.Role == ChatRole.User)?.Text ?? "";
            if (last.Contains("adjust", StringComparison.OrdinalIgnoreCase) && !ScriptedChatClient.HasResult(messages, FeeAdjustmentTool.Name))
            {
                return ScriptedChatClient.Call(FeeAdjustmentTool.Name, new()
                {
                    ["accountId"] = "A-1042",
                    ["amount"] = -200m,
                    ["reason"] = "the client was overcharged in Q2",
                });
            }
            return ScriptedChatClient.Text("Done.");
        });

    private static ApiFactory Api(FakeToolSource tools) => new(ProposingModel(), tools)
    {
        ExtraSettings = new Dictionary<string, string?> { ["Compliance:BaseUrl"] = "" },
        // Billing's own flow for the fakes' write tool, in place of the core fixture's.
        ConfigureTestServices = FeeAdjustmentFlow.Install,
    };

    /// <summary>A run that pauses on a proposal. The interrupt's id is what an answer names.</summary>
    private static async Task<(string ConversationId, string AdjustmentId)> ProposeAsync(HttpClient client)
    {
        var events = await ApiFactory.ChatAsync(client, "adjust the fee on A-1042 down by 200");
        var interrupt = ApiFactory.InterruptOf(events);
        Assert.NotNull(interrupt);
        return (ApiFactory.ThreadOf(events), interrupt.Value.GetProperty("id").GetString()!);
    }

    /// <summary>The answer: a run that resumes the interrupt.</summary>
    private static Task<List<SseEvent>> AnswerAsync(HttpClient client, string conversationId, string adjustmentId, bool approve) =>
        ApiFactory.ResumeAsync(client, conversationId, adjustmentId, approve);

    [Fact]
    public async Task Both_the_answer_and_what_came_of_it_are_recorded_against_the_person()
    {
        var tools = new FakeToolSource();
        using var api = Api(tools);
        var client = api.ClientFor("adam", "firm-a", Role.USER);
        var (conversationId, adjustmentId) = await ProposeAsync(client);

        await AnswerAsync(client, conversationId, adjustmentId, approve: true);

        await using var scope = api.Services.CreateAsyncScope();
        var db = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<MafDbContext>>().CreateDbContextAsync(Ct);
        var steps = await db.Audit.Where(a => a.Kind == FeeAdjustmentFlow.AuditKind).OrderBy(a => a.Id).ToListAsync(Ct);

        Assert.Equal(
            ["fee.adjustment.proposed", "fee.adjustment.confirmed", "fee.adjustment.applied"],
            steps.Select(s => s.ToolName));
        // The kinds and outcomes as they were before the seam (generalize-write-confirmation): the flow records them now.
        Assert.Equal(["proposed", "approved", "applied"], steps.Select(s => s.Outcome));
        Assert.All(steps, s => Assert.Equal("adam", s.PrincipalId));
        Assert.All(steps, s => Assert.DoesNotContain("overcharged", s.Arguments, StringComparison.OrdinalIgnoreCase));

        var report = Maf.Lab.Api.Compliance.AuditChain.Verify(await db.Audit.OrderBy(a => a.Id).ToListAsync(Ct));
        Assert.True(report.Intact);
    }

    [Fact]
    public async Task A_rejection_is_recorded_as_a_rejection()
    {
        var tools = new FakeToolSource();
        using var api = Api(tools);
        var client = api.ClientFor("adam", "firm-a", Role.USER);
        var (conversationId, adjustmentId) = await ProposeAsync(client);

        await AnswerAsync(client, conversationId, adjustmentId, approve: false);

        await using var scope = api.Services.CreateAsyncScope();
        var db = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<MafDbContext>>().CreateDbContextAsync(Ct);
        var steps = await db.Audit.Where(a => a.Kind == FeeAdjustmentFlow.AuditKind).OrderBy(a => a.Id).ToListAsync(Ct);

        Assert.Equal(["fee.adjustment.proposed", "fee.adjustment.rejected"], steps.Select(s => s.ToolName));
        Assert.Equal(["proposed", "rejected"], steps.Select(s => s.Outcome));
        Assert.DoesNotContain(steps, s => s.ToolName == "fee.adjustment.applied");
    }
}
