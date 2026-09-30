using System.Net;

namespace Maf.Lab.TestGen;

/// <summary>
/// Whether a model provider said no for a reason that will not go away in a minute: not authorised, not found, or —
/// as Ollama Cloud words it — not included in the account's usage. A timeout or a 5xx is not a refusal.
/// </summary>
public static class ProviderRefusal
{
    public static bool Is(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (e is HttpRequestException { StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound or HttpStatusCode.PaymentRequired })
            {
                return true;
            }
            var message = e.Message;
            if (message.Contains("not included", StringComparison.OrdinalIgnoreCase)
                || message.Contains("not found", StringComparison.OrdinalIgnoreCase)
                || message.Contains("unauthorized", StringComparison.OrdinalIgnoreCase)
                || message.Contains("forbidden", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }
}
