namespace Maf.Lab.Api.Agent;

/// <summary>
/// What a turn is doing, as the protocol's steps (agui-protocol-only): a client that never reads the trace still sees
/// the turn screen the question, call each tool and check the answer.
/// </summary>
public static class Steps
{
    public const string Screening = "screening the question";
    public const string AnswerCheck = "checking the answer";

    public static string Tool(string name) => $"tool: {name}";
}
