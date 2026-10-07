using Maf.Lab.Api.Agent;
using Maf.Lab.Plugins.Abstractions;

namespace Maf.Lab.Tests;

/// <summary>
/// Catalogues a core test builds for itself (introduce-plugins design §6, "tests build their own"): the stand-in billing
/// and portfolio domains the shared fakes speak (installed by default in every <see cref="ApiFactory"/>), and a stand-in
/// code domain — a domain whose search returns code — so a core mechanism that reads a domain's guard context (citations
/// of code places, the code context of the checks) is tested without any plugin's descriptor. No domain is built in.
/// </summary>
public static class StandInDomains
{
    /// <summary>
    /// The billing-shaped domain the shared fakes speak (<see cref="Plugins.FixtureBillingPlugin"/>), pinned equal to the
    /// billing plugin's table by that plugin's drift test.
    /// </summary>
    public static DomainDescriptor BillingDomain { get; } = Plugins.FixtureBillingPlugin.Domain.ToDescriptor();

    /// <summary>The stand-in billing domain's manifest, as an installed set carries it.</summary>
    public static PluginManifest BillingManifest { get; } = Plugins.FixtureBillingPlugin.Manifest();

    /// <summary>
    /// The portfolio-shaped domain the shared fakes speak (<see cref="Plugins.FixturePortfolioPlugin"/>), pinned equal to
    /// the portfolio plugin's table by that plugin's drift test.
    /// </summary>
    public static DomainDescriptor PortfolioDomain { get; } = Plugins.FixturePortfolioPlugin.Domain.ToDescriptor();

    /// <summary>The stand-in portfolio domain's manifest, as an installed set carries it.</summary>
    public static PluginManifest PortfolioManifest { get; } = Plugins.FixturePortfolioPlugin.Manifest();

    /// <summary>What an <see cref="ApiFactory"/> installs unless a test says otherwise: the stand-in billing and portfolio domains.</summary>
    public static IReadOnlyList<PluginManifest> Installed { get; } = [BillingManifest, PortfolioManifest];

    /// <summary>The stand-in billing and portfolio domains with their behaviours, as the static readers see them.</summary>
    public static DomainCatalogue WithBilling { get; } = DomainCatalogue.Of(
        [BillingDomain, PortfolioDomain], [new Plugins.StandInBillingBehaviour(), new Plugins.StandInPortfolioBehaviour()]);

    /// <summary>The stand-in portfolio domain alone.</summary>
    public static DomainCatalogue PortfolioOnly { get; } = DomainCatalogue.Of([PortfolioDomain], [new Plugins.StandInPortfolioBehaviour()]);

    /// <summary>A domain whose search results are code: the shape the core reads, and nothing of any plugin.</summary>
    public static DomainDescriptor CodeDomain { get; } =
        new("codebase", "search_codebase", [], ["trace_code_symbol", "change_impact"], [], GuardContexts.Code, new Dictionary<string, string>());

    /// <summary>The stand-in domains and the stand-in code domain, in scope for the static readers until disposed.</summary>
    public static IDisposable WithCodeDomain() =>
        DomainCatalogue.Use(DomainCatalogue.Of([.. WithBilling.All, CodeDomain], WithBilling.Behaviours));
}
