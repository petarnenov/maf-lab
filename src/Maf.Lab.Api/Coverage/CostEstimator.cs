namespace Maf.Lab.Api.Coverage;

/// <summary>An estimate of what a run will use: tokens, and dollars at the model's configured prices.</summary>
public sealed record CostEstimate(long InputTokens, long OutputTokens, double CostUsd);

/// <summary>
/// What a run on one file is expected to cost, before it starts. Per attempt: the target file and about as much
/// again of related code (tests, callers), plus the system prompt and tool definitions, re-read as the tool loop
/// goes (×1.6); and a few thousand tokens of test code written back. A rough guide, labelled as one.
/// </summary>
public static class CostEstimator
{
    public const int FixedTokensPerAttempt = 6_000;
    public const double RereadFactor = 1.6;
    public const int OutputTokensPerAttempt = 4_000;

    /// <summary>Tokens in a piece of source: about four bytes each.</summary>
    public static long TokensOf(long bytes) => (bytes + 3) / 4;

    public static CostEstimate Estimate(long targetFileBytes, int attempts, AgentModelOption model)
    {
        var fileTokens = TokensOf(targetFileBytes);
        var inputPerAttempt = (long)Math.Ceiling((fileTokens * 2 + FixedTokensPerAttempt) * RereadFactor);
        var input = inputPerAttempt * attempts;
        var output = (long)OutputTokensPerAttempt * attempts;
        return new CostEstimate(input, output, Cost(input, output, model));
    }

    public static double Cost(long inputTokens, long outputTokens, AgentModelOption model) =>
        Math.Round(inputTokens / 1_000_000.0 * model.InputPerMTok + outputTokens / 1_000_000.0 * model.OutputPerMTok, 4);
}
