using A2A;
using Maf.Lab.A2A;
using Maf.Lab.Hosting;
using Maf.Lab.Retrieval.Configuration;
using Maf.Lab.Retrieval.Models;
using Maf.Lab.TestGen;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Maf.Lab.TestAgent;

/// <summary>
/// The test-generation agent: reachable only over A2A, on the internal network. It holds the model key and reads the
/// repository; the code it writes is built and run by the coverage runner, never here.
/// </summary>
public partial class Program
{
    public static void Main(string[] args) => BuildApp(args).Run();

    public static WebApplication BuildApp(string[] args, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(args);
        configure?.Invoke(builder);

        builder.AddLabTelemetry("maf-lab-test-agent");
        // A run outlives the connection that started it, and the api may follow it from either replica: the task
        // lives in the shared store, not in this process.
        builder.AddSharedState();

        builder.Services.Configure<Maf.Lab.Domain.Configuration.AuthOptions>(
            builder.Configuration.GetSection(Maf.Lab.Domain.Configuration.AuthOptions.Section));
        builder.Services.Configure<TestAgentOptions>(builder.Configuration.GetSection(TestAgentOptions.Section));
        builder.Services.Configure<ModelOptions>(builder.Configuration.GetSection(ModelOptions.Section));
        builder.Services.AddA2APartnerAuthentication(builder.Configuration);
        builder.Services.RequireA2AStoreKeyspace();
        builder.Services.AddAuthorizationBuilder();
        builder.Services.AddHttpContextAccessor();
        builder.Services.TryAddSingleton(TimeProvider.System);

        // The installed chat provider, with the model from the task and the run's cancellation token.
        Maf.Lab.Plugins.Abstractions.ProviderHost.AddInstalledProviders(builder.Services, builder.Configuration);
        builder.Services.TryAddSingleton<IChatClientFactory, ModelProviders>();

        builder.Services.AddHttpClient("coverage-runner", (sp, http) =>
        {
            http.BaseAddress = new Uri(sp.GetRequiredService<IOptions<TestAgentOptions>>().Value.RunnerBaseUrl.TrimEnd('/') + "/");
            http.Timeout = TimeSpan.FromSeconds(30);
        });
        builder.Services.TryAddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<TestAgentOptions>>().Value;
            var auth = sp.GetRequiredService<IOptions<Maf.Lab.Domain.Configuration.AuthOptions>>().Value;
            var audience = new A2AOptions { Audience = CoverageRunnerClient.Audience, TokenLifetime = TimeSpan.FromMinutes(10) };
            return new CoverageRunnerClient(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient("coverage-runner"),
                _ => Task.FromResult(PartnerJwt.Issue(auth, audience, options.RunnerPartnerId, [CoverageRunnerClient.ScopeRun]).Token),
                options.RunnerPollEvery);
        });

        builder.Services.AddSingleton(TestAgentCard.Descriptor);
        builder.Services.AddSingleton<Func<AgentCardDescriptor>>(_ => () => TestAgentCard.Descriptor);
        builder.Services.TryAddSingleton<ITaskStore, RedisTaskStore>();
        builder.Services.TryAddSingleton<IPushConfigStore, RedisPushConfigStore>();
        builder.RequireSharedState<ITaskStore>();
        // A task's checkpoint and lease sit beside it in the shared store; a host without one keeps them in memory.
        builder.Services.TryAddSingleton<ITaskCheckpointStore>(sp =>
            sp.GetService<StackExchange.Redis.IConnectionMultiplexer>() is { } redis
                ? new RedisTaskCheckpointStore(redis, sp.GetRequiredService<IOptions<A2AOptions>>())
                : new InMemoryTaskCheckpointStore(sp.GetRequiredService<TimeProvider>()));
        builder.Services.AddSingleton<TestGenerationHandler>();
        builder.Services.AddSingleton<IAgentHandler>(sp => sp.GetRequiredService<TestGenerationHandler>());
        builder.Services.AddHostedService<TaskRecovery>();
        builder.Services.AddSingleton<ChannelEventNotifier>();
        builder.Services.AddSingleton<A2AServer>();
        builder.Services.AddSingleton<IA2ARequestHandler, A2ARequestHandlerWithExtras>();

        var app = builder.Build();
        if (app.Services.GetRequiredService<IOptions<A2AOptions>>().Value.PathBase is { Length: > 0 } prefix)
        {
            app.UsePathBase(prefix);
        }
        app.UseInstanceHeader();
        app.UseA2ASpecWire();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapInstanceHealth();
        app.MapA2ASurface();
        app.MapA2AProtocol();
        return app;
    }
}
