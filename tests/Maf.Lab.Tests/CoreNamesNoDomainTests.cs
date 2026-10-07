using System.Text.RegularExpressions;

namespace Maf.Lab.Tests;

/// <summary>
/// The core names no domain (introduce-plugins 5g, task 4.6): outside <c>src/Maf.Lab.Api/BuiltIn/</c>, the transitional
/// home of the shipped domains, no api source file names billing, portfolio or codebase — except the consumers whose
/// contract a follow-up plugin moves, each listed here and marked in its own file. The retrieval library is held to the
/// same rule more strictly (extract-billing): any name containing a domain's, so a new billing type in the library fails
/// too; its allow-list is the billing host and the billing graph, which stay there until extract-evals-plugin. The scan
/// reads source text, not IL: the ids are constants, which the compiler inlines.
/// </summary>
public sealed class CoreNamesNoDomainTests
{
    /// <summary>The files that still name a domain, each with the follow-up that removes it (introduce-plugins 8.1).</summary>
    private static readonly IReadOnlyDictionary<string, string> AllowList = new Dictionary<string, string>
    {
        ["Endpoints/FeedbackEndpoints.cs"] = "feedback-review",
        ["Topology/TopologyProbe.cs"] = "topology",
        ["Agent/JevStatistics.cs"] = "insights",
        ["A2A/BillingAgentCard.cs"] = "a2a",
        ["A2A/BillingAgentHandler.cs"] = "a2a",
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
        ["Graph/GraphSchema.cs"] = RetrievalFollowUp,
        ["Graph/GraphQueries.cs"] = RetrievalFollowUp,
        ["Graph/GraphTemplates.cs"] = RetrievalFollowUp,
    };

    private const string RetrievalFollowUp = "extract-evals-plugin";

    // A domain's types count too: a reference to Maf.Lab.Domain.Billing is billing's, whatever the file calls it.
    private static readonly Regex NamesADomain =
        new(@"BuiltInDomains\.(Billing|Portfolio|Codebase)\b|""(billing|portfolio|codebase)""|Maf\.Lab\.Domain\.Billing\b", RegexOptions.Compiled);

    /// <summary>The retrieval library's rule: any identifier or string that contains a domain's name.</summary>
    private static readonly Regex ContainsADomain = new("billing|portfolio|codebase", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static string ApiRoot => Path.Combine(CorpusLoaderTests.RepoRoot(), "src", "Maf.Lab.Api");

    private static string RetrievalRoot => Path.Combine(CorpusLoaderTests.RepoRoot(), "src", "Maf.Lab.Retrieval");

    /// <summary>The source with its comments removed (strings kept, so a literal in a string still counts).</summary>
    internal static string StripComments(string source) =>
        Regex.Replace(source, @"(""(?:\\.|[^""\\])*"")|//[^\n]*|/\*.*?\*/", m => m.Groups[1].Success ? m.Value : "", RegexOptions.Singleline);

    internal static IReadOnlyList<string> Violations(IReadOnlyDictionary<string, string> files) =>
    [
        .. files.Where(f => !f.Key.StartsWith("BuiltIn/", StringComparison.Ordinal) && !AllowList.ContainsKey(f.Key))
            .SelectMany(f => NamesADomain.Matches(StripComments(f.Value)).Select(m => $"{f.Key}: {m.Value}")),
    ];

    internal static IReadOnlyList<string> RetrievalViolations(IReadOnlyDictionary<string, string> files) =>
    [
        .. files.Where(f => !RetrievalAllowList.ContainsKey(f.Key))
            .SelectMany(f => ContainsADomain.Matches(StripComments(f.Value)).Select(m => $"{f.Key}: {m.Value}")),
    ];

    private static IReadOnlyDictionary<string, string> ApiSources() => Sources(ApiRoot);

    private static IReadOnlyDictionary<string, string> RetrievalSources() => Sources(RetrievalRoot);

    private static IReadOnlyDictionary<string, string> Sources(string root) =>
        Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                && !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .ToDictionary(p => Path.GetRelativePath(root, p).Replace('\\', '/'), File.ReadAllText);

    [Fact]
    public void No_core_file_outside_BuiltIn_names_a_domain()
    {
        Assert.Empty(Violations(ApiSources()));
    }

    [Fact]
    public void Each_allowed_file_still_names_a_domain_and_says_which_follow_up_removes_it()
    {
        var sources = ApiSources();
        foreach (var (file, plugin) in AllowList)
        {
            Assert.True(sources.ContainsKey(file), $"{file} is allow-listed but does not exist");
            // A stale entry fails: a file that no longer names a domain leaves the list.
            Assert.Matches(NamesADomain, StripComments(sources[file]));
            Assert.Contains($"// names a domain until the {plugin} follow-up moves it (introduce-plugins 8.1)", sources[file]);
        }
        Assert.DoesNotContain("Program.cs", AllowList.Keys);
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

        Assert.Equal(["Agent/Planted.cs: \"billing\"", "Agent/Constant.cs: BuiltInDomains.Portfolio"], Violations(planted));
    }
}

/// <summary>
/// The static facades' contract (introduce-plugins design §6): inside a scope they read the catalogue put there — a
/// request's or a turn's frozen view — and outside any scope every built-in domain.
/// </summary>
public sealed class DomainCatalogueFacadeTests
{
    [Fact]
    public void A_static_reader_sees_the_catalogue_in_scope_and_all_built_ins_outside_it()
    {
        using (Maf.Lab.Api.Agent.DomainCatalogue.Use(Maf.Lab.Api.Agent.DomainCatalogue.Of([StandInDomains.BillingDomain])))
        {
            Assert.Equal(["billing"], Maf.Lab.Api.Agent.Domains.All);
            Assert.Null(Maf.Lab.Api.Agent.Domains.OfTool("search_portfolio_documents"));
            Assert.Equal("", Maf.Lab.Api.Agent.ChatTurnRunner.ClearedFocusNote);
        }

        Assert.Equal(["portfolio"], Maf.Lab.Api.Agent.Domains.All);
        Assert.Equal("portfolio", Maf.Lab.Api.Agent.Domains.OfTool("search_portfolio_documents"));
        Assert.NotEqual("", Maf.Lab.Api.Agent.ChatTurnRunner.ClearedFocusNote);
    }

    [Fact]
    public void A_frozen_view_keeps_its_domains()
    {
        var all = Maf.Lab.Api.Agent.DomainCatalogue.AllBuiltIn;

        var frozen = all.Freeze();

        Assert.Equal(all.Ids, frozen.Ids);
        Assert.Same(frozen, frozen.Freeze());
    }
}
