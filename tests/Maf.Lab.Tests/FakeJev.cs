using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Maf.Lab.Tests;

/// <summary>
/// Stands in for TypeSafe's Jev at the HTTP boundary: answers <c>POST /v1/systemone</c> in the documented shape —
/// the intent classifier's Choice and Nouls, the relevance judge's per-passage Nouls and the answer check's two Nouls — and records what it was sent
/// (the authorization header and the body) so tests can check both. It answers with keyword rules — English and the
/// Bulgarian words the tests use — because a test double has to be deterministic; the real model has no rules.
/// </summary>
public sealed partial class FakeJev : HttpMessageHandler
{
    /// <summary>A test value, not a secret: what the fake expects as the bearer token.</summary>
    public const string TestKey = "test-jev-key-0f6c2d";

    public ConcurrentQueue<(string? Authorization, string Body)> Requests { get; } = new();

    /// <summary>The confidence every answer reports.</summary>
    public double Confidence { get; set; } = 1.0;

    /// <summary>When set, every request is answered with this status and no body.</summary>
    public HttpStatusCode? Status { get; set; }

    /// <summary>When set, replaces the keyword rules.</summary>
    public Func<string, string>? Choose { get; set; }

    /// <summary>When set, the fake waits this long and ignores cancellation, like a transport that never answers.</summary>
    public TimeSpan? Hang { get; set; }

    public string Model { get; set; } = "jev-1.13.0";

    /// <summary>What every Noul question is answered with — for the classifier, the probability the question is in the domain.</summary>
    public double? InDomain { get; set; } = 1.0;

    /// <summary>
    /// The portfolio domain question's answer (<c>in_portfolio</c>), given the user's question. Default:
    /// <see cref="PortfolioWords"/> — high for questions about holdings, drift, rebalancing or AUM, zero otherwise — so a
    /// billing test is not a crossing test by accident, and a test's own <see cref="InDomain"/> stays the highest domain.
    /// </summary>
    public Func<string, double>? Portfolio { get; set; }

    /// <summary>
    /// The codebase domain question's answer (<c>in_codebase</c>). Default: <see cref="CodebaseWords"/> — high for questions
    /// about the lab's code, zero otherwise — so no earlier test becomes a codebase crossing by accident.
    /// </summary>
    public Func<string, double>? Codebase { get; set; }

    /// <summary>Questions naming code, a class, a method, a file or an MCP server count as the codebase's.</summary>
    public static double CodebaseWords(string question) =>
        System.Text.RegularExpressions.Regex.IsMatch(question,
            @"\b(codebase|source code|in the code|the code base|class|method|mcp server)\b|в кода|клас|метод",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase) ? 0.9 : 0.0;

    /// <summary>
    /// What every screening Noul (<c>guard_*</c>) is answered with, given the screened text and the question id. Null —
    /// the default — answers 0, so a test that is not about the content guard never trips it.
    /// </summary>
    public Func<string, string, double>? Guard { get; set; }

    /// <summary>
    /// The relevance judge's answer for one passage, given the query and the passage text. Default: a shared word of
    /// four letters or more is relevant (0.9), anything else is not (0.02).
    /// </summary>
    public Func<string, string, double>? Relevance { get; set; }

    /// <summary>A routing question's answer: the probability that the question needs the tool. Default: <see cref="Tool"/>.</summary>
    public Func<string, string, double>? Tools { get; set; }

    /// <summary>
    /// The answer check's answer, given the question id (<c>answer_relevant</c> or <c>answer_grounded</c>), the user's
    /// question and the answer. Null — the default — answers 0.95 to both, a pass, so a test that is not about the check
    /// never flags a turn.
    /// </summary>
    public Func<string, string, string, double>? AnswerCheck { get; set; }

    /// <summary>
    /// The eval grade's answer (adopt-meai-evaluation), given the question id (<c>claim_3</c>, <c>supported_3</c>,
    /// <c>stated_0</c>, <c>contradicts_0</c>, <c>on_subject_1</c>, <c>answer_relevant</c>) and the request's state. Null
    /// answers the next rule; a grade request is recognised by its <c>answer_sentences</c>.
    /// </summary>
    public Func<string, JsonNode, double?>? Grade { get; set; }

    /// <summary>The run status a question asks about. Default: <see cref="RunStatus"/>.</summary>
    public Func<string, string>? RunStatusOf { get; set; }

    /// <summary>The question ids and types each request carried, in order.</summary>
    public ConcurrentQueue<IReadOnlyDictionary<string, string>> Questions { get; } = new();

