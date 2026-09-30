using Maf.Lab.A2A;
using Maf.Lab.TestSupport;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Maf.Lab.Tests;

/// <summary>
/// The hosts tests start in-process read their own configuration, whatever order the build copied the referenced
/// projects' appsettings.json into this output (a solution build left the test agent's there, and the runner then
/// refused its own tokens).
/// </summary>
public sealed class TestHostContentRootTests
{
    [Fact]
    public void A_project_directory_is_found_under_src()
    {
        var dir = ProjectDir.Of("Maf.Lab.CoverageRunner");

        Assert.True(File.Exists(Path.Combine(dir, "appsettings.json")));
        Assert.Throws<DirectoryNotFoundException>(() => ProjectDir.Of("Maf.Lab.NoSuchProject"));
    }

    public static TheoryData<string, string> Hosts => new()
    {
        { "Maf.Lab.CoverageRunner", "maf-lab-coverage-runner" },
        { "Maf.Lab.TestAgent", "maf-lab-test-agent" },
        { "Maf.Lab.ComplianceAgent", "maf-lab-compliance" },
    };

    [Theory]
    [MemberData(nameof(Hosts))]
    public async Task Each_in_process_host_reads_its_own_audience(string project, string audience)
    {
        await using var app = Build(project);

        Assert.Equal(audience, app.Services.GetRequiredService<IOptions<A2AOptions>>().Value.Audience);
    }

    private static WebApplication Build(string project) => project switch
    {
        "Maf.Lab.CoverageRunner" => Maf.Lab.CoverageRunner.Program.BuildApp(ProjectDir.ContentRootArgs(project)),
        "Maf.Lab.TestAgent" => Maf.Lab.TestAgent.Program.BuildApp(ProjectDir.ContentRootArgs(project), InMemoryStores),
        "Maf.Lab.ComplianceAgent" => Maf.Lab.ComplianceAgent.Program.BuildApp(ProjectDir.ContentRootArgs(project), InMemoryStores),
        _ => throw new ArgumentOutOfRangeException(nameof(project)),
    };

    // The agents keep their A2A tasks in Redis; building them needs only a store, not a server.
    private static void InMemoryStores(WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton<global::A2A.ITaskStore>(new global::A2A.InMemoryTaskStore());
        builder.Services.AddSingleton<IPushConfigStore>(new FakePushConfigStore());
    }
}
