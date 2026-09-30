using Maf.Lab.A2A;
using Maf.Lab.Hosting;
using Maf.Lab.TestGen;
using Maf.Lab.TestGen.Coverage;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Maf.Lab.CoverageRunner;

/// <summary>
/// The coverage runner: POST a job, poll it until it is done. Callers are the api and the test agent, each with a
/// service token for this runner's audience; nobody else reaches it, and it reaches nothing outside.
/// </summary>
public partial class Program
{
    public static void Main(string[] args) => BuildApp(args).Run();

    public static WebApplication BuildApp(string[] args, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(args);
        configure?.Invoke(builder);

        builder.AddLabTelemetry("maf-lab-coverage-runner");
        builder.Services.Configure<Maf.Lab.Domain.Configuration.AuthOptions>(
            builder.Configuration.GetSection(Maf.Lab.Domain.Configuration.AuthOptions.Section));
        builder.Services.Configure<RunnerOptions>(builder.Configuration.GetSection(RunnerOptions.Section));
        builder.Services.AddA2APartnerAuthentication(builder.Configuration);
        builder.Services.AddAuthorizationBuilder();
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.AddSingleton(sp => sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<RunnerOptions>>().Value);
        // A host that brought its own toolchains (a test over a fixture repository) keeps them.
        if (!builder.Services.Any(d => d.ServiceType == typeof(IToolchainRunner)))
        {
            builder.Services.AddSingleton<IToolchainRunner, DotnetToolchain>();
            builder.Services.AddSingleton<IToolchainRunner, VitestToolchain>();
        }
        builder.Services.AddSingleton<JobExecutor>();
        builder.Services.AddSingleton<JobQueue>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<JobQueue>());

        var app = builder.Build();
        app.UseInstanceHeader();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapInstanceHealth();

        var runs = app.MapGroup("/runs").RequireAuthorization(PartnerAuthentication.Policy);
        runs.MapPost("", (RunnerRequest request, JobQueue queue, RunnerOptions options) =>
        {
            if (Problem(request, options) is { } problem)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["request"] = [problem] });
            }
            var job = queue.Submit(request);
            return Results.Accepted($"/runs/{job.Id}", job);
        });
        runs.MapGet("/{id}", (string id, JobQueue queue) => queue.Get(id) is { } job ? Results.Ok(job) : Results.NotFound());
        return app;
    }

    private static string? Problem(RunnerRequest request, RunnerOptions options)
    {
        if (!Git.IsCommitId(request.Commit))
        {
            return "commit must be a commit id.";
        }
        if (request.Toolchain is not ("dotnet" or "vitest"))
        {
            return "toolchain must be dotnet or vitest.";
        }
        if (request.Diff is { } diff && System.Text.Encoding.UTF8.GetByteCount(diff) > options.MaxDiffBytes)
        {
            return "The diff is larger than the runner accepts.";
        }
        if (request.TargetFile is { } target && CoveragePaths.Clean(target) != target)
        {
            return "targetFile must be a path relative to the repository root.";
        }
        return null;
    }
}
