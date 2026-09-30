namespace Maf.Lab.TestGen;

/// <summary>
/// What one attempt on a file is expected to use, before any has run. Per attempt: the target file and about as much
/// again of related code (tests, callers), plus the instructions and tool definitions, re-read as the tool loop goes
/// (×1.6); and a few thousand tokens of test code written back. The api prices it for the picker; the agent uses it to
/// stop before an attempt that would cross the budget. A rough guide, labelled as one.
/// </summary>
public static class AttemptEstimate
{
    public const int FixedTokensPerAttempt = 6_000;
    public const double RereadFactor = 1.6;
    public const int OutputTokensPerAttempt = 4_000;

    /// <summary>Tokens in a piece of source: about four bytes each.</summary>
    public static long TokensOf(long bytes) => (bytes + 3) / 4;

    public static (long Input, long Output) PerAttempt(long targetFileBytes) =>
        ((long)Math.Ceiling((TokensOf(targetFileBytes) * 2 + FixedTokensPerAttempt) * RereadFactor), OutputTokensPerAttempt);

    public static double Cost(long inputTokens, long outputTokens, double inputPerMTok, double outputPerMTok) =>
        Math.Round(inputTokens / 1_000_000.0 * inputPerMTok + outputTokens / 1_000_000.0 * outputPerMTok, 4);
}
