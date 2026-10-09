using System.Text.RegularExpressions;

namespace Maf.Lab.Tests;

/// <summary>
/// Source-level guards against domain names in the assistant core, with no transitional folder exemption.
/// Shared domain hosts retain explicit file fences until their own implementation moves; a stale fence fails.
/// </summary>
public sealed class CoreNamesNoDomainTests
{
    /// <summary>The files that still name a domain, each with the follow-up that removes it (introduce-plugins 8.1).</summary>
    private static readonly IReadOnlyDictionary<string, string> AllowList = new Dictionary<string, string>
    {
    };

    /// <summary>
    /// The retrieval library's files that still name a domain: the billing host (its Program.cs, Billing/ and its tools)
    /// and the billing graph (the template-contribution seam), each marked in its own file.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> RetrievalAllowList = new Dictionary<string, string>
    {
        ["Program.cs"] = RetrievalFollowUp,
        ["Billing/AccountFees.cs"] = RetrievalFollowUp,
        ["Billing/BillingAccountStore.cs"] = RetrievalFollowUp,
        ["Billing/BillingSeedStore.cs"] = RetrievalFollowUp,
        ["Billing/FeeAdjustmentLedger.cs"] = RetrievalFollowUp,
        ["Billing/FeeWouldGoBelowZeroException.cs"] = RetrievalFollowUp,
        ["Billing/ProposalSigner.cs"] = RetrievalFollowUp,
        ["Tools/BillingTools.cs"] = RetrievalFollowUp,
        ["Tools/BillingGraphTools.cs"] = RetrievalFollowUp,
        ["Tools/FeeAdjustmentTools.cs"] = RetrievalFollowUp,
        ["Tools/SearchDocumentsTool.cs"] = RetrievalFollowUp,
        ["Graph/GraphQueries.cs"] = RetrievalFollowUp,
        ["Graph/GraphTemplates.cs"] = RetrievalFollowUp,
    };

    private const string RetrievalFollowUp = "extract-evals-plugin";

    /// <summary>Portfolio's server project: every file is its host's, each marked in its own file.</summary>
    private static readonly IReadOnlyDictionary<string, string> PortfolioAllowList = new Dictionary<string, string>
    {
        ["Program.cs"] = RetrievalFollowUp,
        ["Store/PortfolioStore.cs"] = RetrievalFollowUp,
        ["Tools/HouseholdTools.cs"] = RetrievalFollowUp,
        ["Tools/PortfolioSearchTool.cs"] = RetrievalFollowUp,
    };

    // A domain's types count too: a reference to Maf.Lab.Domain.Billing is billing's, whatever the file calls it.
    private static readonly Regex NamesADomain =
        new(@"BuiltInDomains\.(Billing|Portfolio|Codebase)\b|""(billing|portfolio|codebase)""|Maf\.Lab\.Domain\.(Billing|Portfolio)\b", RegexOptions.Compiled);

