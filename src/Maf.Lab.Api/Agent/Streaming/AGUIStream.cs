using System.Collections.Frozen;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using AGUI.Abstractions;
using Maf.Lab.Domain.Chat;
using Maf.Lab.Domain.Portfolio;
using Maf.Lab.Domain.Tracing;

namespace Maf.Lab.Api.Agent.Streaming;

/// <summary>
/// How this system's events are written on the wire, and the two names it adds to the protocol.
/// </summary>
public static class AGUIStream
{
    /// <summary>
    /// The protocol's own types are source-generated; ours are not. Combining the resolvers is what lets one
    /// options instance serialize both — without it every event fails with a missing-metadata error.
    /// </summary>
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        TypeInfoResolver = JsonTypeInfoResolver.Combine(
            AGUIJsonUtilities.DefaultTypeInfoResolver,
            new DefaultJsonTypeInfoResolver()),
    };

    /// <summary>The sources of an answer. The protocol has no word for them, so they travel as an extension.</summary>
    public const string SourcesEvent = "maf-lab/sources";

    /// <summary>One behind-the-scenes trace event of a turn.</summary>
    public const string TraceEvent = "maf-lab/trace";

    /// <summary>
    /// The tools whose result may travel to the client as a data card (add-activity-cards), each with its activity type
    /// and the result type it declares. Only a result type with no free-text field belongs here — a test walks every
    /// string property of each type against the names it may carry — so a card holds numbers, flags, dates, ids and the
    /// firm's own account and household names, never text a user, a document or a record note wrote.
    /// </summary>
    public static readonly FrozenDictionary<string, (string ActivityType, Type ResultType)> Cards =
        new Dictionary<string, (string, Type)>
        {
            [PortfolioTools.GetPortfolio] = ("maf-lab/holdings", typeof(HouseholdPortfolio)),
            [PortfolioTools.AumHistory] = ("maf-lab/aum-history", typeof(AumHistory)),
            [PortfolioTools.ListAccounts] = ("maf-lab/accounts", typeof(AccountList)),
        }.ToFrozenDictionary();

    /// <summary>A data card: the protocol's activity message, keyed by the tool call it came from.</summary>
    public static ActivitySnapshotEvent Card(string callId, string activityType, JsonElement content) => new()
    {
        MessageId = CardMessageId(callId),
        ActivityType = activityType,
        Content = content,
    };

    public static string CardMessageId(string callId) => $"card-{callId}";

    /// <summary>
    /// The run's shared state (add-focus-state): the conversation's account in focus, as an id or null. Nothing else
    /// travels in it — no name, no holdings, no text.
    /// </summary>
    public static StateSnapshotEvent State(string? focusAccountId) => new()
    {
        Snapshot = JsonSerializer.SerializeToElement(
            new { focus = focusAccountId is null ? null : new { accountId = focusAccountId } }, Json),
    };

    public static CustomEvent Sources(IReadOnlyList<SourceRef> sources) => new()
    {
        Name = SourcesEvent,
        Value = JsonSerializer.SerializeToElement(new { sources }, Json),
    };

    public static CustomEvent Trace(TraceEvent traceEvent) => new()
    {
        Name = TraceEvent,
        Value = JsonSerializer.SerializeToElement(traceEvent, Json),
    };

    /// <summary>The SSE frame name: the protocol's own discriminator, so the frame says what the payload is.</summary>
    public static string FrameName(BaseEvent e) => e.Type;

    public static string Serialize(BaseEvent e) => JsonSerializer.Serialize(e, e.GetType(), Json);
}
