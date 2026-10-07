namespace Maf.Lab.Api.Agent;

/// <summary>
/// The tools whose result may travel to the client as a data card (add-activity-cards), each with its activity type, as
/// the domains in use describe them (introduce-plugins decision 6). A card holds no free text: each domain plugin
/// pins that for its own result types (portfolio's tests walk every string property of each).
/// </summary>
public static class DataCards
{
    public static IReadOnlyDictionary<string, string> Tools =>
        DomainCatalogue.Current.All.SelectMany(d => d.CardTypes)
            .DistinctBy(kv => kv.Key)
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);

    /// <summary>A card's message id, derived from the tool call it came from.</summary>
    public static string MessageId(string callId) => $"card-{callId}";
}
