using System.Text.Json;
using Maf.Lab.Plugins.Abstractions;

namespace Maf.Lab.Api.BuiltIn;

/// <summary>
/// The domains this repository ships before each becomes a plugin folder of its own (introduce-plugins decision 5g; the
/// billing, portfolio and code follow-ups move them out). Their descriptors are data — <c>&lt;id&gt;/domain.json</c> and
/// <c>&lt;id&gt;/prompt.md</c> beside this file — and their behaviour is the three classes in this folder. This folder is
/// the only place in the core assembly that names a domain; the architecture test holds every other file to that.
/// </summary>
public static class BuiltInDomains
{
    /// <summary>The ids of the built-in domains, for the domain code and the tests; the core never names them.</summary>
    public const string Billing = "billing";
    public const string Portfolio = "portfolio";

    /// <summary>Every built-in domain's id, as shipped.</summary>
    public static readonly IReadOnlyList<string> Ids = [Billing, Portfolio];

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>The built-in domains' folder in the build output.</summary>
    public static string Folder => Path.Combine(AppContext.BaseDirectory, "BuiltIn");

    /// <summary>The descriptors of the built-in domains whose ids are listed, read from their data files.</summary>
    public static IReadOnlyList<DomainDescriptor> Descriptors(IEnumerable<string> ids)
    {
        var descriptors = new List<DomainDescriptor>();
        foreach (var id in ids.Distinct(StringComparer.Ordinal))
        {
            var dir = Path.Combine(Folder, id);
            var file = Path.Combine(dir, "domain.json");
            if (!File.Exists(file))
            {
                continue;
            }
            var table = JsonSerializer.Deserialize<DomainTable>(File.ReadAllText(file), Json)
                ?? throw new InvalidOperationException($"built-in domain {id}: empty descriptor");
            var prompt = table.Prompt is { Length: > 0 } p && File.Exists(Path.Combine(dir, p)) ? File.ReadAllText(Path.Combine(dir, p)) : null;
            descriptors.Add(table.ToDescriptor(prompt));
        }
        return descriptors;
    }

    /// <summary>
    /// The capabilities a built-in domain's tool requires that are not plugins yet, each with the setting that wires it
    /// today: in use when that setting is set. Transitional — the compliance follow-up (introduce-plugins 8.1) removes
    /// its entry, after which the plugin catalogue is the only source.
    /// </summary>
    public static IReadOnlyDictionary<string, string> LegacyCapabilities { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["compliance"] = "Compliance:BaseUrl",
    };

    /// <summary>The behaviour of each built-in domain.</summary>
    public static IReadOnlyList<IDomainBehaviour> Behaviours { get; } =
        [new BillingBehaviour(), new PortfolioBehaviour()];

    /// <summary>
    /// Each built-in domain's chunk store, keyed by its id: billing's is the api's own collection, portfolio's its own.
    /// The review queue resolves a search's sources in the store of the search's domain; a domain with none has no key.
    /// </summary>
    public static void AddStores(IServiceCollection services, IConfiguration configuration)
    {
        services.AddKeyedSingleton(Billing, (sp, _) => sp.GetRequiredService<Maf.Lab.Retrieval.Store.TenantScopedMaintenance>());
        services.AddKeyedSingleton(Portfolio, (sp, _) =>
        {
            var qdrant = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<Maf.Lab.Retrieval.Configuration.QdrantOptions>>().Value;
            return new Maf.Lab.Retrieval.Store.TenantScopedMaintenance(sp.GetRequiredService<Qdrant.Client.QdrantClient>(),
                Microsoft.Extensions.Options.Options.Create(new Maf.Lab.Retrieval.Configuration.QdrantOptions
                {
                    Host = qdrant.Host, GrpcPort = qdrant.GrpcPort, Https = qdrant.Https, ApiKey = qdrant.ApiKey, PayloadM = qdrant.PayloadM,
                    Collection = configuration["Portfolio:Collection"] ?? Maf.Lab.Domain.Portfolio.PortfolioCollections.Chunks,
                    MetaCollection = configuration["Portfolio:MetaCollection"] ?? Maf.Lab.Domain.Portfolio.PortfolioCollections.Meta,
                }));
        });
    }

    /// <summary>The result type each built-in card tool declares: a card holds no free text (add-activity-cards).</summary>
    public static IReadOnlyDictionary<string, Type> CardResultTypes { get; } = new Dictionary<string, Type>
    {
        [Maf.Lab.Domain.Portfolio.PortfolioTools.GetPortfolio] = typeof(Maf.Lab.Domain.Portfolio.HouseholdPortfolio),
        [Maf.Lab.Domain.Portfolio.PortfolioTools.AumHistory] = typeof(Maf.Lab.Domain.Portfolio.AumHistory),
        [Maf.Lab.Domain.Portfolio.PortfolioTools.ListAccounts] = typeof(Maf.Lab.Domain.Portfolio.AccountList),
    };
}
