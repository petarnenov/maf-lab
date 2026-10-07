using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Maf.Lab.Api.Coverage;
using Maf.Lab.TestGen;
using Microsoft.Extensions.DependencyInjection;

namespace Maf.Lab.Tests;

/// <summary>A throwaway git repository with a main branch, for anything that reads the repo through git.</summary>
internal sealed class TempGitRepo
{
    public static readonly IReadOnlyDictionary<string, string> Identity = new Dictionary<string, string>
    {
        ["GIT_AUTHOR_NAME"] = "test", ["GIT_AUTHOR_EMAIL"] = "test@example.invalid",
        ["GIT_COMMITTER_NAME"] = "test", ["GIT_COMMITTER_EMAIL"] = "test@example.invalid",
    };

    private TempGitRepo(string root) => Root = root;

    public string Root { get; }

    public static async Task<TempGitRepo> CreateAsync(IReadOnlyDictionary<string, string> files, CancellationToken ct)
    {
        var repo = new TempGitRepo(Directory.CreateTempSubdirectory("maf-git-").FullName);
        await repo.GitAsync(ct, "init", "-q", "-b", "main");
        await repo.CommitAsync(files, "initial", ct);
        return repo;
    }

    /// <summary>Writes the files, commits them, and returns the new commit.</summary>
    public async Task<string> CommitAsync(IReadOnlyDictionary<string, string> files, string message, CancellationToken ct)
    {
        foreach (var (path, content) in files)
        {
            var full = Path.Combine(Root, path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            await File.WriteAllTextAsync(full, content, ct);
        }
        await GitAsync(ct, "add", "-A");
        await GitAsync(ct, "commit", "-q", "-m", message);
        return await HeadAsync(ct);
    }

    public async Task<string> HeadAsync(CancellationToken ct, string rev = "HEAD") =>
        (await GitAsync(ct, "rev-parse", rev)).Text.Trim();

    public Task<GitResult> GitAsync(CancellationToken ct, params string[] args) => Git.CheckedAsync(Root, args, ct, environment: Identity);
}

/// <summary>
/// The coverage runner as an HTTP handler: each request is answered at once with a finished job whose result the
/// test scripts per toolchain. It records every request it was sent.
/// </summary>
internal sealed class FakeCoverageRunner : HttpMessageHandler
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly ConcurrentDictionary<string, RunnerJob> _jobs = new();

    public ConcurrentQueue<RunnerRequest> Requests { get; } = new();

    /// <summary>What a request is answered with. Default: an ok run with an empty report.</summary>
    public Func<RunnerRequest, RunnerResult> Answer { get; set; } = _ => Result(EmptyReport);

    /// <summary>Held before answering a submission, so a test can catch a job in flight.</summary>
    public TaskCompletionSource? Gate { get; set; }

    /// <summary>Set to make every request fail as if the runner were down.</summary>
    public bool Down { get; set; }

    /// <summary>Jobs stay running until cancelled, so a test can stop the work waiting on one.</summary>
    public bool KeepRunning { get; set; }

    /// <summary>The jobs a caller cancelled, in order.</summary>
    public ConcurrentQueue<string> Cancels { get; } = new();

    public const string EmptyReport = "<coverage><packages/></coverage>";

    public static RunnerResult Result(string cobertura, int failed = 0, string root = "/work/job", double? targetPct = null,
        string status = RunnerStatus.Ok, string build = BuildOutcome.Ok) =>
        new(status, build, [], new TestCounts(10, failed, 0), failed > 0 ? [new TestFailure("T.Fails", "Assert.Equal() Failure")] : [],
            cobertura, root, targetPct, [], 1);

