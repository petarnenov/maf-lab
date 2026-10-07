using Maf.Lab.Api.Agent;
using Maf.Lab.Api.Agent.Decisions;
using Maf.Lab.Plugins.Abstractions;

namespace Maf.Lab.Tests;

/// <summary>
/// Jev's text names no domain (extract-billing): the contexts name the subjects of the domains in use and the intent
/// options carry each domain's clauses, from the descriptors. Pinned for the three shapes a deployment takes — billing,
/// portfolio and code; portfolio alone; no domain — so a change to the composition is a deliberate one, re-measured.
/// </summary>
public class JevDomainTextTests
{
    private static readonly DomainDescriptor Code =
        new DomainTable { Id = "codebase", Order = 30, Subject = "this lab's own source code" }.ToDescriptor();

    private static DomainCatalogue All => DomainCatalogue.Of([.. StandInDomains.WithBilling.All, Code]);

    private static DomainCatalogue PortfolioOnly => StandInDomains.PortfolioOnly;

    private static DomainCatalogue None => DomainCatalogue.Of([]);

    [Fact]
    public void Every_domain_names_its_subject_in_order()
    {
        using (DomainCatalogue.Use(All))
        {
            Assert.Equal("fee billing, investment portfolios and this lab's own source code", Subjects.Current);
        }
        using (DomainCatalogue.Use(PortfolioOnly))
        {
            Assert.Equal("investment portfolios", Subjects.Current);
        }
        using (DomainCatalogue.Use(None))
        {
            Assert.Null(Subjects.Current);
        }
    }

    [Fact]
    public void The_contexts_name_the_subjects_and_still_read_without_any()
    {
        Assert.Equal(
            "`user_question` is a message a user typed to an AI assistant that answers questions about fee billing and investment "
            + "portfolios for the user's own organisation, from that organisation's documents and data. Ordinary users ask about "
            + "procedures, policies and their own data, and may ask the assistant to propose a change, which they then confirm.",
            GuardQuestions.PromptContext("fee billing and investment portfolios"));
        Assert.Equal(
            "`user_question` is a message a user typed to an AI assistant that answers questions for the user's own organisation, "
            + "from that organisation's documents and data. Ordinary users ask about procedures, policies and their own data, and "
            + "may ask the assistant to propose a change, which they then confirm.",
            GuardQuestions.PromptContext(null));
        Assert.StartsWith(
            "`user_question` is what a user asked an AI assistant that answers questions about investment portfolios for the user's own organisation. ",
            DecisionAnswerCheck.Context("investment portfolios"));
        Assert.StartsWith(
            "`user_question` is what a user asked an AI assistant that answers questions for the user's own organisation. ",
            DecisionAnswerCheck.Context(null));
    }

    [Fact]
    public void The_intent_options_carry_each_domains_clauses()
    {
        using var domains = DomainCatalogue.Use(All);

        Assert.Equal(new Dictionary<string, string>
        {
            ["procedural"] = "Asks what the documentation says: how or why something is done, a procedure, policy, definition or "
                + "explanation, or what a named fee schedule, failure code or rule means or charges",
            ["mixed"] = "Asks how or why about one specific billing run identified by its run number, e.g. why run 4417 failed",
            ["data"] = "Asks for the current state of billing runs: a status, which runs failed, a list of runs; or of an account's "
                + "portfolio: what an account holds, its allocation, drift or AUM",
            ["chitchat"] = "A greeting, thanks, closing or small talk",
            ["other"] = "Anything else, including requests to change data",
        }, DecisionIntentClassifier.Criteria);
    }

    [Fact]
    public void Without_a_mixed_clause_the_options_are_a_closed_set_without_mixed()
    {
        using (DomainCatalogue.Use(PortfolioOnly))
        {
            var criteria = DecisionIntentClassifier.Criteria;
            Assert.Equal(["procedural", "data", "chitchat", "other"], criteria.Keys);
            Assert.Equal(DecisionIntentClassifier.ProceduralStem, criteria["procedural"]);
            Assert.Equal("Asks for the current state of an account's portfolio: what an account holds, its allocation, drift or AUM",
                criteria["data"]);
        }
        using (DomainCatalogue.Use(None))
        {
            var criteria = DecisionIntentClassifier.Criteria;
            Assert.Equal(["procedural", "data", "chitchat", "other"], criteria.Keys);
            Assert.Equal("Asks for the current state of the organisation's data: a status or a list", criteria["data"]);
        }
    }

    [Fact]
    public void No_text_jev_reads_names_a_domain_when_none_is_in_use()
    {
        using var domains = DomainCatalogue.Use(None);
        var text = string.Join("\n",
            System.Text.Json.JsonSerializer.Serialize(GuardQuestions.Prompt),
            System.Text.Json.JsonSerializer.Serialize(GuardQuestions.Content),
            System.Text.Json.JsonSerializer.Serialize(DecisionAnswerCheck.Questions),
            string.Join("\n", DecisionIntentClassifier.Criteria.Values));

        Assert.DoesNotMatch(@"(?i)billing|\bfees?\b|adjustment|portfolio", text);
    }
}
