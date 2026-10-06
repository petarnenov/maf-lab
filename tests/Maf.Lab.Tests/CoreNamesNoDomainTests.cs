using System.Text.RegularExpressions;

namespace Maf.Lab.Tests;

/// <summary>
/// The core names no domain (introduce-plugins 5g, task 4.6): outside <c>src/Maf.Lab.Api/BuiltIn/</c>, the transitional
/// home of the shipped domains, no api source file names billing, portfolio or codebase — except the consumers whose
/// contract a follow-up plugin moves, each listed here and marked in its own file. The scan reads source text, not IL:
/// the ids are constants, which the compiler inlines.
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

    private static readonly Regex NamesADomain =
        new(@"BuiltInDomains\.(Billing|Portfolio|Codebase)\b|""(billing|portfolio|codebase)""", RegexOptions.Compiled);

    private static string ApiRoot => Path.Combine(CorpusLoaderTests.RepoRoot(), "src", "Maf.Lab.Api");

    /// <summary>The source with its comments removed (strings kept, so a literal in a string still counts).</summary>
    internal static string StripComments(string source) =>
        Regex.Replace(source, @"(""(?:\\.|[^""\\])*"")|//[^\n]*|/\*.*?\*/", m => m.Groups[1].Success ? m.Value : "", RegexOptions.Singleline);

    internal static IReadOnlyList<string> Violations(IReadOnlyDictionary<string, string> files) =>
    [
        .. files.Where(f => !f.Key.StartsWith("BuiltIn/", StringComparison.Ordinal) && !AllowList.ContainsKey(f.Key))
            .SelectMany(f => NamesADomain.Matches(StripComments(f.Value)).Select(m => $"{f.Key}: {m.Value}")),
    ];

    private static IReadOnlyDictionary<string, string> ApiSources() =>
        Directory.EnumerateFiles(ApiRoot, "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                && !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .ToDictionary(p => Path.GetRelativePath(ApiRoot, p).Replace('\\', '/'), File.ReadAllText);

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
        var billing = Maf.Lab.Api.Agent.DomainCatalogue.AllBuiltIn.Get(Maf.Lab.Api.BuiltIn.BuiltInDomains.Billing)!;

        using (Maf.Lab.Api.Agent.DomainCatalogue.Use(Maf.Lab.Api.Agent.DomainCatalogue.Of([billing])))
        {
            Assert.Equal(["billing"], Maf.Lab.Api.Agent.Domains.All);
            Assert.Null(Maf.Lab.Api.Agent.Domains.OfTool("search_portfolio_documents"));
            Assert.Equal("", Maf.Lab.Api.Agent.ChatTurnRunner.ClearedFocusNote);
        }

        Assert.Equal(["billing", "portfolio"], Maf.Lab.Api.Agent.Domains.All);
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
