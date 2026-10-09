using System.Text.Json;

namespace Maf.Lab.Eval.Datasets;

public sealed record SelectionCase(string Id, string Question, IReadOnlyList<string> ExpectedTools, string Category, string TenantId, string? Source);
/// <param name="Language">Language of the query; null means the corpus language, so old datasets keep working.</param>
/// <param name="OffDomain">
/// A question this corpus cannot answer, for which the right retrieval is none at all. Such a case declares no
/// relevant chunks; the marker is what stops an unlabelled row being read as one.
/// </param>
/// <param name="Domain">Whose collection the case is searched in: billing (the default, when a row names none) or portfolio.</param>
public sealed record RetrievalCase(string Id, string Query, IReadOnlyList<string> RelevantChunkIds, string TenantId, string? Source,
    string? Language = null, bool OffDomain = false, string Domain = "billing");
/// <summary>
/// A portfolio question whose answer is judged for how it presents the turn's data cards (add-system-prompt-v3).
/// <paramref name="RebalanceNeeded"/> is the verdict the answer must state, or null when the question does not ask it.
/// </summary>
/// <paramref name="RowsRequested"/> marks a question that explicitly asks about every row (each class, each account): a
/// list is then a legitimate answer and is not counted as restating the card.
public sealed record PresentationCase(string Id, string Question, string TenantId, bool Carded, bool? RebalanceNeeded, string Language,
    bool RowsRequested = false);

/// <param name="ReferencePoints">The reference answer split by hand into atomic statements, each graded stated or not (adopt-meai-evaluation).</param>
public sealed record GenerationCase(string Id, string Question, string ReferenceAnswer, IReadOnlyList<string> ExpectedDocIds, string TenantId, string? Source,
    IReadOnlyList<string> ReferencePoints);
/// <param name="Question">What the advisor asks, which must make the assistant propose the adjustment.</param>
/// <param name="AccountId">The account the proposal must be about.</param>
/// <param name="Amount">The adjustment the proposal must make.</param>
public sealed record ConfirmationCase(string Id, string Question, string AccountId, decimal Amount, string TenantId, string? Source);

/// <param name="Forces">Whether the classifier should force search_documents for this question.</param>
/// <param name="Split">"design" when the case informed the classifier's thresholds, "holdout" when it did not.</param>
/// <param name="Expected">billing, portfolio, both (the question crosses the boundary) or none.</param>
public sealed record DomainCase(string Id, string Question, string Expected, string Language, string Split);

public sealed record IntentCase(string Id, string Question, bool Forces, string Category, string Language, string Split);

/// <param name="Side">"prompt" (a user's or partner's message), "tool" (a tool-result item) or "agent" (another agent's words).</param>
/// <param name="Malicious">Whether the text tries to instruct or steer the assistant; the guard should flag exactly these.</param>
/// <param name="Split">"design" when the case informed the guard's questions and thresholds, "holdout" when it did not.</param>
/// <param name="Tool">
/// For a tool-side case, the tool whose result item the text is: it is screened as that tool's item — a
/// <c>search_codebase</c> case with the codebase battery and its record-only questions. Null: a result of no search.
/// </param>
public sealed record GuardrailCase(string Id, string Side, string Text, bool Malicious, string Category, string Language, string Split,
    string? Tool = null);

/// <summary>One thing the model read, as an answer-check case replays it: a search item as the tool returned it, or a whole result.</summary>
/// <param name="Tool">The tool that returned it.</param>
/// <param name="Item">A search result item (<c>path</c>/<c>startLine</c>/<c>endLine</c>/<c>symbol</c>/<c>snippet</c>, or <c>docId</c>/<c>sectionPath</c>/<c>snippet</c>).</param>
/// <param name="Text">A whole result, as the model got it; used when <paramref name="Item"/> is null.</param>
public sealed record AnswerCheckSource(string Tool, JsonElement? Item, string? Text);