    /// <summary>The Content-Length the last request declared before its body was read; null means it was streamed.</summary>
    public long? LastContentLength { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastContentLength = request.Content?.Headers.ContentLength;
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Enqueue((request.Headers.Authorization?.ToString(), body));
        if (Hang is { } hang)
        {
            await Task.Delay(hang, CancellationToken.None);
        }
        if (request.Headers.Authorization is not { Scheme: "Bearer", Parameter: TestKey })
        {
            return new HttpResponseMessage(HttpStatusCode.Unauthorized);
        }
        if (Status is { } status)
        {
            return new HttpResponseMessage(status);
        }

        var root = JsonNode.Parse(body)!;
        var asked = root["questions"]!.AsObject().ToDictionary(q => q.Key, q => q.Value!["type"]!.GetValue<string>());
        Questions.Enqueue(asked);
        var answers = new Dictionary<string, object>();
        if (root["state"]!["passages"] is JsonArray passages)
        {
            // The relevance judge: one Noul per passage, "p0"…, each about passages[i].
            var query = root["state"]!["query"]!.GetValue<string>();
            foreach (var id in asked.Keys)
            {
                var text = passages[int.Parse(id[1..])]!["text"]!.GetValue<string>();
                answers[id] = new { type = "noul", noul = (Relevance ?? Overlap)(query, text) };
            }
            return Answer(answers);
        }
        if (root["state"]!["text"] is not null)
        {
            // The start-up warm-up: fixed text, one Noul, no user content (jev-client-reuse).
            return Answer(asked.Keys.ToDictionary(id => id, object (_) => new { type = "noul", noul = 0.0 }));
        }
        if (root["state"]!["answer_sentences"] is not null)
        {
            // The eval grade: every question a Noul; by default every claim supported, every point stated, nothing
            // contradicted, every source on the subject.
            foreach (var id in asked.Keys)
            {
                var p = Grade?.Invoke(id, root["state"]!) ?? (id.StartsWith("contradicts_", StringComparison.Ordinal) ? 0.02 : 0.95);
                if (!double.IsNaN(p))
                {
                    // NaN leaves the question unanswered, as a partial response would.
                    answers[id] = new { type = "noul", noul = p };
                }
            }
            return Answer(answers);
        }
        // A classification or a prompt screening carries the user's question; a content screening, the text it judges.
        var question = (root["state"]!["user_question"] ?? root["state"]!["untrusted_text"])!.GetValue<string>();
        var choice = (Choose ?? Classify)(question);
        foreach (var (id, type) in asked)
        {
            if (id == "run_status")
            {
                var runStatus = (RunStatusOf ?? RunStatus)(question);
                answers[id] = new
                {
                    type = "choice",
                    choice = runStatus,
                    confidence = 1.0,
                    probabilities = new[] { "pending", "running", "completed", "failed", "none" }.ToDictionary(o => o, o => o == runStatus ? 1.0 : 0.0),
                };
            }
            else if (id.StartsWith("tool_", StringComparison.Ordinal))
            {
                answers[id] = new { type = "noul", noul = (Tools ?? Tool)(id["tool_".Length..], question) };
            }
            else if (type == "choice")
            {
                answers[id] = new
                {
                    type = "choice",
                    choice,
                    confidence = Confidence,
                    probabilities = new[] { "procedural", "mixed", "data", "chitchat", "other" }
                        .ToDictionary(o => o, o => o == choice ? Confidence : Math.Round((1 - Confidence) / 4, 4)),
                };
            }
            else if (id == "in_portfolio")
            {
                // A fake told to leave the domain unanswered leaves every domain question unanswered.
                if ((Portfolio ?? (InDomain is null ? null : PortfolioWords)) is { } portfolio)
                {
                    answers[id] = new { type = "noul", noul = portfolio(question) };
                }
            }
            else if (id == "in_codebase")
            {
                if ((Codebase ?? (InDomain is null ? null : CodebaseWords)) is { } codebase)
                {
                    answers[id] = new { type = "noul", noul = codebase(question) };
                }
            }
            else if (id.StartsWith("answer_", StringComparison.Ordinal))
            {
                var answer = root["state"]!["answer"]?.GetValue<string>() ?? "";
                answers[id] = new { type = "noul", noul = AnswerCheck?.Invoke(id, question, answer) ?? 0.95 };
            }
            else if (id.StartsWith("guard_", StringComparison.Ordinal))
            {
                answers[id] = new { type = "noul", noul = Guard?.Invoke(question, id) ?? 0.0 };
            }
            else if (InDomain is { } p)
            {
                answers[id] = new { type = "noul", noul = p };
            }
        }
        return Answer(answers);
    }

