using System.Security.Cryptography;
using System.Text;
using Maf.Lab.Domain.Tenancy;

namespace Maf.Lab.Indexing.Corpus;

/// <summary>A corpus file with its tenant (from the layout, never defaulted) and stable identity.</summary>
public sealed record SourceDocument(TenantId Tenant, string SourceType, string RelativePath, string FullPath, string Content, DateTimeOffset UpdatedAt)
{
    /// <summary>Stable and human-readable: "{tenant}/{path within tenant}". Eval datasets reference it.</summary>
    public string DocId => $"{Tenant.Value}/{RelativePath}";

    /// <summary>Path shown to the model and users; excludes the tenant segment.</summary>
    public string SourcePath => RelativePath;

    public string ContentHash { get; } = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Content)))[..16];
}
