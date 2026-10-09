using A2A;
using Maf.Lab.A2A;

namespace Maf.Lab.TestAgent;

/// <summary>Who the test agent says it is: one skill, with what it is for and what it is not for.</summary>
public static class TestAgentCard
{
    public const string RunScope = "a2a.testgen.run";
    public const string SkillId = "generate-tests";

    public static AgentCardDescriptor Descriptor { get; } = new(
        Name: "maf-lab test agent",
        Description:
            "Writes tests for one source file of the maf-lab repository until its line coverage reaches a target, in "
            + $"at most {Maf.Lab.TestGen.TestGenRequest.AttemptLimit} attempts, and returns a report and a diff of the test changes.",
        PublicSkills:
        [
            new AgentSkill
            {
                Id = SkillId,
                Name = "Generate tests for one file",
                Description =
                    "For: raising the line coverage of one file in this repository to a target, by writing or "
                    + "changing tests only. Send a testgen.request/v1 data part. Long-running: the task reports each "
                    + "attempt and ends with a testgen-report artifact. Not for: changing production code, other "
                    + "repositories, or answering questions.",
                Tags = ["tests", "coverage", "long-running"],
                Examples = ["Raise src/Maf.Lab.Api/Coverage/CoverageTree.cs to 85% line coverage at commit 73fdb9a…"],
            },
        ],
        PrivateSkills: [],
        Scopes: new Dictionary<string, string> { [RunScope] = "Start and follow a test-generation run." })
    {
        DocumentationPath = "/docs/http-api.md",
    };
}
