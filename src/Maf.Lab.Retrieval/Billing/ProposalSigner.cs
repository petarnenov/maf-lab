using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace Maf.Lab.Retrieval.Billing;

/// <summary>
/// What a proposal fixes at the moment it is made. It travels back through the model, so it carries no free
/// text: the advisor's reason is here only as a digest, enough to tell whether it was swapped.
/// </summary>
public sealed record FeeAdjustmentProposalState(
    string AdjustmentId,
    string FirmId,
    string UserId,
    string AccountId,
    decimal Amount,
    string Currency,
    string ReasonDigest,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt);

/// <summary>Why a state was not accepted. The caller turns this into words; it never explains the mechanism.</summary>
public abstract record ProposalCheck
{
    public sealed record Ok(FeeAdjustmentProposalState Proposal) : ProposalCheck;

    /// <summary>The signature does not match the payload, or the token is not one we issued.</summary>
    public sealed record Altered : ProposalCheck;

    /// <summary>Well-formed and ours, but too old to act on.</summary>
    public sealed record Expired : ProposalCheck;
}

/// <summary>
/// Signs a proposal so the second call executes what the first one proposed.
///
/// Symmetric, because the issuer and the verifier are the same service — a real deployment would sign
/// asymmetrically so a verifier need not hold the secret. The key is configuration, shared by the replicas,
/// so a proposal made against one is honoured by another.
/// </summary>
public sealed class ProposalSigner
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly byte[] _key;
    private readonly TimeSpan _validFor;
    private readonly TimeProvider _time;

    public ProposalSigner(IConfiguration configuration, TimeProvider time)
    {
        var key = configuration["Billing:ProposalSigningKey"] is { Length: > 0 } configured
            ? configured
            : configuration["Auth:SigningKey"] ?? throw new InvalidOperationException("No signing key is configured for fee-adjustment proposals.");
        _key = Encoding.UTF8.GetBytes(key);
        _validFor = configuration["Billing:ProposalValidFor"] is { Length: > 0 } window
            ? TimeSpan.Parse(window, System.Globalization.CultureInfo.InvariantCulture)
            : TimeSpan.FromMinutes(30);
        _time = time;
    }

    internal ProposalSigner(string key, TimeProvider time, TimeSpan? validFor = null)
    {
        _key = Encoding.UTF8.GetBytes(key);
        _validFor = validFor ?? TimeSpan.FromMinutes(30);
        _time = time;
    }

    public TimeSpan ValidFor => _validFor;

    /// <summary>The digest that goes into a proposal in place of the advisor's words.</summary>
    public static string DigestOf(string reason) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(reason ?? "")));

    public string Issue(FeeAdjustmentProposalState proposal)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(proposal, Json);
        return $"{Base64Url(payload)}.{Base64Url(HMACSHA256.HashData(_key, payload))}";
    }

    public ProposalCheck Verify(string? state)
    {
        if (state is not { Length: > 0 })
        {
            return new ProposalCheck.Altered();
        }

        var dot = state.IndexOf('.');
        if (dot <= 0 || dot == state.Length - 1)
        {
            return new ProposalCheck.Altered();
        }

        byte[] payload;
        byte[] signature;
        try
        {
            payload = FromBase64Url(state[..dot]);
            signature = FromBase64Url(state[(dot + 1)..]);
        }
        catch (FormatException)
        {
            return new ProposalCheck.Altered();
        }

        if (!CryptographicOperations.FixedTimeEquals(HMACSHA256.HashData(_key, payload), signature))
        {
            return new ProposalCheck.Altered();
        }

        FeeAdjustmentProposalState? proposal;
        try
        {
            proposal = JsonSerializer.Deserialize<FeeAdjustmentProposalState>(payload, Json);
        }
        catch (JsonException)
        {
            return new ProposalCheck.Altered();
        }

        if (proposal is null)
        {
            return new ProposalCheck.Altered();
        }

        return proposal.ExpiresAt <= _time.GetUtcNow()
            ? new ProposalCheck.Expired()
            : new ProposalCheck.Ok(proposal);
    }

    private static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(padded.PadRight((padded.Length + 3) / 4 * 4, '='));
    }
}
