using Maf.Lab.Plugins.Abstractions;

namespace Maf.Lab.Plugins.ConversationHistory;

/// <summary>
/// The conversation list (introduce-plugins 5.4, decision 5y): installation scope, every environment, installed by
/// default. Its routes list, rename and delete the caller's own conversations through the core's
/// <see cref="IConversationStore"/>; reopening a conversation, its pending proposal and starting a new one stay core.
/// </summary>
public sealed class ConversationHistoryPlugin : IMafPlugin, IContributesEndpoints
{
    public const string PluginName = "conversation-history";

    public string Name => PluginName;

    public void MapEndpoints(IMafEndpoints endpoints) => ConversationEndpoints.Map(endpoints.Routes);
}
