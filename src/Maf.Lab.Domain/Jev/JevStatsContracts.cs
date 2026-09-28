using Maf.Lab.Domain.Intent;

namespace Maf.Lab.Domain.Jev;

/// <summary>The configuration the cross-cutting numbers are judged against, as the running services have it.</summary>
/// <param name="Model">The Jev model the intent classifier reports.</param>
/// <param name="GuardEnabled">Whether prompt/content screening is on.</param>
/// <param name="PromptBlockAt">The default probability at which a prompt question blocks.</param>
/// <param name="ContentWithholdAt">The probability at which a tool result or a reviewer's words are withheld.</param>
/// <param name="CrossTenantAt">The per-question override for the cross-tenant prompt question.</param>
/// <param name="RelevanceFloor">The relevance floor a search's top score is held to, read from the traces.</param>
public sealed record JevStatsSettings(
    string Model, bool GuardEnabled, double PromptBlockAt, double ContentWithholdAt, double CrossTenantAt, double? RelevanceFloor);

/// <summary>One request-bearing site's requests, how many were unavailable, and its latency.</summary>
public sealed record JevSiteSummary(string Site, int Requests, int Unavailable, double? P50Ms, double? P90Ms);

/// <summary>One time bucket of total Jev requests and how many were unavailable, summed across every site.</summary>
public sealed record JevAvailabilityBucket(DateTimeOffset Start, int Requests, int Unavailable);

/// <summary>
/// The cross-cutting view: how many Jev requests a firm's turns made in the window, how many were unavailable, and how
/// that moved over time — so a degraded Jev shows across every site at once, not only in one section.
/// </summary>
/// <param name="Requests">Total Jev requests: one per jev intent event, per content-screening item, per judged search, per
/// answer check that sent one.</param>
/// <param name="Unavailable">Of those, how many timed out, were rejected or errored.</param>
/// <param name="Turns">Classified turns (the requests-per-turn denominator).</param>
public sealed record JevOverview(
    JevStatsSettings Settings,
    int Requests,
    int Unavailable,
    int Turns,
    double? RequestsPerTurn,
    IReadOnlyList<JevSiteSummary> Sites,
    IReadOnlyList<JevAvailabilityBucket> Timeline);

/// <summary>One guard check kind and its decisions.</summary>
public sealed record GuardrailCheckCount(string Check, int Total, int Pass, int Blocked, int Withheld, int Unscreened);

/// <param name="Question">The guard question that decided a block or a withholding.</param>
/// <param name="Decision">blocked or withheld.</param>
public sealed record GuardrailQuestionCount(string Question, string Decision, int Count);

/// <summary>One time bucket of screenings by outcome.</summary>
public sealed record GuardrailTimelineBucket(DateTimeOffset Start, int Screened, int Blocked, int Withheld, int Unscreened);

/// <summary>
/// What the guardrail did over the window: prompt, tool-result and reviewer screenings and their decisions, the
/// question that tripped each block or withholding, and the latency of the screenings that make their own Jev request.
/// </summary>
public sealed record GuardrailStats(
    IReadOnlyList<GuardrailCheckCount> Checks,
    IReadOnlyList<GuardrailQuestionCount> TrippedBy,
    int Screened,
    int Blocked,
    int Withheld,
    int Unscreened,
    IntentLatency Latency,
    IReadOnlyList<GuardrailTimelineBucket> Timeline);

/// <summary>One bin of the top-relevance histogram, [From, To), split by whether the search was silenced.</summary>
public sealed record RelevanceMaxBin(double From, double To, int Kept, int Gated);

/// <summary>One time bucket of judged searches, how many the gate silenced and how many were left ungated.</summary>
public sealed record RelevanceTimelineBucket(DateTimeOffset Start, int Searches, int Gated, int Unavailable);

/// <summary>
/// The passage-relevance gate and Jev reranker: how many searches Jev judged, how many the gate silenced, how many the
/// Jev reranker ordered, how many were left ungated because Jev was unavailable, the top-score distribution against the
/// floor, and the judge latency.
/// </summary>
public sealed record RelevanceStats(
    double? Floor,
    int Searches,
    int Gated,
    int Reranked,
    int Unavailable,
    IReadOnlyList<RelevanceMaxBin> MaxHistogram,
    IntentLatency Latency,
    IReadOnlyList<RelevanceTimelineBucket> Timeline,
    IReadOnlyList<RelevanceDomainCount>? ByDomain = null);

