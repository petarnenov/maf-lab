using Maf.Lab.TestGen;

namespace Maf.Lab.Api.Coverage;

/// <summary>An estimate of what a run will use: tokens, and dollars at the model's configured prices.</summary>
public sealed record CostEstimate(long InputTokens, long OutputTokens, double CostUsd);

/// <summary>What a run on one file is expected to cost before it starts, at <see cref="AttemptEstimate"/>'s rates.</summary>
public static class CostEstimator
{
    public static CostEstimate Estimate(long targetFileBytes, int attempts, AgentModelOption model)
    {
        var (input, output) = AttemptEstimate.PerAttempt(targetFileBytes);
        return new CostEstimate(input * attempts, output * attempts, Cost(input * attempts, output * attempts, model));
    }

    public static double Cost(long inputTokens, long outputTokens, AgentModelOption model) =>
        AttemptEstimate.Cost(inputTokens, outputTokens, model.InputPerMTok, model.OutputPerMTok);
}