/// <summary>
/// A labelled answer for Jev's answer check alone (fit-answer-checks-to-code-questions): what was asked, answered and
/// read, and what a reviewer found — whether a claim is unsupported, whether it is off the question.
/// </summary>
/// <param name="PreviousSources">What the model read for <paramref name="PreviousQuestion"/>, replayed as that turn's data envelopes.</param>
/// <param name="Domain">billing, portfolio or codebase.</param>
public sealed record AnswerCheckCase(string Id, string Question, string PreviousQuestion, string Answer, IReadOnlyList<AnswerCheckSource> Sources,
    IReadOnlyList<AnswerCheckSource> PreviousSources, bool Unsupported, bool OffTopic, string Domain, string Language, string Split, string? Source);

/// <summary>
/// An answer graded against its reference points without the agent (adopt-meai-evaluation): for each point, whether a
/// reviewer found it stated and whether contradicted.
/// </summary>
public sealed record GenerationJudgeCase(string Id, string Question, string Answer, IReadOnlyList<string> ReferencePoints,
    IReadOnlyList<bool> Stated, IReadOnlyList<bool> Contradicted, string Domain, string Language, string Split, string? Source);

/// <summary>
/// One sentence of a recorded answer as a reviewer labelled it: whether it makes a claim, whether its sources support
/// that claim (null when it makes none, or when <paramref name="Ambiguous"/>), and whether it is a citation of a source
/// — a claim the system prompt requires to be exact (adopt-meai-evaluation).
/// </summary>
public sealed record SentenceLabel(string Text, bool Claim, bool? Supported, bool Citation, bool Ambiguous);

/// <summary>A recorded answer with the sources it was given and a label per sentence, in the order code cuts them.</summary>
public sealed record GenerationSentenceCase(string Id, string Question, string Answer, IReadOnlyList<AnswerCheckSource> Sources,
    IReadOnlyList<SentenceLabel> Sentences, string Domain, string Language, string Split, string? Source);

/// <summary>One thing a graph-depth case's answer needs, and the number of calls at which the graph first reaches it.</summary>
/// <param name="Item">A method symbol (<c>Type.Member</c>) for a trace; a test file path for an impact.</param>
/// <param name="Hops">Calls from the case's symbol or file to the item; null when it lies beyond the deepest variant.</param>
public sealed record GraphDepthNeed(string Item, int? Hops);

/// <summary>
/// A labelled code-graph question for the graph-depth comparison (add-graph-depth-eval): a trace of a symbol in one
/// direction, or the impact of a file, with what an answer needs. Labels come from reading the code, not from a trace.
/// </summary>
/// <param name="Kind">"trace" or "impact".</param>
/// <param name="Reference">The labelled facts the rubric judge holds the answer to.</param>
public sealed record GraphDepthCase(string Id, string Kind, string? Symbol, string? Direction, string? Path, string Question,
    string Language, IReadOnlyList<GraphDepthNeed> Needed, string Reference, string TenantId)
{
    /// <summary>The calls the case needs to be answered in full: its deepest reachable item.</summary>
    public int RequiredDepth => Needed.Max(n => n.Hops ?? 0);
}

/// <summary>
/// A labelled codebase question for the code-route Choice alone (route-structural-code-questions): what it needs, and
/// whether it names the one symbol or file a routed call would take.
/// </summary>
/// <param name="Expected">callers, callees, impact, text or none.</param>
/// <param name="HasArgument">The question names exactly one <c>Type.Member</c> symbol (callers, callees) or one C# path (impact).</param>
public sealed record CodeRouteCase(string Id, string Question, string Expected, bool HasArgument, string Language, string Split);

public sealed record InjectionCase(string Id, string Question, IReadOnlyList<string> ForbiddenStrings, IReadOnlyList<string> ForbiddenTenantIds, string TenantId, string? Source);

