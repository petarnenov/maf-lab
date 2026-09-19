using Maf.Lab.Domain.Admin;
using Maf.Lab.Domain.Retrieval;
using Maf.Lab.Domain.Tenancy;

namespace Maf.Lab.Indexing.Corpus;

public sealed record CorpusSnapshot(IReadOnlyList<SourceDocument> Documents, IReadOnlyList<RejectedDocument> Rejected)
{
    /// <summary>Tenants whose folder exists in the layout, even if it is now empty (so their removed documents get deleted).</summary>
    public IReadOnlySet<TenantId> LayoutTenants { get; init; } = new HashSet<TenantId>();
}

/// <summary>
/// Loads {root}/{tenant}/{sourceType}/**. The tenant is the first path segment and must be a firm id or "shared";
/// anything else is rejected and reported — never defaulted to shared.
/// </summary>
public static class CorpusLoader
{
    private static readonly Dictionary<string, string[]> Extensions = new()
    {
        [SourceType.Docs] = [".md"],
        [SourceType.Procedures] = [".txt", ".md"],
        [SourceType.Code] = [".cs", ".ts", ".tsx", ".js", ".py", ".sql"],
    };

    public static CorpusSnapshot Load(string root, IReadOnlySet<TenantId>? onlyTenants = null)
    {
        var documents = new List<SourceDocument>();
        var rejected = new List<RejectedDocument>();
        if (!Directory.Exists(root))
        {
            return new CorpusSnapshot(documents, [new RejectedDocument(root, "Corpus root does not exist.")]);
        }

        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            var name = Path.GetFileName(file);
            if (name.StartsWith('.') || relative.Split('/').Any(s => s.StartsWith('.')))
            {
                continue;
            }

            var segments = relative.Split('/');
            if (segments.Length < 3)
            {
                rejected.Add(new RejectedDocument(relative, "Document is not inside a {tenant}/{sourceType}/ folder; tenant cannot be determined."));
                continue;
            }
            if (!TenantId.TryParse(segments[0], out var tenant))
            {
                rejected.Add(new RejectedDocument(relative, $"'{segments[0]}' is not a firm id or 'shared'; tenant cannot be determined."));
                continue;
            }
            var sourceType = segments[1];
            if (!SourceType.IsKnown(sourceType))
            {
                rejected.Add(new RejectedDocument(relative, $"'{sourceType}' is not a known source type."));
                continue;
            }
            if (!Extensions[sourceType].Contains(Path.GetExtension(file).ToLowerInvariant()))
            {
                rejected.Add(new RejectedDocument(relative, $"Unsupported file type for {sourceType}."));
                continue;
            }
            if (onlyTenants is not null && !onlyTenants.Contains(tenant))
            {
                continue;
            }

            var updatedAt = new DateTimeOffset(File.GetLastWriteTimeUtc(file), TimeSpan.Zero);
            updatedAt = updatedAt.AddTicks(-(updatedAt.Ticks % TimeSpan.TicksPerSecond));
            documents.Add(new SourceDocument(tenant, sourceType, string.Join('/', segments[1..]), file, File.ReadAllText(file), updatedAt));
        }
        var layoutTenants = Directory.EnumerateDirectories(root)
            .Select(d => TenantId.TryParse(Path.GetFileName(d), out var t) ? t : (TenantId?)null)
            .Where(t => t is not null && (onlyTenants is null || onlyTenants.Contains(t.Value)))
            .Select(t => t!.Value)
            .ToHashSet();
        return new CorpusSnapshot(documents, rejected) { LayoutTenants = layoutTenants };
    }
}
