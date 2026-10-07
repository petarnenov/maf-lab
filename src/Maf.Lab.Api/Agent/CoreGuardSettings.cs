using Maf.Lab.Plugins.Abstractions;
using Microsoft.Extensions.Options;

namespace Maf.Lab.Api.Agent;

/// <summary>The core's side of <see cref="IGuardSettings"/>: the guard's options as bound, the cross-tenant threshold resolved.</summary>
public sealed class CoreGuardSettings(IOptions<GuardOptions> options) : IGuardSettings
{
    public GuardSettings Current
    {
        get
        {
            var g = options.Value;
            var crossTenant = g.PromptBlockAtByQuestion.TryGetValue("guard_cross_tenant", out var t) ? t : g.PromptBlockAt;
            return new GuardSettings(g.Enabled, g.PromptBlockAt, g.ContentWithholdAt, crossTenant);
        }
    }
}