/// <summary>Loads and validates the JSONL datasets. Invalid rows fail loudly with file and line.</summary>
public static class DatasetLoader
{
    public static readonly string[] Files =
        ["selection.jsonl", "retrieval.jsonl", "generation.jsonl", "injection.jsonl", "confirmation.jsonl", "intent.jsonl", "guardrail.jsonl",
            "domain.jsonl", "presentation.jsonl", "answer-check.jsonl", "graph-depth.jsonl", "code-route.jsonl"];
    public static readonly string[] Tools =
        ["search_documents", "get_billing_run_status", "search_billing_runs", Maf.Lab.Domain.Billing.FeeAdjustmentTool.Name,
            Maf.Lab.Domain.Portfolio.PortfolioTools.Search, Maf.Lab.Domain.Portfolio.PortfolioTools.GetPortfolio,
            Maf.Lab.Domain.Portfolio.PortfolioTools.AumHistory, Maf.Lab.Domain.Portfolio.PortfolioTools.ListAccounts,
            Maf.Lab.Domain.Code.CodeTools.Search, Maf.Lab.Domain.Graph.GraphTools.TraceBilling, Maf.Lab.Domain.Graph.GraphTools.TraceCodeSymbol,
            Maf.Lab.Domain.Graph.GraphTools.ChangeImpact];
    public static readonly string[] SelectionCategories =
        ["obvious-docs", "obvious-data", "boundary", "negative", "feedback", "portfolio", "cross-domain", "codebase", "graph"];

    public static IReadOnlyList<SelectionCase> Selection(string root) => Load(root, "selection.jsonl", (e, where) =>
    {
        var tools = Strings(e, "expectedTools", where, allowEmpty: true);
        foreach (var t in tools.Where(t => !Tools.Contains(t)))
        {
            throw new InvalidDataException($"{where}: unknown tool '{t}'.");
        }
        var category = Str(e, "category", where);
        if (!SelectionCategories.Contains(category))
        {
            throw new InvalidDataException($"{where}: unknown category '{category}'.");
        }
        return new SelectionCase(Str(e, "id", where), Str(e, "question", where), tools, category, Tenant(e, where), Opt(e, "source"));
    });

    /// <summary>A write to propose, and what the sentence put to a person must therefore state.</summary>
    public static IReadOnlyList<ConfirmationCase> Confirmation(string root) => Load(root, "confirmation.jsonl", (e, where) =>
        new ConfirmationCase(
            Str(e, "id", where),
            Str(e, "question", where),
            Str(e, "accountId", where),
            Decimal(e, "amount", where),
            Tenant(e, where),
            Opt(e, "source")));

    public static IReadOnlyList<RetrievalCase> Retrieval(string root) => Load(root, "retrieval.jsonl", (e, where) =>
    {
        // Only a row that says it is off-domain may have no relevant chunks. Without the marker an empty list is
        // indistinguishable from a row somebody forgot to label, and would quietly score as a perfect silence.
        var offDomain = Bool(e, "offDomain");
        var relevant = Strings(e, "relevantChunkIds", where, allowEmpty: offDomain);
        if (offDomain && relevant.Count > 0)
        {
            throw new InvalidDataException($"{where}: an off-domain case must not declare relevant chunks.");
        }
        var domain = Opt(e, "domain") ?? "billing";
        if (domain is not ("billing" or "portfolio"))
        {
            throw new InvalidDataException($"{where}: domain must be billing or portfolio.");
        }
        return new RetrievalCase(Str(e, "id", where), Str(e, "query", where), relevant, Tenant(e, where), Opt(e, "source"),
            Opt(e, "language"), offDomain, domain);
    });

    public static IReadOnlyList<PresentationCase> Presentation(string root) => Load(root, "presentation.jsonl", (e, where) =>
    {
        var language = Str(e, "language", where);
        if (language is not ("en" or "bg"))
        {
            throw new InvalidDataException($"{where}: language must be en or bg.");
        }
        bool? needed = e.TryGetProperty("rebalanceNeeded", out var n) && n.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? n.GetBoolean() : null;
        return new PresentationCase(Str(e, "id", where), Str(e, "question", where), Tenant(e, where), Bool(e, "carded"), needed, language,
            Bool(e, "rowsRequested"));
    });

    public static IReadOnlyList<GenerationCase> Generation(string root) => Load(root, "generation.jsonl", (e, where) =>
        new GenerationCase(Str(e, "id", where), Str(e, "question", where), Str(e, "referenceAnswer", where),
            Strings(e, "expectedDocIds", where, allowEmpty: true), Tenant(e, where), Opt(e, "source"),
            Strings(e, "referencePoints", where, allowEmpty: false)));

