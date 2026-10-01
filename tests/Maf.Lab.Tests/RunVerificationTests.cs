using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Maf.Lab.Api.Coverage;
using Maf.Lab.Api.Storage;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.TestGen;
using Maf.Lab.TestGen.Coverage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maf.Lab.Tests;

/// <summary>GitHub's issues API as far as the lab uses it: open, list, comment, close.</summary>
internal sealed class FakeGitHub : HttpMessageHandler
{
    private int _next = 41;

    public ConcurrentDictionary<int, (string Title, string Body, string State)> Issues { get; } = new();
    public ConcurrentQueue<(int Number, string Body)> Comments { get; } = new();
    public ConcurrentQueue<string> Authorizations { get; } = new();
    public bool Down { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Authorizations.Enqueue(request.Headers.Authorization?.ToString() ?? "");
        if (Down)
        {
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        }
        var path = request.RequestUri!.AbsolutePath;
        var body = request.Content is null ? default : await request.Content.ReadFromJsonAsync<JsonElement>(ct);
        if (request.Method == HttpMethod.Post && path.EndsWith("/issues"))
        {
            var number = Interlocked.Increment(ref _next);
            Issues[number] = (body.GetProperty("title").GetString()!, body.GetProperty("body").GetString()!, "open");
            return Json(new { number, html_url = $"https://github.com/owner/repo/issues/{number}" });
        }
        if (request.Method == HttpMethod.Get && path.EndsWith("/issues"))
        {
            return Json(Issues.Select(i => new { number = i.Key, body = i.Value.Body, html_url = $"https://github.com/owner/repo/issues/{i.Key}" }));
        }
        var parts = path.Split('/');
        if (request.Method == HttpMethod.Post && path.EndsWith("/comments"))
        {
            Comments.Enqueue((int.Parse(parts[^2]), body.GetProperty("body").GetString()!));
            return Json(new { });
        }
        if (request.Method == HttpMethod.Patch)
        {
            var number = int.Parse(parts[^1]);
            Issues[number] = Issues[number] with { State = body.GetProperty("state").GetString()! };
            return Json(new { });
        }
        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
}

/// <summary>
/// What the api does with a completed run: verify it, file its bugs, branch it, and merge or discard it
/// (test-generation-runs: independent verification, suspected bugs, candidate branch, accept and discard).
/// </summary>
// Heavy (git, several hosts): run one after another rather than beside the timing-sensitive tests.
[Collection("TestGeneration")]
public sealed class RunVerificationTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private const string Target = "src/Lab/Calc.cs";

    private static Task<TempGitRepo> RepoAsync() => TempGitRepo.CreateAsync(new Dictionary<string, string>
    {
        [Target] = "namespace Lab;\npublic static class Calc { public static int Add(int a, int b) => a + b; }\n",
        ["tests/Lab.Tests/Existing.cs"] = "namespace Lab.Tests;\npublic class Existing { [Fact] public void Ok() { Assert.True(true); } }\n",
    }, Ct);

    private sealed record Harness(ApiFactory Api, TempGitRepo Repo, FakeCoverageRunner Runner, FakeGitHub GitHub) : IDisposable
    {
        public T Get<T>() where T : notnull => Api.Services.GetRequiredService<T>();

        public void Dispose() => Api.Dispose();
    }

    private static async Task<Harness> HarnessAsync(bool withToken = true, Func<RunnerRequest, RunnerResult>? runner = null)
    {
        var repo = await RepoAsync();
        var fakeRunner = new FakeCoverageRunner
        {
            Answer = runner ?? (r => FakeCoverageRunner.Result(FakeCoverageRunner.Report("/work/job", (Target, 9, 10)), targetPct: 90)),
        };
        var github = new FakeGitHub();
        var tokenVariable = $"MAF_TEST_GITHUB_{Guid.NewGuid():N}";
        if (withToken)
        {
            Environment.SetEnvironmentVariable(tokenVariable, "gh-token-for-tests");
        }
        var api = CoverageApi.Create(repo, fakeRunner, new Dictionary<string, string?>
        {
            ["GitHub:Repository"] = "owner/repo",
            ["GitHub:TokenVariable"] = tokenVariable,
        });
        var previous = api.ConfigureTestServices;
        api.ConfigureTestServices = s =>
        {
            previous?.Invoke(s);
            s.AddHttpClient(GitHubIssues.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => github);
        };
        await CoverageApi.IngestAsync(api, await repo.HeadAsync(Ct), Toolchains.Dotnet, SnapshotKind.Official, null, (Target, 5, 10));
        return new Harness(api, repo, fakeRunner, github);
    }