    /// <summary>A Cobertura report for the given files, named as a container would name them.</summary>
    public static string Report(string root, params (string Path, int Covered, int Total)[] files)
    {
        var sb = new StringBuilder("<coverage><packages><package name=\"p\"><classes>");
        foreach (var (path, covered, total) in files)
        {
            sb.Append($"<class name=\"c\" filename=\"{root}/{path}\"><lines>");
            for (var i = 1; i <= total; i++)
            {
                sb.Append($"<line number=\"{i}\" hits=\"{(i <= covered ? 1 : 0)}\" branch=\"False\"/>");
            }
            sb.Append("</lines></class>");
        }
        return sb.Append("</classes></package></packages></coverage>").ToString();
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (Down)
        {
            throw new HttpRequestException("connection refused");
        }
        if (request.Headers.Authorization?.Scheme != "Bearer")
        {
            return new HttpResponseMessage(HttpStatusCode.Unauthorized);
        }
        var path = request.RequestUri!.AbsolutePath;
        if (request.Method == HttpMethod.Post && path.EndsWith("/runs"))
        {
            var body = (await request.Content!.ReadFromJsonAsync<RunnerRequest>(Json, ct))!;
            Requests.Enqueue(body);
            if (Gate is { } gate)
            {
                await gate.Task.WaitAsync(ct);
            }
            var job = KeepRunning
                ? new RunnerJob($"job_{Guid.NewGuid():N}", RunnerJobState.Running, 0, null)
                : new RunnerJob($"job_{Guid.NewGuid():N}", RunnerJobState.Done, 0, Answer(body));
            _jobs[job.Id] = job;
            return new HttpResponseMessage(HttpStatusCode.Accepted) { Content = JsonContent.Create(job, options: Json) };
        }
        if (request.Method == HttpMethod.Post && path.EndsWith("/cancel"))
        {
            var canceled = path.Split('/')[^2];
            Cancels.Enqueue(canceled);
            if (_jobs.TryGetValue(canceled, out var running))
            {
                _jobs[canceled] = running with { State = RunnerJobState.Canceled, Result = null };
            }
            return new HttpResponseMessage(HttpStatusCode.Accepted);
        }
        var id = path.Split('/').Last();
        return _jobs.TryGetValue(id, out var found)
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(found, options: Json) }
            : new HttpResponseMessage(HttpStatusCode.NotFound);
    }
}

internal static class CoverageApi
{
    /// <summary>The api over a temp repository and a fake runner.</summary>
    /// <param name="dataDir">Another api's data directory: a second replica over the same database.</param>
    public static ApiFactory Create(TempGitRepo repo, FakeCoverageRunner? runner = null, IReadOnlyDictionary<string, string?>? extra = null,
        string? dataDir = null)
    {
        runner ??= new FakeCoverageRunner();
        var settings = new Dictionary<string, string?>
        {
            ["Coverage:RepoRoot"] = repo.Root,
            ["CoverageRunner:BaseUrl"] = "http://runner.test",
            ["CoverageRunner:PollEvery"] = "00:00:00.010",
        };
        foreach (var (k, v) in extra ?? new Dictionary<string, string?>())
        {
            settings[k] = v;
        }
        return new ApiFactory(ApiFactory.ProceduralModel(), dataDir: dataDir)
        {
            ExtraSettings = settings,
            ConfigureTestServices = s => s.AddHttpClient(CoverageRunnerRegistration.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => runner),
        };
    }

    /// <summary>Ingests a report straight into the store, as a refresh would.</summary>
    public static async Task IngestAsync(ApiFactory api, string commit, string toolchain, string kind, string? runId,
        params (string Path, int Covered, int Total)[] files)
    {
        var ingestor = api.Services.GetRequiredService<CoverageIngestor>();
        await ingestor.IngestAsync(FakeCoverageRunner.Report("/work/job", files), commit, false, toolchain, kind, runId, "/work/job",
            TestContext.Current.CancellationToken);
    }

    /// <summary>A C# file with the given number of lines.</summary>
    public static string Lines(int count) => string.Join('\n', Enumerable.Range(1, count).Select(i => $"// line {i}")) + "\n";
}