    public static readonly string[] IntentCategories =
        ["in-proc", "in-mixed", "in-data", "in-write", "chitchat", "off-proc", "off-meta", "off-trap", "steer"];
    public static readonly string[] IntentLanguages = ["en", "bg", "bg-latn"];

    /// <summary>Questions for the intent classifier alone, each saying whether retrieval should be forced.</summary>
    public static IReadOnlyList<IntentCase> Intent(string root) => Load(root, "intent.jsonl", (e, where) =>
    {
        if (!e.TryGetProperty("forces", out var f) || f.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new InvalidDataException($"{where}: 'forces' must be true or false.");
        }
        var category = Str(e, "category", where);
        var language = Str(e, "language", where);
        var split = Str(e, "split", where);
        if (!IntentCategories.Contains(category) || !IntentLanguages.Contains(language) || split is not ("design" or "holdout"))
        {
            throw new InvalidDataException($"{where}: unknown category, language or split.");
        }
        return new IntentCase(Str(e, "id", where), Str(e, "question", where), f.GetBoolean(), category, language, split);
    });

    /// <summary>
    /// One domain, <c>none</c>, <c>both</c> (billing and portfolio, as the dataset has always said it), or several domains
    /// joined with "+" in the trace's order, e.g. <c>billing+codebase</c> (add-codebase-domain).
    /// </summary>
    public static readonly string[] DomainExpectations = ["billing", "portfolio", "codebase", "both", "none"];

    public static bool IsDomainExpectation(string expected) =>
        DomainExpectations.Contains(expected)
        || expected.Split('+') is { Length: > 1 } parts && parts.All(p => p is "billing" or "portfolio" or "codebase") && parts.Distinct().Count() == parts.Length;

    /// <summary>Questions for Jev's domain verdict alone, each labelled with the domains it belongs to.</summary>
    public static IReadOnlyList<DomainCase> Domain(string root) => Load(root, "domain.jsonl", (e, where) =>
    {
        var expected = Str(e, "expected", where);
        var language = Str(e, "language", where);
        var split = Str(e, "split", where);
        if (!IsDomainExpectation(expected) || !IntentLanguages.Contains(language) || split is not ("design" or "holdout"))
        {
            throw new InvalidDataException($"{where}: unknown expected domain, language or split.");
        }
        return new DomainCase(Str(e, "id", where), Str(e, "question", where), expected, language, split);
    });

    public static readonly string[] GuardrailSides = ["prompt", "tool", "agent"];

    /// <summary>Texts for the content guard alone, each labelled malicious or benign.</summary>
    public static IReadOnlyList<GuardrailCase> Guardrail(string root) => Load(root, "guardrail.jsonl", (e, where) =>
    {
        if (!e.TryGetProperty("malicious", out var m) || m.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new InvalidDataException($"{where}: 'malicious' must be true or false.");
        }
        var side = Str(e, "side", where);
        var language = Str(e, "language", where);
        var split = Str(e, "split", where);
        if (!GuardrailSides.Contains(side) || !IntentLanguages.Contains(language) || split is not ("design" or "holdout"))
        {
            throw new InvalidDataException($"{where}: unknown side, language or split.");
        }
        var tool = Opt(e, "tool");
        if (tool is not null && (side != "tool" || !Tools.Contains(tool)))
        {
            throw new InvalidDataException($"{where}: 'tool' must name a known tool, on a tool-side case.");
        }
        return new GuardrailCase(Str(e, "id", where), side, Str(e, "text", where), m.GetBoolean(), Str(e, "category", where), language, split, tool);
    });

    public static readonly string[] AnswerCheckDomains = ["billing", "portfolio", "codebase"];

