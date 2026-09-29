using System.ComponentModel;
using Maf.Lab.Domain.Portfolio;
using Maf.Lab.Portfolio.Store;
using Maf.Lab.Retrieval.Auth;
using Maf.Lab.Retrieval.Tools;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Maf.Lab.Portfolio.Tools;

/// <summary>The portfolio domain's read tools. None can change anything: this server has no write.</summary>
[McpServerToolType]
public sealed class HouseholdTools(PortfolioStore store, IPrincipalAccessor principals, ILogger<HouseholdTools> logger)
{
    [McpServerTool(Name = PortfolioTools.GetPortfolio, Title = "Get household portfolio", ReadOnly = true, Idempotent = true, Destructive = false,
        OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(HouseholdPortfolio))]
    [Description(
        "Returns one account's current portfolio: its household, model portfolio, holdings by asset class with target and actual " +
        "weights, the drift of each against the model, whether any drift is outside the model's tolerance, and the total market value.\n" +
        "Use when: the user asks what an account holds, how it is allocated, or whether it has drifted and needs a rebalance.\n" +
        "Do not use for: fees or billing runs (use the billing tools), for how rebalancing works in general (use search_portfolio_documents), " +
        "or for which accounts the user has (use list_my_accounts).")]
    public CallToolResult GetPortfolio(
        [Description("The account id, e.g. 'A-1042'.")] string accountId,
        RequestContext<CallToolRequestParams>? context = null) =>
        WithInstance(Read(accountId, "Portfolio lookup", id => store.Portfolio(principals.Current, id)), context);

    [McpServerTool(Name = PortfolioTools.AumHistory, Title = "Get quarter-end AUM history", ReadOnly = true, Idempotent = true, Destructive = false,
        OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(AumHistory))]
    [Description(
        "Returns one account's quarter-end AUM valuations, oldest first, with the percentage change from each quarter to the next. " +
        "The quarter-end AUM is the figure the billing engine bills that quarter on.\n" +
        "Use when: the user asks how an account's AUM changed, or why its fee moved between quarters (together with the billing " +
        "documentation on fee tiers).\n" +
        "Do not use for: current holdings or drift (use get_household_portfolio), the fee itself (use the billing tools), or which " +
        "accounts the user has (use list_my_accounts).")]
    public CallToolResult GetAumHistory(
        [Description("The account id, e.g. 'A-1042'.")] string accountId,
        RequestContext<CallToolRequestParams>? context = null) =>
        WithInstance(Read(accountId, "AUM history lookup", id => store.History(principals.Current, id)), context);

    [McpServerTool(Name = PortfolioTools.ListAccounts, Title = "List my accounts", ReadOnly = true, Idempotent = true, Destructive = false,
        OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(AccountList))]
    [Description(
        "Lists the accounts the signed-in user can access: each account's id, name, household, model portfolio and currency, " +
        "ordered by account id. Takes no arguments; the scope is always the caller's own.\n" +
        "Use when: the user asks which accounts or households they have or can see, or asks about an account without naming its id " +
        "(call this first, then the per-account tool with the id).\n" +
        "Do not use for: one account's holdings or drift (use get_household_portfolio), its AUM (use get_aum_history), or " +
        "documentation (use search_portfolio_documents).")]
    public CallToolResult ListMyAccounts(RequestContext<CallToolRequestParams>? context = null)
    {
        try
        {
            var list = store.List(principals.Current);
            logger.LogInformation("{Tool} returned {Count} accounts", PortfolioTools.ListAccounts, list.Count);
            return WithInstance(SearchDocumentsTool.Structured(list), context);
        }
        catch (Exception ex)
        {
            logger.LogError("{Capability} failed: {ErrorType}", "Account list", ex.GetType().Name);
            return WithInstance(ToolErrors.Error(ToolErrors.ForException(ex, "Account list")), context);
        }
    }

    private CallToolResult Read<T>(string accountId, string capability, Func<string, T?> read) where T : class
    {
        try
        {
            return read(accountId ?? "") is { } value
                ? SearchDocumentsTool.Structured(value)
                // Another firm's account gets exactly this answer too: its existence is not disclosed.
                : ToolErrors.Error($"Account '{PortfolioStore.Normalize(accountId ?? "")}' was not found. Check the account id.");
        }
        catch (Exception ex)
        {
            logger.LogError("{Capability} failed: {ErrorType}", capability, ex.GetType().Name);
            return ToolErrors.Error(ToolErrors.ForException(ex, capability));
        }
    }

    private static CallToolResult WithInstance(CallToolResult result, RequestContext<CallToolRequestParams>? context)
    {
        if (SearchDocumentsTool.TraceRequested(context))
        {
            result.Meta = new System.Text.Json.Nodes.JsonObject { [SearchDocumentsTool.InstanceKey] = Hosting.InstanceIdentity.Name };
        }
        return result;
    }
}