    private static string NewTest(string body) => $$"""
        diff --git a/tests/Lab.Tests/CalcTests.cs b/tests/Lab.Tests/CalcTests.cs
        new file mode 100644
        --- /dev/null
        +++ b/tests/Lab.Tests/CalcTests.cs
        @@ -0,0 +1,5 @@
        +namespace Lab.Tests;
        +public class CalcTests
        +{
        +    {{body}}
        +}

        """;

    private const string GoodTest = "[Fact] public void Adds() { Assert.Equal(2, Calc.Add(1, 1)); }";

    /// <summary>A run in the state a completed task leaves it: verifying, with the agent's report.</summary>
    private static async Task<string> VerifyingRunAsync(Harness h, string diff, double reported = 88, IReadOnlyList<SuspectedBug>? bugs = null,
        int? maxBugs = null)
    {
        var id = $"r_{Guid.NewGuid():N}";
        var report = new TestGenReport(TestGenKinds.Report, true, StopReason.Target, 85, 50, reported, [], new TestGenUsage(1, 1, 0.01), diff, bugs);
        await using var db = await h.Get<IDbContextFactory<MafDbContext>>().CreateDbContextAsync(Ct);
        var row = CoverageStorageTests.Run(id, Target, TestGenRunState.Verifying);
        row.CommitSha = await h.Repo.HeadAsync(Ct);
        row.TaskId = "task-1";
        // The test verifies it itself: a live lease keeps the host's own follower from verifying it at the same time.
        row.Follower = "this-test";
        row.FollowerHeartbeatAt = DateTime.UtcNow.AddHours(1);
        row.ReportJson = JsonSerializer.Serialize(report, TestGenKinds.Json);
        row.MaxSuspectedBugs = maxBugs;
        db.TestGenRuns.Add(row);
        await db.SaveChangesAsync(Ct);
        return id;
    }

    private static async Task<TestGenRunRow> VerifyAsync(Harness h, string runId)
    {
        await h.Get<IRunVerifier>().VerifyAsync(runId, Ct);
        return (await h.Get<TestGenRuns>().GetAsync(runId, Ct))!;
    }

    [Fact]
    public async Task A_verified_run_becomes_a_candidate_on_its_own_branch_at_the_measured_coverage()
    {
        using var h = await HarnessAsync(runner: _ => FakeCoverageRunner.Result(FakeCoverageRunner.Report("/work/job", (Target, 21, 25)), targetPct: 84));
        var run = await VerifyAsync(h, await VerifyingRunAsync(h, NewTest(GoodTest), reported: 88));

        Assert.Equal(TestGenRunState.Candidate, run.State);
        Assert.Equal(84.0, run.LastPct);
        // The candidate is what the runner measured, not what the agent said.
        Assert.Equal(84.0, (await h.Get<CoverageStore>().CandidatesAsync([run.Id], Ct)).Single().LinePct);
        Assert.Equal(50.0, (await h.Get<CoverageStore>().CurrentAsync(Target, Ct))!.Totals.LinePct);

        var log = (await h.Repo.GitAsync(Ct, "log", "--format=%s", $"{await h.Repo.HeadAsync(Ct)}..{run.Branch}")).Text.Trim().Split('\n');
        Assert.Single(log);
        var changed = (await h.Repo.GitAsync(Ct, "diff", "--name-only", $"{await h.Repo.HeadAsync(Ct)}", run.Branch!)).Text.Trim();
        Assert.Equal("tests/Lab.Tests/CalcTests.cs", changed);
        Assert.StartsWith("test-agent/src-lab-calc-cs-r_", run.Branch);
        // Nobody's checkout was touched.
        Assert.False(File.Exists(Path.Combine(h.Repo.Root, "tests/Lab.Tests/CalcTests.cs")));
        Assert.Equal("", (await h.Repo.GitAsync(Ct, "status", "--porcelain")).Text.Trim());
    }

    [Fact]
    public async Task A_failing_test_fails_verification_and_leaves_coverage_alone()
    {
        using var h = await HarnessAsync(runner: _ => FakeCoverageRunner.Result(FakeCoverageRunner.EmptyReport, failed: 1, targetPct: 90));

        var run = await VerifyAsync(h, await VerifyingRunAsync(h, NewTest(GoodTest)));

        Assert.Equal((TestGenRunState.VerificationFailed, "1 test(s) fail"), (run.State, run.Reason));
        Assert.Empty(await h.Get<CoverageStore>().CandidatesAsync([run.Id], Ct));
        Assert.Null(run.Branch);
    }

