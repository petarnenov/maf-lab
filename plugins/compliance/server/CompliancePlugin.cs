using Maf.Lab.Plugins.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Maf.Lab.Plugins.Compliance;

/// <summary>
/// The compliance plugin's in-process part (extract-compliance-plugin): the client that consults the reviewer agent —
/// the core's reviewer-consultation port, so a write flow that needs a review gets one while this plugin is in use — and
/// the audit screen's routes over the core's audit trail. The reviewer itself is this plugin's service, in its own
/// container.
/// </summary>
public sealed class CompliancePlugin : IMafPlugin, IContributesServices, IContributesEndpoints
{
    public const string PluginName = "compliance";

    public string Name => PluginName;

    /// <summary>Once for every host that composes the installed plugins: the api, and the eval's agent host.</summary>
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ComplianceOptions>(configuration.GetSection(ComplianceOptions.Section));
        services.AddHttpClient(ComplianceConsultant.HttpClientName);
        // Scoped: its record is a step of the request's write, filed under the request's principal.
        services.AddScoped<ComplianceConsultant>();
        services.AddScoped<IReviewerConsultation>(sp => sp.GetRequiredService<ComplianceConsultant>());
    }

    public void MapEndpoints(IMafEndpoints endpoints) => AuditScreenEndpoints.Map(endpoints.Routes);
}
