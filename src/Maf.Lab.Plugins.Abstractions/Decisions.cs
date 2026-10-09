using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Maf.Lab.Plugins.Abstractions;

// Typed, closed-set decisions with a confidence (introduce-provider-plugins 5t): the port the core asks every routing,
// screening, relevance and answer-check question through. docs/rules/jev-usage.md is its contract, binding on any
// engine: closed answer spaces only, one atomic question each, all questions over one state in one request, nothing code
// can compute, confidence gated by risk, never a security boundary. The core never names an engine: the installed
// `decision-engine` provider plugin implements it. Our own abstraction (DECISIONS): MEAI's IChatClient with structured
// output has no calibrated probabilities, and MEAI.Evaluation's IEvaluator grades rather than routes.

/// <summary>
/// One closed question. <paramref name="Instructions"/> is a sentence, or named fields beside it (the question names
/// them in backticks), serialised as its runtime type; the state it is asked over is never inside it.
/// </summary>
public abstract record DecisionQuestion(object Instructions);

/// <summary>One of the given options; each option's text says what separates it from the others.</summary>
public sealed record ChoiceQuestion(object Instructions, IReadOnlyDictionary<string, string> Criteria) : DecisionQuestion(Instructions);

/// <summary>A yes/no question; the answer is the probability of yes. Criteria, when given, say what counts as each.</summary>
public sealed record NoulQuestion(object Instructions, NoulCriteria? Criteria = null) : DecisionQuestion(Instructions);

/// <summary>What counts as yes and what as no.</summary>
public sealed record NoulCriteria(string Yes, string No);

/// <summary>An ordered level, lowest first; only thresholded or ranked, never read back as a number between levels.</summary>
public sealed record ScoreQuestion(object Instructions, IReadOnlyList<string> Levels) : DecisionQuestion(Instructions);

/// <summary>What a request was charged for. Numbers only.</summary>
public sealed record DecisionUsage(int? InputTokens, int? OutputTokens);

/// <summary>
/// What one request came to: the answers, or why there are none (<paramref name="Failure"/>), the engine and version that
/// answered (or would have), what it was charged, and how long it took. <paramref name="Skipped"/> is true when nothing
/// was sent because the engine is known to be failing (<see cref="DecisionFailures.CircuitOpen"/>): the caller treats it
/// as any other missing answer and records no request.
/// </summary>
public sealed record DecisionOutcome(
    IReadOnlyDictionary<string, DecisionAnswer>? Answers,
    string Engine,
    DecisionUsage? Usage,
    string? Failure,
    double DurationMs,
    bool Skipped = false);

/// <summary>The failure reasons a caller may act on. They are trace data, so their text never changes.</summary>
public static class DecisionFailures
{
    /// <summary>Nothing was sent: the engine has been failing and is being left alone for a while.</summary>
    public const string CircuitOpen = "circuit open";

    /// <summary>Nothing was sent: the engine has no credential.</summary>
    public const string NoKey = "no key";
}

/// <summary>
/// The decision engine: closed, typed questions over one state in, one typed answer each with its confidence out, in
/// one request. It never throws for the engine's own failures — a timeout, an error status, an unreachable engine or a
/// missing credential comes back as <see cref="DecisionOutcome.Failure"/> — and stops when <c>ct</c> fires.
/// </summary>
public interface IDecisionEngine
{
    /// <summary>The engine and version that answers, as traces record it.</summary>
    string Engine { get; }

    /// <summary>False when the engine cannot be asked at all (no credential): callers skip the request and say why.</summary>
    bool IsConfigured { get; }

    Task<DecisionOutcome> DecideAsync(object state, IReadOnlyDictionary<string, DecisionQuestion> questions, TimeSpan budget,
        CancellationToken ct);
}

/// <summary>
/// A provider plugin (kind <c>provider</c>): it registers an implementation of a core port — a decision engine, a chat
/// model, embeddings — in every process that hosts providers, and nothing else. <see cref="Provides"/> is its manifest's
/// <c>provides</c>.
/// </summary>
public interface IContributesProvider
{
    string Provides { get; }

    void ConfigureProvider(IServiceCollection services, IConfiguration configuration);
}

/// <summary>The kinds of provider the core needs (the manifest's <c>provides</c>).</summary>
public static class ProviderKinds
{
    public const string DecisionEngine = "decision-engine";
    public const string ChatModel = "chat-model";
    public const string Embeddings = "embeddings";
}
