using System.Net;
using Maf.Lab.Domain.SharedState;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Hosting;
using Maf.Lab.TestSupport;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Maf.Lab.Tests;

/// <summary>
/// Stateless does not mean without state: it means the state is somewhere every replica sees. A replica that
/// cannot see it says so rather than serving half of what it is asked.
/// </summary>
public class SharedStateTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_service_that_needs_the_store_and_has_none_does_not_start()
    {
        // No connection string and nothing registered: the store this service declared it needs is not there.
        await using var server = new WebApplicationFactory<Maf.Lab.Retrieval.Program>()
            .WithWebHostBuilder(b => b.ConfigureTestServices(s => s.RemoveAll<IIdempotencyStore>()));

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => Task.Run(() => server.CreateClient(), Ct));

        // It names what it could not find and how to give it one, and never an address or a credential.
        Assert.Contains(nameof(IIdempotencyStore), failure.Message);
        Assert.Contains($"{SharedStateOptions.Section}:ConnectionString", failure.Message);
    }

    [Fact]
    public async Task A_service_with_a_store_serves_whichever_store_it_is()
    {
        await using var server = new WebApplicationFactory<Maf.Lab.Retrieval.Program>()
            .WithWebHostBuilder(b => b.WithFakeSharedState());

        var response = await server.CreateClient().GetAsync("/health", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_replica_that_cannot_reach_the_store_reports_itself_unhealthy()
    {
        // A configured store that is not there: the address parses, the connection is never made. Built against
        // a plain host, because what is under test is the wiring rather than an api that happens to have it.
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{SharedStateOptions.Section}:ConnectionString"] = "127.0.0.1:6399,connectTimeout=200,syncTimeout=200,abortConnect=false",
        });
        builder.AddSharedState();

        using var host = builder.Build();
        var health = host.Services.GetRequiredService<SharedStateHealth>();

        var (ok, reason) = await health.CheckAsync(Ct);
        Assert.False(ok);
        Assert.Contains("unreachable", reason);
        // The reason says what kind of failure it was, never where the store is or how to reach it.
        Assert.DoesNotContain("6399", reason);
    }

    [Fact]
    public async Task What_one_replica_writes_another_reads()
    {
        // The store is the thing every replica shares; two hosts over one store is what that means.
        var shared = new FakeRunStateStore();
        var now = DateTimeOffset.UtcNow;
        await shared.SaveAsync(new RunState("r1", "c1", "adam", "firm-a", "half an answer", [],
            RunOutcomes.Running, null, null, null, now, now), Ct);

        var read = await shared.GetAsync("r1", Ct);
        Assert.Equal("half an answer", read!.Answer);
        Assert.Equal(RunOutcomes.Running, read.Outcome);
    }
}
