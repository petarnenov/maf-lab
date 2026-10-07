using A2A;
using Maf.Lab.A2A;

namespace Maf.Lab.ComplianceAgent;

/// <summary>
/// Who the reviewer says it is. One skill, described so that a caller can tell what it will and will not do — and
/// so that nobody mistakes a lab stand-in for a compliance desk.
/// </summary>
public static class ComplianceAgentCard
{
    /// <summary>The scope a caller needs to ask for a review.</summary>
    public const string ReviewScope = "a2a.compliance.review";

    public const string SkillId = "review_fee_adjustment";

    public static AgentCardDescriptor Descriptor { get; } = new(
        Name: "maf-lab compliance reviewer",
        Description:
            "Reviews a proposed fee adjustment and returns a verdict with a reason. It is a separate system with "
            + "its own identity: it does not know who asked, only which system asked.",
        PublicSkills:
        [
            new AgentSkill
            {
                Id = SkillId,
                Name = "Review a fee adjustment",
                Description =
                    "Use to have a proposed fee adjustment reviewed before it is applied: send the firm, the "
                    + "account, the amount and the reason, and receive approved or refused with a reason. The "
                    + "review takes tens of seconds and may ask for the advisor's justification first. Do not use "
                    + "it to apply an adjustment, to ask about billing runs or documentation, or as an authority "
                    + "on anything real — the verdict is simulated and binds nobody.",
                Tags = ["compliance", "review", "long-running", "simulated"],
                Examples = ["Review a fee adjustment of 250.00 for account ACC-1042 at firm-a: overcharged in Q2."],
            },
        ],
        PrivateSkills: [],
        Scopes: new Dictionary<string, string>
        {
            [ReviewScope] = "Ask for a fee adjustment to be reviewed.",
        })
    {
        DocumentationPath = "/docs/http-api.md",
    };
}
