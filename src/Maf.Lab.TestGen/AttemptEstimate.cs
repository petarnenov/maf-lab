namespace Maf.Lab.TestGen;

/// <summary>
/// What one attempt on a file is expected to use, before any has run. An attempt is a tool loop: every round re-sends
/// the growing conversation, so input follows the rounds taken far more than the file's size. Fitted on 13 measured
/// attempts (DECISIONS.md §61): input ≈ 43k + 4k per round, with 14–22 rounds used and never near the cap, and about
/// 6k tokens of test code written back. The api serves the parts to the picker, which prices them for the limits
/// entered; the agent uses them to stop before an attempt that would cross the budget. A rough guide, labelled as one.
/// </summary>
public static class AttemptEstimate
{
    public const int FixedInputTokens = 43_000;
    public const int InputTokensPerRound = 4_000;
    /// <summary>Rounds an attempt typically takes: a lower cap saves rounds, a higher one is not used.</summary>
    public const int TypicalRounds = 20;
    /// <summary>The file and what it pulls in, re-read each round; the data cannot fit it, so it is kept conservative.</summary>
    public const int FileTokenFactor = 4;
    public const int OutputTokensPerAttempt = 6_000;

    /// <summary>Tokens in a piece of source: about four bytes each.</summary>
    public static long TokensOf(long bytes) => (bytes + 3) / 4;

    /// <summary>The input an attempt uses before its rounds: the instructions, tools and the file.</summary>
    public static long FixedInput(long targetFileBytes) => FixedInputTokens + FileTokenFactor * TokensOf(targetFileBytes);

    public static (long Input, long Output) PerAttempt(long targetFileBytes, int toolRounds) =>
        (FixedInput(targetFileBytes) + (long)InputTokensPerRound * Math.Min(toolRounds, TypicalRounds), OutputTokensPerAttempt);

    public static double Cost(long inputTokens, long outputTokens, double inputPerMTok, double outputPerMTok) =>
        Math.Round(inputTokens / 1_000_000.0 * inputPerMTok + outputTokens / 1_000_000.0 * outputPerMTok, 4);
}
