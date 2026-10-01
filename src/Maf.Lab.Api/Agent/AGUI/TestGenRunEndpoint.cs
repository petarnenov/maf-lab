using AGUI.Abstractions;
using Maf.Lab.Api.Coverage;

namespace Maf.Lab.Api.Agent.AGUI;

/// <summary>The test-generation run agent at its AG-UI endpoint, behind the official server (agui-protocol-only).</summary>
public static class TestGenRunEndpoint
{
    public const string Path = "/api/coverage/runs/agent";

    public static IEndpointRouteBuilder MapTestGenRunAgent(this IEndpointRouteBuilder app)
    {
        app.MapAgent(Path, app.ServiceProvider.GetRequiredService<TestGenRunAgent>(), AGUIMappings.Agents())
            .AddEndpointFilter(async (context, next) =>
            {
                // A thread that is not a known run's is not found, before any run starts.
                var input = context.Arguments.OfType<RunAgentInput>().FirstOrDefault();
                var runId = TestGenRunAgent.RunIdOf(input?.ThreadId);
                var runs = context.HttpContext.RequestServices.GetRequiredService<TestGenRuns>();
                return runId is null || await runs.GetAsync(runId, context.HttpContext.RequestAborted) is null
                    ? Maf.Lab.Api.Endpoints.CoverageEndpoints.NotFound()
                    : await next(context);
            });
        return app;
    }
}
