using System.Net;
using Maf.Lab.Api.A2A;
using Maf.Lab.Api.Compliance;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Maf.Lab.Tests;

/// <summary>Counts what the consultant actually sends, so caching is observed from the caller's side.</summary>
internal sealed class CountingHandler : DelegatingHandler
{
    private int cards;
    private int tokens;

    public int CardFetches => cards;
    public int TokenAttempts => tokens;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri?.AbsolutePath ?? "";
        if (path.EndsWith("agent-card.json", StringComparison.Ordinal))
        {
            Interlocked.Increment(ref cards);
        }
        else if (path.EndsWith("/a2a/token", StringComparison.Ordinal))
        {
            Interlocked.Increment(ref tokens);
        }
        return base.SendAsync(request, cancellationToken);
    }
}

/// <summary>
/// Consulting the reviewer over a real socket: a verdict, a refusal, a question back, a deadline that passes, and
/// an agent that is not there. Every one of them is a value the caller gets, and a record in the audit chain.
/// </summary>
public class ComplianceConsultantTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly FeeAdjustment Adjustment =
        new("ADJ-77", "firm-a", "ACC-1042", 250m, "Overcharged in Q2 after a fee schedule change");

    /// <summary>The reviewer, listening on a real port — the consultant uses a real HttpClient to reach it.</summary>
    private static async Task<(ComplianceFactory Agent, string Url)> ReviewerAsync(
        double askRate = 0, int reviewMs = 10, decimal refuseAbove = 1_000m, string pathBase = "")
    {
        var agent = new ComplianceFactory
        {
            AskRate = askRate, ReviewMs = reviewMs, RefuseAbove = refuseAbove, PathBase = pathBase,
        };
        var address = await agent.ListenAsync();
        return (agent, address.TrimEnd('/') + pathBase);
    }

    private static ApiFactory ApiFor(string? reviewerUrl, Action<Dictionary<string, string?>>? extra = null,
        CountingHandler? counter = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Compliance:BaseUrl"] = reviewerUrl ?? "",
            ["Compliance:ClientId"] = "maf-lab-assistant",
            ["Compliance:ClientSecret"] = "assistant-secret",
            ["Compliance:Deadline"] = "00:00:10",
        };
        extra?.Invoke(settings);
        return new ApiFactory(ApiFactory.ProceduralModel())
        {
            ExtraSettings = settings,
            ConfigureTestServices = counter is null
                ? null
                : services => services.AddHttpClient("a2a-consult").AddHttpMessageHandler(() => counter),
        };
    }

    private static ComplianceConsultant Consultant(ApiFactory api) =>
        api.Services.GetRequiredService<ComplianceConsultant>();

    [Fact]
    public async Task A_review_comes_back_as_a_verdict()
    {
        var (agent, url) = await ReviewerAsync();
        await using var _ = agent;
        using var api = ApiFor(url);

        var result = await Consultant(api).ReviewAsync(Adjustment, Ct);

        var verdict = Assert.IsType<ConsultationResult.Verdict>(result);
        Assert.True(verdict.Approved);
        Assert.Equal("ADJ-77", verdict.AdjustmentId);
        Assert.NotEmpty(verdict.Reason);
        Assert.NotEmpty(verdict.TaskId);
    }

    [Fact]
    public async Task An_agent_served_under_a_prefix_is_still_found_by_its_own_card()
    {
        // Two agents behind one entry point: the reviewer answers under /compliance, and asking the origin for a
        // card would fetch the *other* agent's. Found live, before this test existed.
        var (agent, url) = await ReviewerAsync(pathBase: "/compliance");
        await using var _ = agent;
        using var api = ApiFor(url);

        var result = await Consultant(api).ReviewAsync(Adjustment, Ct);

        Assert.IsType<ConsultationResult.Verdict>(result);
    }

    [Fact]
    public async Task A_refusal_is_a_verdict_too()
    {
        var (agent, url) = await ReviewerAsync(refuseAbove: 10m);
        await using var _ = agent;
        using var api = ApiFor(url);

        var result = await Consultant(api).ReviewAsync(Adjustment, Ct);

        var verdict = Assert.IsType<ConsultationResult.Verdict>(result);
        Assert.False(verdict.Approved);
        Assert.Contains("10", verdict.Reason);
    }

    [Fact]
    public async Task A_question_is_reported_as_a_question_and_can_be_answered()
    {
        var (agent, url) = await ReviewerAsync(askRate: 1);
        await using var _ = agent;
        using var api = ApiFor(url);
        var consultant = Consultant(api);

        var asked = Assert.IsType<ConsultationResult.QuestionAsked>(await consultant.ReviewAsync(Adjustment, Ct));
        Assert.Contains("justification", asked.Question, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(asked.TaskId);

        var answered = await consultant.AnswerAsync(Adjustment, asked.TaskId, "The client was billed twice in Q2.", Ct);

        var verdict = Assert.IsType<ConsultationResult.Verdict>(answered);
        Assert.Equal(asked.TaskId, verdict.TaskId);
    }

    [Fact]
    public async Task A_review_that_outlives_the_deadline_is_a_timeout_not_a_verdict()
    {
        var (agent, url) = await ReviewerAsync(reviewMs: 5_000);
        await using var _ = agent;
        using var api = ApiFor(url, s => s["Compliance:Deadline"] = "00:00:00.300");

        var result = await Consultant(api).ReviewAsync(Adjustment, Ct);

        Assert.IsType<ConsultationResult.TimedOut>(result);
    }

    [Fact]
    public async Task A_timeout_keeps_the_task_id_so_the_answer_can_be_collected_later()
    {
        var (agent, url) = await ReviewerAsync(reviewMs: 5_000);
        await using var _ = agent;
        using var api = ApiFor(url, s => s["Compliance:Deadline"] = "00:00:00.300");

        var result = await Consultant(api).ReviewAsync(Adjustment, Ct);

        var timedOut = Assert.IsType<ConsultationResult.TimedOut>(result);
        Assert.NotEqual("", timedOut.TaskId);

        // The review is still running over there, and that id reaches it.
        var answered = await Consultant(api).AnswerAsync(Adjustment, timedOut.TaskId, "the client agreed in writing", Ct);
        Assert.IsNotType<ConsultationResult.Unreachable>(answered);
    }

    [Fact]
    public async Task An_agent_that_is_not_there_is_unreachable()
    {
        // Port 9 discards: nothing answers, so discovery fails rather than hanging.
        using var api = ApiFor("http://127.0.0.1:9");

        var result = await Consultant(api).ReviewAsync(Adjustment, Ct);

        var unreachable = Assert.IsType<ConsultationResult.Unreachable>(result);
        Assert.NotEmpty(unreachable.Reason);
    }

    [Fact]
    public async Task Without_configuration_nothing_is_attempted()
    {
        using var api = ApiFor(reviewerUrl: null);

        var result = await Consultant(api).ReviewAsync(Adjustment, Ct);

        Assert.Contains("configured", Assert.IsType<ConsultationResult.Unreachable>(result).Reason);
    }

    [Fact]
    public async Task Without_credentials_nothing_is_attempted_either()
    {
        var (agent, url) = await ReviewerAsync();
        await using var _ = agent;
        using var api = ApiFor(url, s => s["Compliance:ClientSecret"] = "");

        var result = await Consultant(api).ReviewAsync(Adjustment, Ct);

        Assert.Contains("credentials", Assert.IsType<ConsultationResult.Unreachable>(result).Reason);
    }

    [Fact]
    public async Task The_reviewer_is_told_a_system_asked_and_never_who()
    {
        var (agent, url) = await ReviewerAsync();
        await using var _ = agent;
        using var api = ApiFor(url);

        await Consultant(api).ReviewAsync(Adjustment, Ct);

        // The reviewer logs the partner it authenticated; it is this system, and no user appears anywhere.
        var logs = string.Join('\n', agent.Logs.Messages);
        Assert.Contains("partner=maf-lab-assistant", logs);
        Assert.DoesNotContain("alice", logs, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Bearer", logs, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Every_consultation_is_recorded_without_its_content()
    {
        var (agent, url) = await ReviewerAsync();
        await using var _ = agent;
        using var api = ApiFor(url);
        var consultant = Consultant(api);

        await consultant.ReviewAsync(Adjustment, Ct);

        await using var db = ChatApiTests.Db(api);
        var row = Assert.Single(await db.Audit.Where(a => a.Kind == AuditKinds.A2AConsultation).ToListAsync(Ct));
        Assert.Equal(ComplianceConsultant.Operation, row.ToolName);
        Assert.Equal("approved", row.Outcome);
        Assert.Contains("agent=compliance", row.Arguments);
        Assert.Contains("adjustmentId=ADJ-77", row.Arguments);
        Assert.DoesNotContain("Overcharged", row.Arguments);
        Assert.DoesNotContain("ACC-1042", row.Arguments);
        Assert.True(row.DurationMs >= 0);
        Assert.True(AuditChain.Verify(await db.Audit.OrderBy(a => a.Id).ToListAsync(Ct)).Intact);
    }

    [Fact]
    public async Task A_failure_and_an_unreachable_agent_are_recorded_too()
    {
        using var api = ApiFor("http://127.0.0.1:9");

        await Consultant(api).ReviewAsync(Adjustment, Ct);

        await using var db = ChatApiTests.Db(api);
        var row = Assert.Single(await db.Audit.Where(a => a.Kind == AuditKinds.A2AConsultation).ToListAsync(Ct));
        Assert.Equal("unreachable", row.Outcome);
        Assert.Contains("taskId=-", row.Arguments);
    }

    [Fact]
    public async Task The_card_is_fetched_once_and_the_second_review_reuses_it()
    {
        var (agent, url) = await ReviewerAsync();
        await using var _ = agent;
        var counter = new CountingHandler();
        using var api = ApiFor(url, s => s["Compliance:CardCacheFor"] = "00:05:00", counter);
        var consultant = Consultant(api);

        await consultant.ReviewAsync(Adjustment, Ct);
        var afterFirst = counter.CardFetches;
        await consultant.ReviewAsync(Adjustment, Ct);

        Assert.Equal(1, afterFirst);
        Assert.Equal(1, counter.CardFetches);
    }

    [Fact]
    public async Task A_transport_failure_makes_the_next_consultation_look_again()
    {
        var counter = new CountingHandler();
        using var api = ApiFor("http://127.0.0.1:9", counter: counter);
        var consultant = Consultant(api);

        // Nothing is listening, so discovery fails — and a failed discovery must not be remembered as an answer.
        await consultant.ReviewAsync(Adjustment, Ct);
        await consultant.ReviewAsync(Adjustment, Ct);

        Assert.Equal(2, counter.TokenAttempts);
    }
}