/// <summary>Judged searches of one domain's documentation — each domain's search asks Jev on its own server.</summary>
public sealed record RelevanceDomainCount(string Domain, int Searches, int Gated, int Unavailable);

/// <summary>
/// Where Jev placed the firm's questions among the domains (add-portfolio-domain): how many turns carried a domain
/// verdict, how many were in billing, portfolio, both (crossing) or neither, how many turns actually crossed from one
/// domain's tools to the other's, and — of the turns that called any tool — how many touched exactly the domains Jev
/// predicted.
/// </summary>
public sealed record DomainStats(int Judged, int Billing, int Portfolio, int Both, int None, int Crossed, int WithCalls, int Agreed);

/// <summary>
/// Jev's check of the final answer (add-jev-answer-check): how many answers it was asked about, how many got a verdict,
/// how many of those fell below the relevance and the grounding floor — each against the floor recorded with its check —
/// how many were left unchecked (of which Jev was unavailable for how many), and the check's latency.
/// </summary>
/// <param name="Answers">Answer checks recorded in the window, checked or not.</param>
/// <param name="Checked">Answers that got a verdict (pass, not relevant, not grounded).</param>
/// <param name="NotRelevant">Checked answers below the relevance floor.</param>
/// <param name="NotGrounded">Checked answers below the grounding floor; an answer can be below both.</param>
/// <param name="Unchecked">Answers with no verdict: disabled, no key, or Jev unavailable.</param>
/// <param name="Unavailable">Of the unchecked, those whose request timed out, was rejected or failed.</param>
public sealed record AnswerCheckStats(
    int Answers,
    int Checked,
    int Pass,
    int NotRelevant,
    int NotGrounded,
    int Unchecked,
    int Unavailable,
    double? RelevantFloor,
    double? GroundedFloor,
    IntentLatency Latency);

/// <param name="Tool">The read tool a data turn was routed to.</param>
public sealed record RoutingToolCount(string Tool, int Count);

/// <param name="Reason">Why a data turn was not routed (the router's reason, its number kept where it had one).</param>
public sealed record RoutingReasonCount(string Reason, int Count);

/// <summary>
/// Data-turn tool routing: how many data turns there were, how many were routed and to which tool, why a data turn was
/// not routed, the median model calls a data turn made routed against unrouted, and the routed turns' latency.
/// </summary>
/// <param name="Enabled">Whether any data turn in the window carried a routing answer.</param>
/// <param name="ModelCallsRoutedMedian">Median model calls of routed data turns; null when there were none.</param>
/// <param name="ModelCallsUnroutedMedian">Median model calls of unrouted data turns; null when there were none.</param>
public sealed record RoutingStats(
    bool Enabled,
    int DataTurns,
    int Routed,
    IReadOnlyList<RoutingToolCount> Tools,
    IReadOnlyList<RoutingReasonCount> NotRoutedReasons,
    double? ModelCallsRoutedMedian,
    double? ModelCallsUnroutedMedian,
    IntentLatency Latency);

/// <summary>
/// Aggregates of every Jev call site in one firm's chat turns over one window: the intent aggregate unchanged, the
/// guardrail, relevance, routing and answer-check sections, and a cross-cutting overview. Jev calls made outside a chat turn — an A2A
/// partner's question and the tool results on its path — are in no turn trace and are not counted. Numbers only: no
/// question, answer, passage or identifier of a turn, conversation or user.
/// </summary>
public sealed record JevStatsReport(
    string Window,
    DateTimeOffset From,
    DateTimeOffset To,
    int BucketMinutes,
    JevOverview Overview,
    IntentStatsReport Intent,
    GuardrailStats Guardrail,
    RelevanceStats Relevance,
    RoutingStats Routing,
    DomainStats? Domains = null,
    AnswerCheckStats? AnswerCheck = null);
