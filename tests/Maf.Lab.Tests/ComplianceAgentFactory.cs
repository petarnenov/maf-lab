using Maf.Lab.TestSupport;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Maf.Lab.Tests;

/// <summary>
/// The compliance reviewer on a real socket, configured to be instant and predictable: the slowness and the
/// occasional question are what the stack is for, not what a test should wait for. It listens for real because the
/// consultant reaches it with a real HttpClient — which is the half of the protocol worth testing.
/// </summary>
public sealed class ComplianceFactory : IAsyncDisposable
{
    private WebApplication? app;

    /// <summary>The running host, for a test that needs to look at what this replica can see.</summary>
    public WebApplication App => app ?? throw new InvalidOperationException("call ListenAsync first");

    /// <summary>0 never asks for a justification, 1 always does.</summary>
    public double AskRate { get; init; }

    public int ReviewMs { get; init; } = 4;

    /// <summary>Above this, the review refuses.</summary>
    public decimal RefuseAbove { get; init; } = 1_000m;

    /// <summary>The prefix it answers under, as it does in the stack where two agents share one entry point.</summary>
    public string PathBase { get; init; } = "";

    public CapturingLoggerProvider Logs { get; } = new();

    /// <summary>The address it is listening on, started on first use.</summary>
    /// <summary>The stores this reviewer runs against, shared by every host a test starts over them.</summary>
    public global::A2A.ITaskStore Tasks { get; init; } = new global::A2A.InMemoryTaskStore();
    public Maf.Lab.A2A.IPushConfigStore PushConfigs { get; init; } = new FakePushConfigStore();

    public async Task<string> ListenAsync()
    {
        if (app is not null)
        {
            return Address;
        }
        app = Maf.Lab.ComplianceAgent.Program.BuildApp([], builder =>
        {
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["A2A:Audience"] = "maf-lab-compliance",
                ["A2A:PublicBaseUrl"] = "",
                ["A2A:PathBase"] = PathBase,
                ["A2A:RequiredScope"] = Maf.Lab.ComplianceAgent.ComplianceAgentCard.ReviewScope,
                ["A2A:Partners:maf-lab-assistant:Secret"] = "assistant-secret",
                ["A2A:Partners:maf-lab-assistant:Scopes:0"] = Maf.Lab.ComplianceAgent.ComplianceAgentCard.ReviewScope,
                ["Review:MinDurationMs"] = ReviewMs.ToString(),
                ["Review:MaxDurationMs"] = ReviewMs.ToString(),
                ["Review:AskForJustificationRate"] =
                    AskRate.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
                ["Review:RefuseAboveAmount"] =
                    RefuseAbove.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
            });
            builder.Logging.ClearProviders();
            builder.Logging.AddProvider(Logs).SetMinimumLevel(LogLevel.Information);
            // A store, not a particular one: the reviewer requires that its tasks are not in its own memory,
            // and these tests are about the reviewer rather than about Redis.
            builder.Services.AddSingleton<global::A2A.ITaskStore>(Tasks);
            builder.Services.AddSingleton<Maf.Lab.A2A.IPushConfigStore>(PushConfigs);
        });
        await app.StartAsync();

        // The card must advertise where it actually is, now that the port is known.
        var options = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<Maf.Lab.A2A.A2AOptions>>();
        options.Value.PublicBaseUrl = Address.TrimEnd('/') + PathBase;
        return Address;
    }

    public string Address => app!.Services.GetRequiredService<IServer>().Features
        .Get<IServerAddressesFeature>()!.Addresses.First();

    public HttpClient CreateClient()
    {
        ListenAsync().GetAwaiter().GetResult();
        return new HttpClient { BaseAddress = new Uri(Address) };
    }

    public async ValueTask DisposeAsync()
    {
        if (app is not null)
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }
}
