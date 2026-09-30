using Maf.Lab.TestGen;

namespace Maf.Lab.Api.Coverage;

/// <summary>An estimate of what a run will use: tokens, and dollars at the model's configured prices.</summary>
public sealed record CostEstimate(long InputTokens, long OutputTokens, double CostUsd);

/// <summary>
/// The model-independent parts of <see cref="AttemptEstimate"/> for one file, which the picker prices for the model,
/// attempts and tool rounds entered: per attempt, input = fixed + perRound × min(rounds, typical), output as given.
/// </summary>
public sealed record EstimateParts(long FixedInputTokens, int InputTokensPerRound, int TypicalRounds, int OutputTokensPerAttempt);

/// <summary>What a run on one file is expected to cost before it starts, at <see cref="AttemptEstimate"/>'s rates.</summary>
public static class CostEstimator
{
    public static EstimateParts Parts(long targetFileBytes) => new(AttemptEstimate.FixedInput(targetFileBytes),
        AttemptEstimate.InputTokensPerRound, AttemptEstimate.TypicalRounds, AttemptEstimate.OutputTokensPerAttempt);

    public static CostEstimate Estimate(long targetFileBytes, int attempts, int toolRounds, AgentModelOption model)
    {
        var (input, output) = AttemptEstimate.PerAttempt(targetFileBytes, toolRounds);
        return new CostEstimate(input * attempts, output * attempts, Cost(input * attempts, output * attempts, model));
    }

    public static double Cost(long inputTokens, long outputTokens, AgentModelOption model) =>
        AttemptEstimate.Cost(inputTokens, outputTokens, model.InputPerMTok, model.OutputPerMTok);
}
