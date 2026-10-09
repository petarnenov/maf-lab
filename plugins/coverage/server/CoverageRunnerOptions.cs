using Maf.Lab.A2A;
using Maf.Lab.Domain.Configuration;
using Maf.Lab.TestGen;
using Microsoft.Extensions.Options;

namespace Maf.Lab.Plugins.Coverage;

/// <summary>Where the coverage runner is, and who the api is to it.</summary>
public sealed class CoverageRunnerOptions
{
    public const string Section = "CoverageRunner";

    public string BaseUrl { get; set; } = "http://localhost:5095";

    /// <summary>The partner id the runner knows the api by.</summary>
    public string PartnerId { get; set; } = "maf-lab-assistant";

    public TimeSpan PollEvery { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Whether verification may be answered with a complete result the runner already computed for the identical
    /// request (the agent's whole-suite confirmation of the same diff). False asks the runner for a fresh run: a second
    /// sample of the whole suite, at the cost of running it again.
    /// </summary>
    public bool ReuseForVerification { get; set; } = true;
}

public static class CoverageRunnerRegistration
{
    public const string HttpClientName = "coverage-runner";

    /// <summary>
    /// The runner client, signing its own short-lived service token with the lab's key: the runner checks audience,
    /// partner and scope, and holds no other secret.
    /// </summary>
    public static IServiceCollection AddCoverageRunnerClient(this IServiceCollection services)
    {
        services.AddHttpClient(HttpClientName, (sp, http) =>
        {
            var baseUrl = sp.GetRequiredService<IOptions<CoverageRunnerOptions>>().Value.BaseUrl.TrimEnd('/');
            http.BaseAddress = new Uri(baseUrl + "/");
            http.Timeout = TimeSpan.FromSeconds(30);
        });
        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<CoverageRunnerOptions>>().Value;
            var auth = sp.GetRequiredService<IOptions<AuthOptions>>().Value;
            var audience = new A2AOptions { Audience = CoverageRunnerClient.Audience, TokenLifetime = TimeSpan.FromMinutes(10) };
            return new CoverageRunnerClient(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName),
                _ => Task.FromResult(PartnerJwt.Issue(auth, audience, options.PartnerId, [CoverageRunnerClient.ScopeRun]).Token),
                options.PollEvery);
        });
        return services;
    }
}
