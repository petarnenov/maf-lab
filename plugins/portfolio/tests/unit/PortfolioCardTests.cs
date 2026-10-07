using System.Reflection;
using Maf.Lab.Domain.Portfolio;

namespace Maf.Lab.Tests;

/// <summary>
/// Portfolio's data cards (add-activity-cards), moved with the domain (extract-portfolio): which of its reads travel to
/// the client as a card, and that no card type carries free text — a new string field on a carded result has to be
/// reviewed here before its content can reach a browser.
/// </summary>
public class PortfolioCardTests : IDisposable
{
    private readonly IDisposable _domains = PortfolioPluginSupport.Use();

    public void Dispose() => _domains.Dispose();

    /// <summary>
    /// Every string a card may carry, by type. A string property not named here fails the test: a new text field on a
    /// carded result has to be looked at before its content can reach a browser.
    /// </summary>
    private static readonly Dictionary<Type, string[]> PermittedStrings = new()
    {
        [typeof(HouseholdPortfolio)] = ["AccountId", "AccountName", "HouseholdId", "ModelPortfolio", "Currency"],
        [typeof(HoldingView)] = ["AssetClass", "TradeSide"],
        [typeof(AumHistory)] = ["AccountId", "HouseholdId", "Currency"],
        [typeof(AumPoint)] = [],
        [typeof(AccountList)] = [],
        [typeof(AccountSummary)] = ["AccountId", "Name", "HouseholdId", "ModelPortfolio", "Currency"],
    };

    /// <summary>The result type each card tool declares (the server's DTOs): what the walk below reviews.</summary>
    private static readonly Dictionary<string, Type> ResultTypes = new()
    {
        [PortfolioTools.GetPortfolio] = typeof(HouseholdPortfolio),
        [PortfolioTools.AumHistory] = typeof(AumHistory),
        [PortfolioTools.ListAccounts] = typeof(AccountList),
    };

    [Fact]
    public void The_allow_list_names_the_three_portfolio_reads()
    {
        Assert.Equal(ResultTypes.Keys.Order(StringComparer.Ordinal), Maf.Lab.Api.Agent.DataCards.Tools.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(
            [(PortfolioTools.AumHistory, "maf-lab/aum-history"), (PortfolioTools.GetPortfolio, "maf-lab/holdings"), (PortfolioTools.ListAccounts, "maf-lab/accounts")],
            Maf.Lab.Api.Agent.DataCards.Tools.OrderBy(c => c.Key, StringComparer.Ordinal).Select(c => (c.Key, c.Value)));
    }

    [Fact]
    public void A_carded_result_type_carries_no_free_text()
    {
        foreach (var type in ResultTypes.Values)
        {
            foreach (var (owner, property) in StringProperties(type))
            {
                Assert.True(PermittedStrings.TryGetValue(owner, out var permitted), $"{owner.Name} is not reviewed for cards");
                Assert.True(permitted.Contains(property), $"{owner.Name}.{property} is a string no card may carry until reviewed");
            }
        }
    }

    private static IEnumerable<(Type Owner, string Property)> StringProperties(Type type, HashSet<Type>? seen = null)
    {
        seen ??= [];
        if (!seen.Add(type))
        {
            yield break;
        }
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var t = property.PropertyType;
            if (t == typeof(string))
            {
                yield return (type, property.Name);
            }
            else if (t.IsGenericType && t.GetGenericArguments() is [var item] && item.Namespace == typeof(HoldingView).Namespace)
            {
                foreach (var nested in StringProperties(item, seen))
                {
                    yield return nested;
                }
            }
            else if (t.Namespace == typeof(HoldingView).Namespace && t.IsClass)
            {
                foreach (var nested in StringProperties(t, seen))
                {
                    yield return nested;
                }
            }
        }
    }
}
