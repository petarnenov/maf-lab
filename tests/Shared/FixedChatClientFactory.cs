using Maf.Lab.Retrieval.Models;
using Microsoft.Extensions.AI;

namespace Maf.Lab.TestSupport;

/// <summary>
/// Hands out <paramref name="client"/> for every model, except <paramref name="routedModel"/> (when given), which
/// gets its own client — the seam that keeps the intent classifier's calls out of a turn's scripted conversation.
/// </summary>
public sealed class FixedChatClientFactory(IChatClient client, string? routedModel = null, IChatClient? routedClient = null) : IChatClientFactory
{
    public IChatClient CreateChatClient(string? model = null) =>
        routedModel is not null && model == routedModel && routedClient is not null ? routedClient : client;

    public ChatOptions BaseChatOptions() => new() { Temperature = 0 };
}