    private const string Ca2022 = "tests/Lab.Tests/CalcTests.cs(4,9): warning CA2022: Avoid inexact read with 'System.IO.Stream.Read(byte[], int, int)'";

    [Fact]
    public async Task A_candidate_that_would_fail_lint_fails_verification_and_records_nothing()
    {
        using var h = await HarnessAsync(runner: _ => FakeCoverageRunner.Result(FakeCoverageRunner.Report("/work/job", (Target, 9, 10)),
            targetPct: 90, build: BuildOutcome.Failed) with { Diagnostics = [Ca2022] });

        var run = await VerifyAsync(h, await VerifyingRunAsync(h, NewTest(GoodTest)));

        Assert.Equal((TestGenRunState.VerificationFailed, RunVerifier.NotLintClean), (run.State, run.Reason));
        Assert.Empty(await h.Get<CoverageStore>().CandidatesAsync([run.Id], Ct));
        Assert.Null(run.Branch);
    }

    [Fact]
    public async Task A_compile_error_beside_a_warning_is_a_build_failure()
    {
        using var h = await HarnessAsync(runner: _ => FakeCoverageRunner.Result(FakeCoverageRunner.EmptyReport, build: BuildOutcome.Failed) with
        {
            Diagnostics = ["tests/Lab.Tests/CalcTests.cs(4,51): error CS1002: ; expected", Ca2022],
        });

        var run = await VerifyAsync(h, await VerifyingRunAsync(h, NewTest(GoodTest)));

        Assert.Equal((TestGenRunState.VerificationFailed, "the tests do not build"), (run.State, run.Reason));
    }

    [Fact]
    public async Task A_diff_that_touches_production_code_fails_without_running_anything()
    {
        using var h = await HarnessAsync();
        const string diff = """
            diff --git a/src/Lab/Calc.cs b/src/Lab/Calc.cs
            --- a/src/Lab/Calc.cs
            +++ b/src/Lab/Calc.cs
            @@ -1,2 +1,2 @@
             namespace Lab;
            -public static class Calc { public static int Add(int a, int b) => a + b; }
            +public static class Calc { public static int Add(int a, int b) => 2; }

            """;

        var run = await VerifyAsync(h, await VerifyingRunAsync(h, diff));

        Assert.Equal(TestGenRunState.VerificationFailed, run.State);
        Assert.Contains("outside the test locations", run.Reason);
        Assert.Empty(h.Runner.Requests);
    }

    [Fact]
    public async Task A_skip_that_is_not_a_reported_bug_fails_without_running_anything()
    {
        using var h = await HarnessAsync();

        var run = await VerifyAsync(h, await VerifyingRunAsync(h, NewTest("""[Fact(Skip = "later")] public void Adds() { Assert.Equal(2, Calc.Add(1, 1)); }""")));

        Assert.Equal(TestGenRunState.VerificationFailed, run.State);
        Assert.Contains(TestGuardrails.Skipped, run.Reason);
        Assert.Empty(h.Runner.Requests);
    }

    [Fact]
    public async Task An_empty_diff_completes_with_no_change()
    {
        using var h = await HarnessAsync();

        var run = await VerifyAsync(h, await VerifyingRunAsync(h, ""));

        Assert.Equal(TestGenRunState.CompletedNoChange, run.State);
    }

    // ---- suspected bugs ----

    private const string BugTest =
        """[Fact(Skip = "suspected-bug: Add ignores overflow")] public void AddChecksOverflow() { Assert.Throws<OverflowException>(() => Calc.Add(int.MaxValue, 1)); }""";

    private static readonly SuspectedBug Bug = new("tests/Lab.Tests/CalcTests.cs", "AddChecksOverflow", "Add ignores overflow",
        "Add is meant to be checked", "OverflowException", "-2147483648", "Assert.Throws() Failure: No exception was thrown");