    /// <summary>
    /// Labelled answers for the answer check alone. Every field is required — an unlabelled row would quietly count as a
    /// supported, on-topic answer — and each source must name a known tool and carry a search item or a whole text.
    /// </summary>
    public static IReadOnlyList<AnswerCheckCase> AnswerCheck(string root) => Load(root, "answer-check.jsonl", (e, where) =>
    {
        foreach (var flag in new[] { "unsupported", "offTopic" })
        {
            if (!e.TryGetProperty(flag, out var f) || f.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                throw new InvalidDataException($"{where}: '{flag}' must be true or false.");
            }
        }
        if (!e.TryGetProperty("previousQuestion", out var pq) || pq.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException($"{where}: 'previousQuestion' must be a string (empty on a first question).");
        }
        var domain = Str(e, "domain", where);
        var language = Str(e, "language", where);
        var split = Str(e, "split", where);
        if (!AnswerCheckDomains.Contains(domain) || !IntentLanguages.Contains(language) || split is not ("design" or "holdout"))
        {
            throw new InvalidDataException($"{where}: unknown domain, language or split.");
        }
        return new AnswerCheckCase(Str(e, "id", where), Str(e, "question", where), pq.GetString()!, Str(e, "answer", where),
            Sources(e, "sources", where), Sources(e, "previousSources", where), e.GetProperty("unsupported").GetBoolean(),
            e.GetProperty("offTopic").GetBoolean(), domain, language, split, Opt(e, "source"));
    });

    public static IReadOnlyList<GenerationJudgeCase> GenerationJudge(string root) => Load(root, "generation-judge.jsonl", (e, where) =>
    {
        var points = Strings(e, "referencePoints", where);
        var stated = Bools(e, "stated", where);
        var contradicted = Bools(e, "contradicted", where);
        if (stated.Count != points.Count || contradicted.Count != points.Count)
        {
            throw new InvalidDataException($"{where}: 'stated' and 'contradicted' must have one label per reference point.");
        }
        var domain = Str(e, "domain", where);
        var language = Str(e, "language", where);
        var split = Str(e, "split", where);
        if (!AnswerCheckDomains.Contains(domain) || !IntentLanguages.Contains(language) || split is not ("design" or "holdout"))
        {
            throw new InvalidDataException($"{where}: unknown domain, language or split.");
        }
        return new GenerationJudgeCase(Str(e, "id", where), Str(e, "question", where), Str(e, "answer", where), points, stated,
            contradicted, domain, language, split, Opt(e, "source"));
    });

    /// <summary>
    /// Sentence labels are only as good as their alignment: the labelled texts must be exactly the sentences code cuts
    /// the answer into, or a label would grade a neighbour.
    /// </summary>
    public static IReadOnlyList<GenerationSentenceCase> GenerationSentences(string root) => Load(root, "generation-sentences.jsonl", (e, where) =>
    {
        if (!e.TryGetProperty("sentences", out var list) || list.ValueKind != JsonValueKind.Array || list.GetArrayLength() == 0)
        {
            throw new InvalidDataException($"{where}: 'sentences' must be a non-empty array.");
        }
        var labels = list.EnumerateArray().Select(x =>
        {
            var claim = x.TryGetProperty("claim", out var c) && c.ValueKind == JsonValueKind.True;
            bool? supported = x.TryGetProperty("supported", out var sp) && sp.ValueKind is JsonValueKind.True or JsonValueKind.False ? sp.GetBoolean() : null;
            var ambiguous = x.TryGetProperty("ambiguous", out var a) && a.ValueKind == JsonValueKind.True;
            if (claim && supported is null && !ambiguous)
            {
                throw new InvalidDataException($"{where}: a claim needs 'supported' unless it is marked ambiguous.");
            }
            return new SentenceLabel(Str(x, "text", where), claim, supported, x.TryGetProperty("citation", out var ci) && ci.ValueKind == JsonValueKind.True, ambiguous);
        }).ToList();
        var answer = Str(e, "answer", where);
        var cut = Judging.AnswerSentences.Split(Api.Agent.Decisions.AnswerText.Normalise(answer));
        if (!cut.SequenceEqual(labels.Select(l => l.Text)))
        {
            throw new InvalidDataException($"{where}: the labelled sentences are not the {cut.Count} sentences code cuts the answer into.");
        }
        var domain = Str(e, "domain", where);
        var language = Str(e, "language", where);
        var split = Str(e, "split", where);
        if (!AnswerCheckDomains.Contains(domain) || !IntentLanguages.Contains(language) || split is not ("design" or "holdout"))
        {
            throw new InvalidDataException($"{where}: unknown domain, language or split.");
        }
        return new GenerationSentenceCase(Str(e, "id", where), Str(e, "question", where), answer, Sources(e, "sources", where), labels,
            domain, language, split, Opt(e, "source"));
    });

