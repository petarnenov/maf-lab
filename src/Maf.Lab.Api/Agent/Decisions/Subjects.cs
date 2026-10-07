namespace Maf.Lab.Api.Agent.Decisions;

/// <summary>
/// What Jev's contexts say the assistant is about: the subjects of the domains in use, from their descriptors
/// (extract-billing), in the domains' order — "fee billing and investment portfolios". The core names none itself.
/// </summary>
internal static class Subjects
{
    /// <summary>The subjects joined as a list ("a", "a and b", "a, b and c"); null when no domain in use names one.</summary>
    internal static string? Phrase(IEnumerable<string?> subjects)
    {
        var all = subjects.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!).ToList();
        return all.Count switch
        {
            0 => null,
            1 => all[0],
            _ => string.Join(", ", all.Take(all.Count - 1)) + " and " + all[^1],
        };
    }

    /// <summary>The subjects of the domains in scope.</summary>
    internal static string? Current => Phrase(DomainCatalogue.Current.All.Select(d => d.Subject));

    /// <summary>" about {subjects}", or nothing when no domain names a subject.</summary>
    internal static string About(string? phrase) => phrase is null ? "" : $" about {phrase}";
}
