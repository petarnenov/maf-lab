using Maf.Lab.Retrieval.Models;
using Microsoft.Extensions.AI;

namespace Maf.Lab.TestSupport;

public sealed class FixedChatClientFactory(IChatClient client) : IChatClientFactory
{
    public IChatClient CreateChatClient(string? model = null) => client;
    public ChatOptions BaseChatOptions() => new() { Temperature = 0 };
}