    /// <summary>The runner: an un-skipped bug test fails; the real run passes.</summary>
    private static RunnerResult BugRunner(RunnerRequest r) =>
        r.Diff!.Contains("Skip = ", StringComparison.Ordinal)
            ? FakeCoverageRunner.Result(FakeCoverageRunner.Report("/work/job", (Target, 9, 10)), targetPct: 90)
            : FakeCoverageRunner.Result(FakeCoverageRunner.EmptyReport, failed: 1) with
            {
                Failures = [new TestFailure("Lab.Tests.CalcTests.AddChecksOverflow", "Assert.Throws() Failure: No exception was thrown")],
            };

    [Fact]
    public async Task A_confirmed_bug_opens_one_issue_and_the_skip_links_to_it()
    {
        using var h = await HarnessAsync(runner: BugRunner);

        var run = await VerifyAsync(h, await VerifyingRunAsync(h, NewTest(BugTest), bugs: [Bug]));

        Assert.Equal(TestGenRunState.Candidate, run.State);
        var (number, issue) = Assert.Single(h.GitHub.Issues);
        Assert.Equal($"Suspected bug: Add ignores overflow ({Target})", issue.Title);
        Assert.Contains("OverflowException", issue.Body);
        Assert.Contains(run.Id, issue.Body);
        Assert.Contains(run.CommitSha[..12], issue.Body);
        Assert.All(h.GitHub.Authorizations, a => Assert.Equal("Bearer gh-token-for-tests", a));

        var test = (await h.Repo.GitAsync(Ct, "show", $"{run.Branch}:tests/Lab.Tests/CalcTests.cs")).Text;
        Assert.Contains($"suspected-bug https://github.com/owner/repo/issues/{number}: Add ignores overflow", test);
        // The panel's view of the run carries the issue.
        var shown = await Admin(h).GetFromJsonAsync<RunDetail>($"/api/coverage/runs/{run.Id}", new JsonSerializerOptions(JsonSerializerDefaults.Web), Ct);
        Assert.Equal($"https://github.com/owner/repo/issues/{number}", Assert.Single(shown!.Issues).Url);
                // The un-skipped proof ran once, then the real verification.
        Assert.Equal(2, h.Runner.Requests.Count);
        Assert.DoesNotContain("Skip = ", h.Runner.Requests.First().Diff);
        // The proof needs only the related tests, with the run's file as the target; verification runs everything.
        Assert.Equal([(TestScope.Related, Target), (TestScope.All, Target)], h.Runner.Requests.Select(r => (r.Tests, r.TargetFile)));
    }

    [Fact]
    public async Task A_bug_proof_with_only_a_lint_finding_still_reproduces_the_bug()
    {
        // Un-skipping can leave the copy unformatted or warning; that copy is never merged, so its tests decide.
        using var h = await HarnessAsync(runner: r => r.Diff!.Contains("Skip = ", StringComparison.Ordinal)
            ? BugRunner(r)
            : BugRunner(r) with { Build = BuildOutcome.Failed, Diagnostics = [Ca2022] });

        var run = await VerifyAsync(h, await VerifyingRunAsync(h, NewTest(BugTest), bugs: [Bug]));

        Assert.Equal(TestGenRunState.Candidate, run.State);
        Assert.Single(h.GitHub.Issues);
    }

    [Fact]
    public async Task A_run_that_allows_no_bugs_fails_verification_on_one_and_opens_nothing()
    {
        using var h = await HarnessAsync(runner: BugRunner);

        var run = await VerifyAsync(h, await VerifyingRunAsync(h, NewTest(BugTest), bugs: [Bug], maxBugs: 0));

        // With no bug allowed, its skip is one nobody may make: the guardrails stop it before any proof runs.
        Assert.Equal(TestGenRunState.VerificationFailed, run.State);
        Assert.StartsWith("guardrail:", run.Reason);
        Assert.Empty(h.Runner.Requests);
        Assert.Empty(h.GitHub.Issues);
    }

    [Fact]
    public async Task A_bug_that_is_not_reproduced_fails_verification_and_opens_nothing()
    {
        using var h = await HarnessAsync(runner: _ => FakeCoverageRunner.Result(FakeCoverageRunner.Report("/work/job", (Target, 9, 10)), targetPct: 90));

        var run = await VerifyAsync(h, await VerifyingRunAsync(h, NewTest(BugTest), bugs: [Bug]));

        Assert.Equal((TestGenRunState.VerificationFailed, RunVerifier.NotReproduced), (run.State, run.Reason));
        Assert.Empty(h.GitHub.Issues);
    }

