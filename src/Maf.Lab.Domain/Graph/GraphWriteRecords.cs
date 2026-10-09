using Maf.Lab.Domain.Tenancy;
namespace Maf.Lab.Retrieval.Graph;
/// <summary>A node to write: its label (one of <see cref="GraphLabels"/>), its tenant, its key within that tenant and its properties.</summary>
public sealed record GraphNode(string Label, TenantId Tenant, string Key, IReadOnlyDictionary<string, object?> Properties)
{
    /// <summary>A node whose tenant was never set (a default <see cref="TenantId"/>) has no tenant and is rejected.</summary>
    public bool HasTenant => Tenant.Value is not null;

    /// <summary>Stable over the properties, so an unchanged node is reported as unchanged.</summary>
    public string ContentHash => GraphHash.Of(Properties);
}

/// <summary>A directed relationship between two nodes identified by label, tenant and key.</summary>
/// <param name="Properties">The relationship's own properties, e.g. a BM25 weight; none for most edges.</param>
public sealed record GraphEdge(string FromLabel, TenantId FromTenant, string FromKey, string Type, string ToLabel, TenantId ToTenant, string ToKey,
    IReadOnlyDictionary<string, object?>? Properties = null);

public static class GraphHash
{
    public static string Of(IReadOnlyDictionary<string, object?> properties)
    {
        var text = string.Join("\u001f", properties.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => $"{p.Key}={Format(p.Value)}"));
        return Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)))[..16];
    }

    private static string Format(object? value) => value switch
    {
        null => "∅",
        string s => s,
        bool b => b ? "true" : "false",
        IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
        System.Collections.IEnumerable list => "[" + string.Join(",", list.Cast<object?>().Select(Format)) + "]",
        _ => value.ToString() ?? "",
    };
}
