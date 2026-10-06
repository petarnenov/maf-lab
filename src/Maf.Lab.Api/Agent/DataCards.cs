namespace Maf.Lab.Api.Agent;

/// <summary>
/// The tools whose result may travel to the client as a data card (add-activity-cards), each with its activity type
/// and the result type it declares, as the domains in use describe them (introduce-plugins decision 6). Only a result
/// type with no free-text field belongs here — a test walks every string property of each type against the names it may
/// carry — so a card holds numbers, flags, dates, ids and the firm's own account and household names, never text a
/// user, a document or a record note wrote. A plugin domain's card declares no CLR type here (<see cref="object"/>).
/// </summary>
public static class DataCards
{
    public static IReadOnlyDictionary<string, (string ActivityType, Type ResultType)> Tools =>
        DomainCatalogue.Current.All.SelectMany(d => d.CardTypes)
            .DistinctBy(kv => kv.Key)
            .ToDictionary(kv => kv.Key,
                kv => (kv.Value, BuiltIn.BuiltInDomains.CardResultTypes.GetValueOrDefault(kv.Key) ?? typeof(object)),
                StringComparer.Ordinal);

    /// <summary>A card's message id, derived from the tool call it came from.</summary>
    public static string MessageId(string callId) => $"card-{callId}";
}
