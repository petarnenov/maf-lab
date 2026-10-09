using Maf.Lab.TestGen;
using Maf.Lab.Retrieval.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Maf.Lab.Plugins.Coverage;

/// <summary>Whether the configured account can use a model right now, and if not, why (in words fit for the picker).</summary>
public sealed record Availability(bool Available, string? Reason);

/// <summary>
/// Asks the provider, with a one-token request carrying a fixed probe string and no repository content, whether a
/// model answers for this account. The answer is reused for a while, per replica. A refusal the account can do
/// nothing about quickly (not in the plan, unknown model, not authorised) marks the model unavailable.
/// </summary>
public sealed class ModelAvailability(IChatClientFactory models, IMemoryCache cache, IOptions<TestAgentOptions> options,
    ILogger<ModelAvailability> logger)
{
    /// <summary>The whole probe. Nothing from the repository or a user is ever sent with it.</summary>
    public const string Probe = "Reply with the single word OK.";

    public async Task<Availability> CheckAsync(string tag, CancellationToken ct)
    {
        if (cache.TryGetValue<Availability>(Key(tag), out var known) && known is not null)
        {
            return known;
        }
        var answer = await AskAsync(tag, ct);
        // A transient failure is not an answer worth keeping for ten minutes.
        var keepFor = answer.Available || answer.Reason != CouldNotCheck ? options.Value.AvailabilityCacheFor : TimeSpan.FromSeconds(30);
        cache.Set(Key(tag), answer, keepFor);
        return answer;
    }

    public const string NotInPlan = "Not available to this account.";
    public const string CouldNotCheck = "Could not check availability; try again shortly.";

    private async Task<Availability> AskAsync(string tag, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            var chat = models.CreateChatClient(tag);
            var chatOptions = models.BaseChatOptions();
            chatOptions.MaxOutputTokens = 1;
            await chat.GetResponseAsync([new ChatMessage(ChatRole.User, Probe)], chatOptions, timeout.Token);
            return new Availability(true, null);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("model availability {Model}: timed out", tag);
            return new Availability(false, CouldNotCheck);
        }
        catch (Exception ex) when (IsRefusal(ex))
        {
            logger.LogInformation("model availability {Model}: refused ({ErrorType})", tag, ex.GetType().Name);
            return new Availability(false, NotInPlan);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning("model availability {Model}: {ErrorType}", tag, ex.GetType().Name);
            return new Availability(false, CouldNotCheck);
        }
    }

    internal static bool IsRefusal(Exception ex) => ProviderRefusal.Is(ex);

    private static string Key(string tag) => $"model-availability:{tag}";
}
