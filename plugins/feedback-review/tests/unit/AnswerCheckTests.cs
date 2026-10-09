using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Maf.Lab.Api.Agent.Decisions;
using Maf.Lab.Domain.Billing;
using Maf.Lab.Domain.Feedback;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Domain.Tracing;
using Maf.Lab.Plugins.Abstractions;
using Maf.Lab.TestSupport;
using Microsoft.Extensions.AI;

namespace Maf.Lab.Tests;

public partial class AnswerCheckTests
{
    [Fact]
    public async Task An_unsupported_answer_is_flagged_for_review()
    {
        var jev = new FakeJev { AnswerCheck = (id, _, _) => id == DecisionAnswerCheck.GroundedId ? 0.12 : 0.9 };
        using var api = new ApiFactory(ApiFactory.ProceduralModel(Marker), jev: jev) { InstalledPlugins = FeedbackReviewPluginSupport.Installed };

        var events = await ApiFactory.ChatAsync(api.ClientFor("adam", "firm-a", Role.USER), Procedural);

        Assert.Equal(Marker, ApiFactory.AnswerOf(events));
        var check = Trace(events).Single(t => t.Kind == TraceKinds.AnswerCheck);
        Assert.Equal("not_grounded", check.Data.GetProperty("verdict").GetString());
        Assert.EndsWith("grounded 0.12 < 0.30 — not grounded", check.Title);
        Assert.Contains(TurnSignal.AnswerNotGrounded, Signals(events));
        Assert.DoesNotContain(TurnSignal.AnswerNotRelevant, Signals(events));

        var queue = await api.ClientFor("alice", "firm-a", Role.TENANT_ADMIN).GetFromJsonAsync<List<ReviewQueueItem>>("/api/admin/feedback/queue", Json, Ct);
        Assert.Contains(queue!, q => q.Signals.Contains(TurnSignal.AnswerNotGrounded));
    }

}
