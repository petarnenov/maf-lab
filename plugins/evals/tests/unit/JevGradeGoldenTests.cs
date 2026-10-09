extern alias service;
using service::Maf.Lab.Eval;
using service::Maf.Lab.Eval.Judging;
using System.Text.Json;
using Maf.Lab.Api.Agent;
using Maf.Lab.Api.Agent.Decisions;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Retrieval.Configuration;
using Maf.Lab.Retrieval.Rerank;
using Maf.Lab.Retrieval.Store;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Maf.Lab.Tests;

/// <summary>
/// Golden files (introduce-provider-plugins, Q8): the exact body every core call site sends to <c>POST /v1/systemone</c>,
/// recorded from main before the decision engine moved behind <c>IDecisionEngine</c> (eefaa3e), and sent now through
/// this plugin's <see cref="JevDecisionEngine"/>. Every body must stay byte-identical, so what Jev reads — its questions,
/// its state, the pinned model — and therefore every measured threshold stay as they are. The files are in this
/// plugin's <c>tests/unit/Golden/</c>; <c>MAF_UPDATE_GOLDEN=1</c> rewrites them, which is a change to what Jev reads and
/// needs its evals.
/// </summary>
public partial class JevGoldenRequestTests
{
    [Fact]
    public async Task An_eval_grade_sends_the_recorded_request()
    {
        var jev = new FakeJev();
        var grader = new DecisionGrader(JevSupport.Engine(jev), new JudgeOptions(), NullLogger<DecisionGrader>.Instance);

        await grader.GradeAsync(new GradeInput("What do I do when a fee schedule is missing?",
            "Assign the agreed schedule. Then re-run the run.",
            [new ReadItem("doc:fees›Missing", "fees › Missing: Assign the agreed schedule, then re-run.", [], "billing")],
            ["Assign the agreed schedule.", "Re-run the run."]), Ct);

        AssertGolden("eval-grade", Assert.Single(jev.Requests).Body);
    }

}