    private static IReadOnlyList<bool> Bools(JsonElement e, string name, string where)
    {
        if (!e.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException($"{where}: '{name}' must be an array.");
        }
        return v.EnumerateArray().Select(x => x.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? x.GetBoolean() : throw new InvalidDataException($"{where}: '{name}' must contain true or false.")).ToList();
    }

    private static IReadOnlyList<AnswerCheckSource> Sources(JsonElement e, string name, string where)
    {
        if (!e.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException($"{where}: '{name}' must be an array.");
        }
        return [.. v.EnumerateArray().Select(s =>
        {
            if (s.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException($"{where}: every entry of '{name}' must be an object.");
            }
            var tool = Str(s, "tool", where);
            if (!Tools.Contains(tool))
            {
                throw new InvalidDataException($"{where}: unknown tool '{tool}' in '{name}'.");
            }
            var item = s.TryGetProperty("item", out var i) && i.ValueKind == JsonValueKind.Object ? i.Clone() : (JsonElement?)null;
            var text = Opt(s, "text");
            if (item is null == string.IsNullOrWhiteSpace(text))
            {
                throw new InvalidDataException($"{where}: an entry of '{name}' carries either an 'item' or a 'text'.");
            }
            // A withheld item is its stub: no text, by design (injection-defense).
            if (item is { } it && !(it.TryGetProperty("withheld", out var w) && w.ValueKind == JsonValueKind.True)
                && (!it.TryGetProperty("snippet", out var sn) || sn.ValueKind != JsonValueKind.String))
            {
                throw new InvalidDataException($"{where}: a search item in '{name}' must carry its 'snippet'.");
            }
            return new AnswerCheckSource(tool, item, text);
        })];
    }

    /// <summary>
    /// Code-graph cases for the graph-depth comparison. A case lists at least one needed item — without one, every depth
    /// would score a perfect recall — and each item's hops are 1 to <see cref="Maf.Lab.Retrieval.Graph.CallTrace.MaxDepth"/>
    /// or null for an item no variant reaches, so a missing caller is reported rather than dropped.
    /// </summary>
    public static IReadOnlyList<GraphDepthCase> GraphDepth(string root) => Load(root, "graph-depth.jsonl", (e, where) =>
    {
        var kind = Str(e, "kind", where);
        var language = Str(e, "language", where);
        if (kind is not ("trace" or "impact") || !IntentLanguages.Contains(language))
        {
            throw new InvalidDataException($"{where}: kind must be trace or impact, and language one of {string.Join(", ", IntentLanguages)}.");
        }
        string? symbol = null, direction = null, path = null;
        if (kind == "trace")
        {
            symbol = Str(e, "symbol", where);
            direction = Str(e, "direction", where);
            if (direction is not ("callers" or "callees"))
            {
                throw new InvalidDataException($"{where}: direction must be callers or callees.");
            }
        }
        else
        {
            path = Str(e, "path", where);
        }
        if (!e.TryGetProperty("needed", out var needed) || needed.ValueKind != JsonValueKind.Array || needed.GetArrayLength() == 0)
        {
            throw new InvalidDataException($"{where}: 'needed' must list at least one item an answer needs.");
        }
        var items = needed.EnumerateArray().Select(n =>
        {
            if (n.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException($"{where}: every entry of 'needed' must be an object.");
            }
            int? hops = n.TryGetProperty("hops", out var h) && h.ValueKind == JsonValueKind.Number ? h.GetInt32()
                : n.TryGetProperty("hops", out h) && h.ValueKind == JsonValueKind.Null ? null
                : throw new InvalidDataException($"{where}: every needed item has 'hops', a number or null (beyond reach).");
            if (hops is < 1 or > Maf.Lab.Retrieval.Graph.CallTrace.MaxDepth)
            {
                throw new InvalidDataException($"{where}: 'hops' must be 1 to {Maf.Lab.Retrieval.Graph.CallTrace.MaxDepth}, or null.");
            }
            return new GraphDepthNeed(Str(n, "item", where), hops);
        }).ToList();
        if (items.All(i => i.Hops is null))
        {
            throw new InvalidDataException($"{where}: at least one needed item must be reachable, or the case measures nothing.");
        }
        return new GraphDepthCase(Str(e, "id", where), kind, symbol, direction, path, Str(e, "question", where), language, items,
            Str(e, "reference", where), Tenant(e, where));
    });

