using Maf.Lab.A2A;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;

namespace Maf.Lab.TestSupport;

/// <summary>In-process protocol hosts use memory stores and fixture providers, without Redis or model calls.</summary>
public static class ProtocolHostFixtures
{
    /// <summary>Protocol/authentication fixtures must never create collections or depend on external stores.</summary>
    public static IWebHostBuilder WithoutCollectionBootstrap(this IWebHostBuilder builder) =>
        builder.ConfigureTestServices(services =>
        {
            foreach (var descriptor in services.Where(service => service.ServiceType == typeof(IHostedService)
                && service.ImplementationType?.Name == "BootstrapService").ToArray()) services.Remove(descriptor);
        });

    public static void InMemoryStores(WebApplicationBuilder builder)
    {
        builder.Configuration.AddInMemoryCollection(TestProviders.FixtureEngineSettings);
        builder.Services.AddSingleton<global::A2A.ITaskStore>(new global::A2A.InMemoryTaskStore());
        builder.Services.AddSingleton<IPushConfigStore>(new FakePushConfigStore());
        builder.Services.AddSingleton<Maf.Lab.Domain.SharedState.IBreakGlassPermissionStore>(new FakeBreakGlassPermissionStore());
    }
}