    [Fact]
    public async Task Verifying_again_after_a_restart_opens_no_second_issue()
    {
        using var h = await HarnessAsync(runner: BugRunner);
        var id = await VerifyingRunAsync(h, NewTest(BugTest), bugs: [Bug]);
        await VerifyAsync(h, id);
        // As if the replica died after opening the issue, before the run moved on.
        await using (var db = await h.Get<IDbContextFactory<MafDbContext>>().CreateDbContextAsync(Ct))
        {
            await db.TestGenRuns.Where(r => r.Id == id).ExecuteUpdateAsync(s => s.SetProperty(r => r.State, TestGenRunState.Verifying), Ct);
            await db.TestGenIssues.Where(i => i.RunId == id).ExecuteUpdateAsync(s => s.SetProperty(i => i.State, "creating"), Ct);
        }

        var run = await VerifyAsync(h, id);

        Assert.Equal(TestGenRunState.Candidate, run.State);
        Assert.Single(h.GitHub.Issues);
        Assert.Single(await h.Get<CoverageStore>().HistoryAsync(Target, Ct), x => x.Kind == SnapshotKind.Candidate);
    }

    [Fact]
    public async Task Without_a_token_the_bug_is_skipped_and_says_no_issue_was_opened()
    {
        using var h = await HarnessAsync(withToken: false, runner: BugRunner);

        var run = await VerifyAsync(h, await VerifyingRunAsync(h, NewTest(BugTest), bugs: [Bug]));

        Assert.Equal(TestGenRunState.Candidate, run.State);
        Assert.Empty(h.GitHub.Issues);
        Assert.Empty(h.GitHub.Authorizations);
        var test = (await h.Repo.GitAsync(Ct, "show", $"{run.Branch}:tests/Lab.Tests/CalcTests.cs")).Text;
        Assert.Contains("suspected-bug (no issue: GitHub not configured): Add ignores overflow", test);
    }

    // ---- accept and discard ----

    private static async Task<TestGenRunRow> CandidateAsync(Harness h, string body = GoodTest, IReadOnlyList<SuspectedBug>? bugs = null) =>
        await VerifyAsync(h, await VerifyingRunAsync(h, NewTest(body), bugs: bugs));

    private static HttpClient Admin(Harness h) => h.Api.ClientFor("alice", "firm-a", Role.FIRM_ADMIN);