    public static readonly string[] CodeRouteOptions = ["callers", "callees", "impact", "text", "none"];

    /// <summary>Questions for the code-route Choice alone; every field is required, so an unlabelled row cannot pass as text.</summary>
    public static IReadOnlyList<CodeRouteCase> CodeRoute(string root) => Load(root, "code-route.jsonl", (e, where) =>
    {
        var expected = Str(e, "expected", where);
        var language = Str(e, "language", where);
        var split = Str(e, "split", where);
        if (!CodeRouteOptions.Contains(expected) || !IntentLanguages.Contains(language) || split is not ("design" or "holdout"))
        {
            throw new InvalidDataException($"{where}: unknown expected option, language or split.");
        }
        if (!e.TryGetProperty("hasArgument", out var h) || h.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new InvalidDataException($"{where}: 'hasArgument' must be true or false.");
        }
        return new CodeRouteCase(Str(e, "id", where), Str(e, "question", where), expected, h.GetBoolean(), language, split);
    });

    public static IReadOnlyList<InjectionCase> Injection(string root) => Load(root, "injection.jsonl", (e, where) =>
        new InjectionCase(Str(e, "id", where), Str(e, "question", where), Strings(e, "forbiddenStrings", where),
            Strings(e, "forbiddenTenantIds", where, allowEmpty: true), Tenant(e, where), Opt(e, "source")));

    private static IReadOnlyList<T> Load<T>(string root, string file, Func<JsonElement, string, T> parse)
    {
        var path = Path.Combine(root, file);
        var rows = new List<T>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var lineNo = 0;
        foreach (var line in File.ReadLines(path))
        {
            lineNo++;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }
            var where = $"{file}:{lineNo}";
            JsonElement element;
            try
            {
                element = JsonDocument.Parse(line).RootElement;
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException($"{where}: invalid JSON ({ex.Message}).");
            }
            var id = Str(element, "id", where);
            if (!ids.Add(id))
            {
                throw new InvalidDataException($"{where}: duplicate id '{id}'.");
            }
            rows.Add(parse(element, where));
        }
        return rows;
    }

    private static string Str(JsonElement e, string name, string where) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString())
            ? v.GetString()!
            : throw new InvalidDataException($"{where}: '{name}' must be a non-empty string.");

    private static decimal Decimal(JsonElement e, string name, string where) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetDecimal()
            : throw new InvalidDataException($"{where}: '{name}' must be a number.");

    private static string? Opt(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool Bool(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    /// <summary>The case's tenant: <c>tenantId</c>, or its pre-rename name <c>firmId</c> for one release.</summary>
    private static string Tenant(JsonElement e, string where)
    {
        var tenant = Opt(e, "tenantId") ?? Opt(e, "firmId") ?? "firm-a";
        return Maf.Lab.Domain.Tenancy.TenantId.TryParse(tenant, out var t) && !t.IsShared ? tenant : throw new InvalidDataException($"{where}: tenantId '{tenant}' is not a tenant.");
    }

    private static IReadOnlyList<string> Strings(JsonElement e, string name, string where, bool allowEmpty = false)
    {
        if (!e.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException($"{where}: '{name}' must be an array.");
        }
        var list = v.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? x.GetString()! : throw new InvalidDataException($"{where}: '{name}' must contain strings.")).ToList();
        if (!allowEmpty && list.Count == 0)
        {
            throw new InvalidDataException($"{where}: '{name}' must not be empty.");
        }
        return list;
    }
}
