using A2A;
using Maf.Lab.A2A;

namespace Maf.Lab.Api.A2A;

/// <summary>
/// Who this agent says it is. Each skill states what it is for *and* what it is not for, because a caller choosing
/// a skill from a one-line description is how the wrong agent gets asked the wrong question.
/// </summary>
public static class BillingAgentCard
{
    public const string PrivateSkillId = "start_billing_run";

    public static AgentCardDescriptor Descriptor { get; } = new(
        Name: "maf-lab billing assistant",
        Description:
            "Answers questions about a TAMP billing domain from firm-scoped documentation and billing run data. "
            + "Every answer is grounded in retrieved documents; the agent never invents billing figures.",
        PublicSkills:
        [
            new AgentSkill
            {
                Id = "search_billing_documentation",
                Name = "Search billing documentation",
                Description =
                    "Use when you need the procedure, policy or definition behind a billing question: what to do "
                    + "when a fee schedule is missing, how a tiered fee is calculated, what a failure code means. "
                    + "Do not use for the current state of a billing run, for anything outside the firms you are "
                    + "entitled to, or to change anything — this skill only reads documentation.",
                Tags = ["documentation", "procedures", "read-only"],
                Examples = ["What is the procedure when a fee schedule is missing?"],
            },
            new AgentSkill
            {
                Id = "billing_run_status",
                Name = "Billing run status",
                Description =
                    "Use to ask the current state of a billing run you are entitled to see, by run id. Do not use "
                    + "to list runs of other firms, to ask why a run failed in procedural terms (use the "
                    + "documentation skill), or to start or change a run.",
                Tags = ["billing", "status", "read-only"],
                Examples = ["What is the status of run 4417?"],
            },
        ],
        PrivateSkills:
        [
            new AgentSkill
            {
                Id = PrivateSkillId,
                Name = "Start a billing run",
                Description =
                    "Starts a billing run for a firm you are entitled to and reports progress until it completes. "
                    + "Simulated in this lab: it walks the real task lifecycle over seeded data and does not bill "
                    + "anyone. Do not use it expecting money to move. Requires the period; the task will ask for it "
                    + "if you leave it out.",
                Tags = ["billing", "long-running", "simulated"],
                Examples = ["Start the billing run for firm-a for 2026-06."],
            },
        ],
        Scopes: new Dictionary<string, string>
        {
            [A2AScopes.BillingRead] = "Ask questions about firms you are entitled to.",
        });
}
