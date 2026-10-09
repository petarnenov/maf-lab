using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Plugins.Abstractions;
using Maf.Lab.Plugins.Compliance;
using Microsoft.Extensions.DependencyInjection;

namespace Maf.Lab.Tests;

/// <summary>
/// A verdict is another system's word about our question. These are the words a broken or hostile reviewer might send
/// (`evals/injection-a2a.jsonl`), and how the consultation reads each one: as the check that reads a verdict alone, and as
/// it arrives over the wire from a reviewer that sends it. What a write flow then does with the result it is read as is
/// that flow's own test (billing's, at the reviewer-consultation port).
/// </summary>
public class HostileVerdictTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<HostileVerdict> Fixtures => HostileVerdicts.Fixtures();

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Each_verdict_is_treated_as_the_fixture_says(HostileVerdict row)
    {
        var asked = Asked(row);

        AssertReadAsTheFixtureSays(row, ComplianceConsultant.Judge(row.Verdict, "task-1", asked), wording: true);
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public async Task Each_verdict_over_the_wire_is_read_as_the_fixture_says(HostileVerdict row)
    {
        await using var reviewer = new HostileReviewerFactory();
        var url = (await reviewer.ListenAsync()).TrimEnd('/');
        // Where the fixture wrote "the adjustment we asked about", the reviewer echoes the id this system sent; every other
        // identifier in the row is sent back exactly as written.
        reviewer.Verdict = row.Verdict;
        reviewer.EchoedPlaceholder = row.AskedAdjustmentId;
        using var api = new ApiFactory(ApiFactory.ProceduralModel())
        {
            InstalledPlugins = CompliancePluginSupport.Installed,
            ExtraSettings = new Dictionary<string, string?>
            {
                ["A2A:Clients:compliance:BaseUrl"] = url,
                ["A2A:Clients:compliance:ClientId"] = "maf-lab-assistant",
                ["A2A:Clients:compliance:ClientSecret"] = "assistant-secret",
                ["Compliance:Deadline"] = "00:00:10",
            },
        };
        using var scope = api.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<Maf.Lab.Api.Agent.Writes.WriteTurnContext>()
            .Set(new Principal("adam", TenantId.Firm("firm-a"), Role.USER), null, null, "", null);

        var result = await scope.ServiceProvider.GetRequiredService<IReviewerConsultation>().ReviewAsync(Asked(row), Ct);

        // Over the wire the result is held to its kind; the wording of a failure is the check's own (the test above).
        AssertReadAsTheFixtureSays(row, result, wording: false);
    }

    private static ReviewRequest Asked(HostileVerdict row) => new(row.AskedAdjustmentId, "firm-a", row.AskedAccountId,
        row.AskedAmount, row.Asked.GetProperty("reason").GetString()!);

    private static void AssertReadAsTheFixtureSays(HostileVerdict row, ConsultationResult result, bool wording)
    {
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
                if (wording)
                {
                    Assert.Contains(row.Expect.GetProperty("reasonContains").GetString()!, failed.Reason,
                        StringComparison.OrdinalIgnoreCase);
                }
                break;

            default:
                throw new InvalidOperationException($"{row.Id} expects '{row.ExpectedResult}', which nothing checks.");
        }
    }
}
