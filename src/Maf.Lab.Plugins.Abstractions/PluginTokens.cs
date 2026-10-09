using Maf.Lab.Domain.Tenancy;

namespace Maf.Lab.Plugins.Abstractions;

/// <summary>A protocol client asks for a token bound to exactly one installed plugin audience.</summary>
public interface IPluginTokens
{
    Task<string> ForAsync(Principal principal, string plugin, string subjectToken, CancellationToken ct);
}
