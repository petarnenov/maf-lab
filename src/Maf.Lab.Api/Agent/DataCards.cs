using System.Collections.Frozen;
using Maf.Lab.Domain.Portfolio;

namespace Maf.Lab.Api.Agent;

/// <summary>
/// The tools whose result may travel to the client as a data card (add-activity-cards), each with its activity type
/// and the result type it declares. Only a result type with no free-text field belongs here — a test walks every
/// string property of each type against the names it may carry — so a card holds numbers, flags, dates, ids and the
/// firm's own account and household names, never text a user, a document or a record note wrote.
/// </summary>
public static class DataCards
{
    public static readonly FrozenDictionary<string, (string ActivityType, Type ResultType)> Tools =
        new Dictionary<string, (string, Type)>
        {
            [PortfolioTools.GetPortfolio] = ("maf-lab/holdings", typeof(HouseholdPortfolio)),
            [PortfolioTools.AumHistory] = ("maf-lab/aum-history", typeof(AumHistory)),
            [PortfolioTools.ListAccounts] = ("maf-lab/accounts", typeof(AccountList)),
        }.ToFrozenDictionary();

    /// <summary>A card's message id, derived from the tool call it came from.</summary>
    public static string MessageId(string callId) => $"card-{callId}";
}
