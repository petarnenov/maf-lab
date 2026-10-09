using System.Net.Http.Headers;

namespace Maf.Lab.Plugins.Jev;

/// <summary>
/// The Jev API key, held in exactly one place. It is read from the <c>JEV_MAF_LAB</c> environment variable and leaves
/// this class only as the bearer header of a request to the Jev endpoint — never as a value a classifier, a judge, a
/// trace, a log line or a model prompt could see.
/// </summary>
public sealed class JevCredential
{
    public const string EnvironmentVariable = "JEV_MAF_LAB";

    private readonly string? _key;

    public JevCredential(IConfiguration configuration, ILogger<JevCredential> logger)
    {
        var key = configuration[EnvironmentVariable];
        _key = string.IsNullOrWhiteSpace(key) ? null : key.Trim();
        if (_key is null)
        {
            logger.LogWarning("Jev unavailable: {Variable} is not set; intent classification, tool routing, the relevance judge and " +
                "the answer check proceed as if Jev had not answered (no recognised intent, nothing routed, no gate, fused order, " +
                "answers unchecked)",
                EnvironmentVariable);
        }
    }

    public bool IsConfigured => _key is not null;

    internal void Authorize(HttpRequestMessage request)
    {
        if (_key is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _key);
        }
    }

    public override string ToString() => IsConfigured ? $"{EnvironmentVariable}=(set)" : $"{EnvironmentVariable}=(missing)";
}

/// <summary>Sets the bearer header on requests of the Jev client, and on nothing else.</summary>
public sealed class JevAuthHandler(JevCredential credential) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        credential.Authorize(request);
        return base.SendAsync(request, cancellationToken);
    }
}
