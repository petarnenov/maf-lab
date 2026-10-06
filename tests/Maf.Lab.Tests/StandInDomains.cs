using Maf.Lab.Api.Agent;
using Maf.Lab.Plugins.Abstractions;

namespace Maf.Lab.Tests;

/// <summary>
/// Catalogues a core test builds for itself (introduce-plugins design §6, "tests build their own"): the built-in domains,
/// the stand-in billing domain the shared fakes speak (installed by default in every <see cref="ApiFactory"/>), and a
/// stand-in code domain — a domain whose search returns code — so a core mechanism that reads a domain's guard
/// context (citations of code places, the code context of the checks) is tested without any plugin's descriptor.
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

    /// <summary>What an <see cref="ApiFactory"/> installs unless a test says otherwise: the stand-in billing domain.</summary>
    public static IReadOnlyList<PluginManifest> Installed { get; } = [BillingManifest];

    /// <summary>The built-in domains with the stand-in billing domain and its behaviour, as the static readers see them.</summary>
    public static DomainCatalogue WithBilling { get; } = DomainCatalogue.Of(
        [.. DomainCatalogue.AllBuiltIn.All.Append(BillingDomain).OrderBy(d => d.Order).ThenBy(d => d.Id, StringComparer.Ordinal)], [.. DomainCatalogue.AllBuiltIn.Behaviours, new Plugins.StandInBillingBehaviour()]);

    /// <summary>A domain whose search results are code: the shape the core reads, and nothing of any plugin.</summary>
    public static DomainDescriptor CodeDomain { get; } =
        new("codebase", "search_codebase", [], ["trace_code_symbol", "change_impact"], [], GuardContexts.Code, new Dictionary<string, string>());

    /// <summary>The built-in domains and the stand-in code domain, in scope for the static readers until disposed.</summary>
    public static IDisposable WithCodeDomain() =>
        DomainCatalogue.Use(DomainCatalogue.Of([.. WithBilling.All, CodeDomain], WithBilling.Behaviours));
}
