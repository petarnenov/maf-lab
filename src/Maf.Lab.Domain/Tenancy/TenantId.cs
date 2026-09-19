using System.Text.RegularExpressions;

namespace Maf.Lab.Domain.Tenancy;

/// <summary>
/// A tenant key as stored in the vector store payload: a firm id (e.g. "firm-a") or "shared".
/// Only ever constructed from a principal or from the corpus layout, never from request input.
/// </summary>
public readonly partial record struct TenantId
{
    public const string SharedValue = "shared";

    public static readonly TenantId Shared = new(SharedValue);

    public string Value { get; }

    private TenantId(string value) => Value = value;

    public bool IsShared => Value == SharedValue;

    public static TenantId Firm(string firmId)
    {
        if (!FirmPattern().IsMatch(firmId))
        {
            throw new ArgumentException($"'{firmId}' is not a valid firm id.", nameof(firmId));
        }
        return new TenantId(firmId);
    }

    /// <summary>Parses a stored tenant value ("shared" or a firm id). Returns false for anything else.</summary>
    public static bool TryParse(string? value, out TenantId tenant)
    {
        tenant = default;
        if (value == SharedValue)
        {
            tenant = Shared;
            return true;
        }
        if (value is not null && FirmPattern().IsMatch(value))
        {
            tenant = new TenantId(value);
            return true;
        }
        return false;
    }

    public override string ToString() => Value;

    [GeneratedRegex("^firm-[a-z0-9][a-z0-9-]{0,62}$")]
    private static partial Regex FirmPattern();
}
