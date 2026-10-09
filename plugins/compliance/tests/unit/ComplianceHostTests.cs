extern alias service;

using Maf.Lab.A2A;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Maf.Lab.Tests;

/// <summary>The reviewer, started in process from this plugin's service, reads its own configuration (its audience).</summary>
public sealed class ComplianceHostTests
{
    [Fact]
    public async Task The_reviewer_reads_its_own_audience()
    {
        await using var app = service::Maf.Lab.ComplianceAgent.Program.BuildApp(
            ProjectDir.ContentRootArgsAt(Path.Combine("plugins", "compliance", "service")), Maf.Lab.TestSupport.ProtocolHostFixtures.InMemoryStores);

        Assert.Equal("maf-lab-compliance", app.Services.GetRequiredService<IOptions<A2AOptions>>().Value.Audience);
    }
}