    [Fact]
    public async Task Accept_merges_into_main_and_makes_the_candidate_official()
    {
        using var h = await HarnessAsync();
        var run = await CandidateAsync(h);

        var response = await Admin(h).PostAsync($"/api/coverage/runs/{run.Id}/accept", null, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var accepted = (await h.Get<TestGenRuns>().GetAsync(run.Id, Ct))!;
        Assert.Equal(TestGenRunState.Accepted, accepted.State);
        Assert.Equal(await h.Repo.HeadAsync(Ct, "main"), accepted.MergeCommit);
        Assert.True(File.Exists(Path.Combine(h.Repo.Root, "tests/Lab.Tests/CalcTests.cs")));
        var current = (await h.Get<CoverageStore>().CurrentAsync(Target, Ct))!;
        Assert.Equal((90.0, accepted.MergeCommit), (current.Totals.LinePct, current.Totals.CommitSha));
    }

    [Fact]
    public async Task Accept_refuses_a_dirty_checkout_of_main_and_writes_nothing()
    {
        using var h = await HarnessAsync();
        var run = await CandidateAsync(h);
        var main = await h.Repo.HeadAsync(Ct, "main");
        await File.AppendAllTextAsync(Path.Combine(h.Repo.Root, Target), "// someone is editing\n", Ct);

        var response = await Admin(h).PostAsync($"/api/coverage/runs/{run.Id}/accept", null, Ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("main_dirty", await response.Content.ReadAsStringAsync(Ct));
        Assert.Equal(main, await h.Repo.HeadAsync(Ct, "main"));
        Assert.Equal(TestGenRunState.Candidate, (await h.Get<TestGenRuns>().GetAsync(run.Id, Ct))!.State);
    }

    [Fact]
    public async Task Accept_refuses_a_conflict_and_leaves_main_as_it_was()
    {
        using var h = await HarnessAsync();
        var run = await CandidateAsync(h);
        await h.Repo.CommitAsync(new Dictionary<string, string> { ["tests/Lab.Tests/CalcTests.cs"] = "// written by someone else\n" }, "clash", Ct);
        var main = await h.Repo.HeadAsync(Ct, "main");

        var response = await Admin(h).PostAsync($"/api/coverage/runs/{run.Id}/accept", null, Ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("merge_conflict", await response.Content.ReadAsStringAsync(Ct));
        Assert.Equal(main, await h.Repo.HeadAsync(Ct, "main"));
        Assert.Equal("", (await h.Repo.GitAsync(Ct, "status", "--porcelain")).Text.Trim());
        Assert.Equal(TestGenRunState.Candidate, (await h.Get<TestGenRuns>().GetAsync(run.Id, Ct))!.State);
    }

    [Fact]
    public async Task Accept_without_a_checkout_of_main_swaps_it_and_retries_when_it_moved()
    {
        using var h = await HarnessAsync();
        var run = await CandidateAsync(h);
        await h.Repo.GitAsync(Ct, "checkout", "-q", "-b", "elsewhere");
        var moved = false;
        h.Get<RepoWriter>().BeforeSwap = async () =>
        {
            if (!moved)
            {
                moved = true;
                // Someone else lands on main between the merge being built and main being moved.
                await h.Repo.GitAsync(Ct, "commit", "-q", "--allow-empty", "-m", "meanwhile");
                var meanwhile = await h.Repo.HeadAsync(Ct);
                await h.Repo.GitAsync(Ct, "update-ref", "refs/heads/main", meanwhile);
            }
        };

        var response = await Admin(h).PostAsync($"/api/coverage/runs/{run.Id}/accept", null, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var main = await h.Repo.HeadAsync(Ct, "main");
        Assert.Equal(main, (await h.Get<TestGenRuns>().GetAsync(run.Id, Ct))!.MergeCommit);
        // The merge sits on top of what landed meanwhile, and has the tests.
        Assert.Contains("meanwhile", (await h.Repo.GitAsync(Ct, "log", "--format=%s", "main")).Text);
        Assert.Contains("CalcTests", (await h.Repo.GitAsync(Ct, "ls-tree", "-r", "--name-only", "main")).Text);
    }

    [Fact]
    public async Task Discard_deletes_the_branch_and_closes_the_runs_issues()
    {
        using var h = await HarnessAsync(runner: BugRunner);
        var run = await CandidateAsync(h, BugTest, [Bug]);

        var response = await Admin(h).PostAsync($"/api/coverage/runs/{run.Id}/discard", null, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(TestGenRunState.Discarded, (await h.Get<TestGenRuns>().GetAsync(run.Id, Ct))!.State);
        Assert.DoesNotContain(run.Branch!, (await h.Repo.GitAsync(Ct, "branch", "--list", run.Branch!)).Text);
        var issue = Assert.Single(h.GitHub.Issues).Value;
        Assert.Equal("closed", issue.State);
        Assert.Contains("discarded", Assert.Single(h.GitHub.Comments).Body);
        Assert.Equal(50.0, (await h.Get<CoverageStore>().CurrentAsync(Target, Ct))!.Totals.LinePct);
    }

    [Fact]
    public async Task Accept_comments_on_the_issue_and_leaves_it_open()
    {
        using var h = await HarnessAsync(runner: BugRunner);
        var run = await CandidateAsync(h, BugTest, [Bug]);

        await Admin(h).PostAsync($"/api/coverage/runs/{run.Id}/accept", null, Ct);

        Assert.Equal("open", Assert.Single(h.GitHub.Issues).Value.State);
        Assert.Contains((await h.Repo.HeadAsync(Ct, "main"))[..12], Assert.Single(h.GitHub.Comments).Body);
    }

    [Fact]
    public async Task GitHub_being_down_does_not_block_a_decision()
    {
        using var h = await HarnessAsync(runner: BugRunner);
        var run = await CandidateAsync(h, BugTest, [Bug]);
        h.GitHub.Down = true;

        var response = await Admin(h).PostAsync($"/api/coverage/runs/{run.Id}/discard", null, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("could not be updated on GitHub", await response.Content.ReadAsStringAsync(Ct));
        Assert.Equal(TestGenRunState.Discarded, (await h.Get<TestGenRuns>().GetAsync(run.Id, Ct))!.State);
    }

    [Fact]
    public void A_branch_name_is_safe_and_short()
    {
        var name = RepoWriter.BranchFor("web/src/coverage/Very.Long.Component.Name.With.Many.Parts.For.No.Good.Reason.tsx", "r_1");

        Assert.Matches("^test-agent/[a-z0-9-]+-r_1$", name);
        Assert.True(name.Length <= "test-agent/".Length + 60 + "-r_1".Length);
    }
}