    private HttpResponseMessage Answer(Dictionary<string, object> answers)
    {
        var answer = new
        {
            model = Model,
            answers,
            usage = new { input_tokens = 400, output_tokens = 50 },
        };
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(answer), Encoding.UTF8, "application/json"),
        };
    }

    /// <summary>Keyword answers to the routing questions, shaped like the planning probe's: the named tool high, the rest low.</summary>
    public static double Tool(string tool, string question)
    {
        var q = question.ToLowerInvariant();
        return tool switch
        {
            "get_billing_run_status" => RunReference().IsMatch(q) ? 0.93 : 0.1,
            "search_billing_runs" => !RunReference().IsMatch(q) && (DataWords().IsMatch(q) || q.Contains("runs")) ? 0.92 : 0.2,
            "propose_fee_adjustment" => WriteWords().IsMatch(q) ? 0.8 : 0.02,
            "get_household_portfolio" => Regex.IsMatch(q, @"\b(holdings?|hold|allocation|drift)\b") ? 0.9 : 0.05,
            "get_aum_history" => Regex.IsMatch(q, @"\baum\b.*\b(history|quarter|quarters|year)\b|quarter-end") ? 0.9 : 0.05,
            "list_my_accounts" => Regex.IsMatch(q, @"\b(my|which) (accounts|households)\b|\b(accounts|households) (do|can) i\b|(?<!\p{L})(акаунт|сметк)\p{L}*") ? 0.9 : 0.05,
            _ => 0.0,
        };
    }

    public static string RunStatus(string question)
    {
        var q = question.ToLowerInvariant();
        return q.Contains("fail") ? "failed" : q.Contains("pending") ? "pending" : q.Contains("running") ? "running"
            : q.Contains("complete") || q.Contains("finished") ? "completed" : "none";
    }

    /// <summary>Keyword answer to the portfolio domain question.</summary>
    public static double PortfolioWords(string question) =>
        PortfolioVocabulary().IsMatch(question.ToLowerInvariant()) ? 0.9 : 0.0;

    // "Which accounts do I have?" is the portfolio's too: its tools list them, and since add-codebase-domain a turn loads
    // only the tools of its domains.
    [GeneratedRegex(@"\b(holdings?|hold|drift\w*|rebalanc\w*|allocation|model portfolio|aum|tolerance|portfolio)\b|\b(my|which) (accounts|households)\b|\b(accounts|households) (do|can) i\b|(?<!\p{L})(портфейл\p{L}*|ребаланс\p{L}*|акаунт\p{L}*|сметк\p{L}*)(?!\p{L})")]
    private static partial Regex PortfolioVocabulary();

    [GeneratedRegex(@"\b(credit|reduce|increase|adjust|refund)\b.*\ba-\d+")]
    private static partial Regex WriteWords();

    /// <summary>Relevant when the passage shares a word of four letters or more with the query.</summary>
    public static double Overlap(string query, string passage)
    {
        var words = Words().Matches(query.ToLowerInvariant()).Select(m => m.Value).Where(w => w.Length >= 4).ToHashSet();
        return Words().Matches(passage.ToLowerInvariant()).Any(m => words.Contains(m.Value)) ? 0.9 : 0.02;
    }

    [GeneratedRegex(@"\p{L}+")]
    private static partial Regex Words();

    /// <summary>Keyword classification into Jev's option names.</summary>
    public static string Classify(string question)
    {
        var q = question.Trim().ToLowerInvariant();
        var words = q.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        var procedural = Procedural().IsMatch(q);
        var run = RunReference().IsMatch(q);
        if (words <= 8 && ChitChat().IsMatch(q) && !procedural)
        {
            return "chitchat";
        }
        if (procedural)
        {
            return run ? "mixed" : "procedural";
        }
        return run || DataWords().IsMatch(q) ? "data" : "other";
    }

    [GeneratedRegex(@"^(thanks|thank you|thx|ok|okay|great|perfect|bye|goodbye|hello|hi|hey|cheers|got it|здравей|здрасти|благодаря|мерси|чао)(?!\p{L})")]
    private static partial Regex ChitChat();

    [GeneratedRegex(@"\b(how|why|procedure|process|steps?|explain|what is|what's|what are|what does|define|definition|meaning|policy|what should|what to do|when should|guide)\b|(?<!\p{L})(как|защо|процедура|процедурата|обясни|политика)(?!\p{L})")]
    private static partial Regex Procedural();

    [GeneratedRegex(@"\brun\s*#?\s*\d{3,}\b|#\d{3,}\b|(?<!\p{L})рън\s*#?\s*\d{3,}")]
    private static partial Regex RunReference();

    [GeneratedRegex(@"\b(status|state of|which runs|list (the )?runs|show (me )?(the )?runs|failed runs|pending runs|latest runs?|runs? (that )?failed)\b|(?<!\p{L})(статус|списък|колко)(?!\p{L})")]
    private static partial Regex DataWords();
}
