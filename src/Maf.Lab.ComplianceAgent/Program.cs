using A2A;
using Maf.Lab.A2A;
using Maf.Lab.ComplianceAgent;
using Maf.Lab.Hosting;
using Microsoft.Extensions.Options;

namespace Maf.Lab.ComplianceAgent;

/// <summary>
/// The compliance reviewer: a second agent, in its own process, with its own identity and its own card. It shares
/// the protocol code with the billing assistant and nothing else — no database, no retrieval, no user.
/// </summary>
public partial class Program
{
    public static void Main(string[] args) => BuildApp(args).Run();

    public static WebApplication BuildApp(string[] args, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(args);
        configure?.Invoke(builder);

        // What this service emits about itself. Nothing is exported unless an OTLP endpoint is configured.
        builder.AddLabTelemetry("maf-lab-compliance");

        builder.Services.Configure<Maf.Lab.Domain.Configuration.AuthOptions>(
            builder.Configuration.GetSection(Maf.Lab.Domain.Configuration.AuthOptions.Section));
        builder.Services.Configure<ReviewOptions>(builder.Configuration.GetSection(ReviewOptions.Section));
        builder.Services.AddA2APartnerAuthentication(builder.Configuration);
        builder.Services.AddAuthorizationBuilder();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddSingleton(TimeProvider.System);

        builder.Services.AddSingleton(ComplianceAgentCard.Descriptor);
        builder.Services.AddSingleton<ITaskStore, InMemoryTaskStore>();
        builder.Services.AddSingleton<IPushConfigStore, InMemoryPushConfigStore>();
        builder.Services.AddSingleton<IAgentHandler, ReviewAgentHandler>();
        builder.Services.AddSingleton<ChannelEventNotifier>();
        builder.Services.AddSingleton<A2AServer>();
        builder.Services.AddSingleton<IA2ARequestHandler, A2ARequestHandlerWithExtras>();

        var app = builder.Build();
        if (app.Services.GetRequiredService<IOptions<A2AOptions>>().Value.PathBase is { Length: > 0 } prefix)
        {
            // Two agents share one entry point, so this one answers under its own prefix and says so in its card.
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