    /// <summary>The retrieval library's rule: any identifier or string that contains a domain's name.</summary>
    private static readonly Regex ContainsADomain = new("billing|portfolio|codebase", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static string ApiRoot => Path.Combine(CorpusLoaderTests.RepoRoot(), "src", "Maf.Lab.Api");

    private static string RetrievalRoot => Path.Combine(CorpusLoaderTests.RepoRoot(), "src", "Maf.Lab.Retrieval");

    private static string PortfolioRoot => Path.Combine(CorpusLoaderTests.RepoRoot(), "src", "Maf.Lab.Portfolio");

    /// <summary>The source with its comments removed (strings kept, so a literal in a string still counts).</summary>
    internal static string StripComments(string source) =>
        Regex.Replace(source, @"(""(?:\\.|[^""\\])*"")|//[^\n]*|/\*.*?\*/", m => m.Groups[1].Success ? m.Value : "", RegexOptions.Singleline);

    internal static IReadOnlyList<string> Violations(IReadOnlyDictionary<string, string> files) =>
    [
        .. files.Where(f => !AllowList.ContainsKey(f.Key))
            .SelectMany(f => NamesADomain.Matches(StripComments(f.Value)).Select(m => $"{f.Key}: {m.Value}")),
    ];

    internal static IReadOnlyList<string> RetrievalViolations(IReadOnlyDictionary<string, string> files) =>
        LibraryViolations(files, RetrievalAllowList);

    /// <summary>A library root's rule: outside its allow-list, no name or string contains a domain's.</summary>
    internal static IReadOnlyList<string> LibraryViolations(IReadOnlyDictionary<string, string> files, IReadOnlyDictionary<string, string> allowed) =>
    [
        .. files.Where(f => !allowed.ContainsKey(f.Key))
            .SelectMany(f => ContainsADomain.Matches(StripComments(f.Value)).Select(m => $"{f.Key}: {m.Value}")),
    ];

    private static IReadOnlyDictionary<string, string> ApiSources() => Sources(ApiRoot);

    private static IReadOnlyDictionary<string, string> RetrievalSources() => Sources(RetrievalRoot);

    private static IReadOnlyDictionary<string, string> PortfolioSources() => Sources(PortfolioRoot);

    private static IReadOnlyDictionary<string, string> Sources(string root) =>
        Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                && !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .ToDictionary(p => Path.GetRelativePath(root, p).Replace('\\', '/'), File.ReadAllText);

    [Fact]
    public void No_core_source_file_names_a_domain()
    {
        Assert.Empty(Violations(ApiSources()));
    }

    [Fact]
    public void The_core_has_no_transitional_domain_files_or_allow_list_entries()
    {
        Assert.Empty(AllowList);
        Assert.DoesNotContain(ApiSources().Keys, file => file.StartsWith("BuiltIn/", StringComparison.Ordinal));
    }

    [Fact]
    public void No_retrieval_library_file_outside_its_allow_list_names_a_domain()
    {
        Assert.Empty(RetrievalViolations(RetrievalSources()));
    }

    [Fact]
    public void Each_allowed_retrieval_file_still_names_a_domain_and_says_which_follow_up_removes_it()
    {
        var sources = RetrievalSources();
        foreach (var (file, plugin) in RetrievalAllowList)
        {
            Assert.True(sources.ContainsKey(file), $"{file} is allow-listed but does not exist");
            Assert.Matches(ContainsADomain, StripComments(sources[file]));
            Assert.Contains($"// names a domain until the {plugin} follow-up moves it (introduce-plugins 8.1)", sources[file]);
        }
    }

    [Fact]
    public void No_portfolio_server_file_outside_its_allow_list_names_a_domain()
    {
        Assert.Empty(LibraryViolations(PortfolioSources(), PortfolioAllowList));
    }

    [Fact]
    public void Each_allowed_portfolio_server_file_still_names_a_domain_and_says_which_follow_up_removes_it()
    {
        var sources = PortfolioSources();
        foreach (var (file, plugin) in PortfolioAllowList)
        {
            Assert.True(sources.ContainsKey(file), $"{file} is allow-listed but does not exist");
            Assert.Matches(ContainsADomain, StripComments(sources[file]));
            Assert.Contains($"// names a domain until the {plugin} follow-up moves it (introduce-plugins 8.1)", sources[file]);
        }
    }

    [Fact]
    public void The_retrieval_scanner_catches_a_domain_inside_a_name()
    {
        var planted = new Dictionary<string, string>
        {
            ["Search/Planted.cs"] = "class BillingHelper { }",
            ["Store/Commented.cs"] = "// billing\nclass C { }",
            ["Tools/BillingTools.cs"] = "class BillingTools { }",
        };

        Assert.Equal(["Search/Planted.cs: Billing"], RetrievalViolations(planted));
    }

    [Fact]
    public void The_scanner_catches_a_planted_violation_and_ignores_comments()
    {
        var planted = new Dictionary<string, string>
        {
            ["Agent/Planted.cs"] = "class P { string D = \"billing\"; }",
            ["Agent/Constant.cs"] = "class Q { string D = BuiltIn.BuiltInDomains.Portfolio; }",
            ["Agent/Commented.cs"] = "// the billing domain, \"billing\"\nclass R { /* \"codebase\" */ string S = \"// \"; }",
            ["BuiltIn/Domain.cs"] = "class S { string D = \"codebase\"; }",
        };

        Assert.Equal(["Agent/Planted.cs: \"billing\"", "Agent/Constant.cs: BuiltInDomains.Portfolio", "BuiltIn/Domain.cs: \"codebase\""], Violations(planted));
    }
}

/// <summary>
/// The static facades' contract (introduce-plugins design §6): inside a scope they read the catalogue put there — a
/// request's or a turn's frozen view — and outside any scope no domain, since every domain is a plugin's.
/// </summary>
public sealed class DomainCatalogueFacadeTests
{
    [Fact]
    public void A_static_reader_sees_the_catalogue_in_scope_and_no_domain_outside_it()
    {
        using (Maf.Lab.Api.Agent.DomainCatalogue.Use(Maf.Lab.Api.Agent.DomainCatalogue.Of([StandInDomains.BillingDomain])))
        {
            Assert.Equal(["billing"], Maf.Lab.Api.Agent.Domains.All);
            Assert.Null(Maf.Lab.Api.Agent.Domains.OfTool("search_portfolio_documents"));
            Assert.Equal("", Maf.Lab.Api.Agent.ChatTurnRunner.ClearedFocusNote);
        }

        using (Maf.Lab.Api.Agent.DomainCatalogue.Use(StandInDomains.PortfolioOnly))
        {
            Assert.Equal("portfolio", Maf.Lab.Api.Agent.Domains.OfTool("search_portfolio_documents"));
            Assert.NotEqual("", Maf.Lab.Api.Agent.ChatTurnRunner.ClearedFocusNote);
        }

        Assert.Empty(Maf.Lab.Api.Agent.Domains.All);
        Assert.Null(Maf.Lab.Api.Agent.Domains.OfTool("search_portfolio_documents"));
    }

    [Fact]
    public void A_frozen_view_keeps_its_domains()
    {
        var all = StandInDomains.WithBilling;

        var frozen = all.Freeze();

        Assert.Equal(all.Ids, frozen.Ids);
        Assert.Same(frozen, frozen.Freeze());
    }
}
