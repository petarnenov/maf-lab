using Maf.Lab.Api.Agent;
using Maf.Lab.Plugins.Abstractions;

namespace Maf.Lab.Tests;

/// <summary>
/// Catalogues a core test builds for itself (introduce-plugins design §6, "tests build their own"): the built-in domains
/// plus a stand-in code domain — a domain whose search returns code — so a core mechanism that reads a domain's guard
/// context (citations of code places, the code context of the checks) is tested without any plugin's descriptor.
/// </summary>
public static class StandInDomains
{
    /// <summary>A domain whose search results are code: the shape the core reads, and nothing of any plugin.</summary>
    public static DomainDescriptor CodeDomain { get; } =
        new("codebase", "search_codebase", [], ["trace_code_symbol", "change_impact"], [], GuardContexts.Code, new Dictionary<string, string>());

    /// <summary>The built-in domains and the stand-in code domain, in scope for the static readers until disposed.</summary>
    public static IDisposable WithCodeDomain() =>
        DomainCatalogue.Use(DomainCatalogue.Of([.. DomainCatalogue.AllBuiltIn.All, CodeDomain], DomainCatalogue.AllBuiltIn.Behaviours));
}
