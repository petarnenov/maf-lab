using System.Text.Json;

namespace Maf.Lab.Eval.Datasets;

public sealed record SelectionCase(string Id, string Question, IReadOnlyList<string> ExpectedTools, string Category, string FirmId, string? Source);
/// <param name="Language">Language of the query; null means the corpus language, so old datasets keep working.</param>
/// <param name="OffDomain">
/// A question this corpus cannot answer, for which the right retrieval is none at all. Such a case declares no
/// relevant chunks; the marker is what stops an unlabelled row being read as one.
/// </param>
/// <param name="Domain">Whose collection the case is searched in: billing (the default, when a row names none) or portfolio.</param>
public sealed record RetrievalCase(string Id, string Query, IReadOnlyList<string> RelevantChunkIds, string FirmId, string? Source,
    string? Language = null, bool OffDomain = false, string Domain = "billing");
/// <summary>
/// A portfolio question whose answer is judged for how it presents the turn's data cards (add-system-prompt-v3).
/// <paramref name="RebalanceNeeded"/> is the verdict the answer must state, or null when the question does not ask it.
/// </summary>
/// <paramref name="RowsRequested"/> marks a question that explicitly asks about every row (each class, each account): a
/// list is then a legitimate answer and is not counted as restating the card.
public sealed record PresentationCase(string Id, string Question, string FirmId, bool Carded, bool? RebalanceNeeded, string Language,
    bool RowsRequested = false);

public sealed record GenerationCase(string Id, string Question, string ReferenceAnswer, IReadOnlyList<string> ExpectedDocIds, string FirmId, string? Source);
/// <param name="Question">What the advisor asks, which must make the assistant propose the adjustment.</param>
/// <param name="AccountId">The account the proposal must be about.</param>
/// <param name="Amount">The adjustment the proposal must make.</param>
public sealed record ConfirmationCase(string Id, string Question, string AccountId, decimal Amount, string FirmId, string? Source);

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

public sealed record InjectionCase(string Id, string Question, IReadOnlyList<string> ForbiddenStrings, IReadOnlyList<string> ForbiddenTenantIds, string FirmId, string? Source);

/// <summary>Loads and validates the JSONL datasets. Invalid rows fail loudly with file and line.</summary>
public static class DatasetLoader
{
    public static readonly string[] Files =
        ["selection.jsonl", "retrieval.jsonl", "generation.jsonl", "injection.jsonl", "confirmation.jsonl", "intent.jsonl", "guardrail.jsonl",
            "domain.jsonl", "presentation.jsonl", "answer-check.jsonl"];
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
        return new SelectionCase(Str(e, "id", where), Str(e, "question", where), tools, category, Firm(e, where), Opt(e, "source"));
    });

    /// <summary>A write to propose, and what the sentence put to a person must therefore state.</summary>
    public static IReadOnlyList<ConfirmationCase> Confirmation(string root) => Load(root, "confirmation.jsonl", (e, where) =>
        new ConfirmationCase(
            Str(e, "id", where),
            Str(e, "question", where),
            Str(e, "accountId", where),
            Decimal(e, "amount", where),
            Firm(e, where),
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
        return new RetrievalCase(Str(e, "id", where), Str(e, "query", where), relevant, Firm(e, where), Opt(e, "source"),
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
        return new PresentationCase(Str(e, "id", where), Str(e, "question", where), Firm(e, where), Bool(e, "carded"), needed, language,
            Bool(e, "rowsRequested"));
    });

    public static IReadOnlyList<GenerationCase> Generation(string root) => Load(root, "generation.jsonl", (e, where) =>
        new GenerationCase(Str(e, "id", where), Str(e, "question", where), Str(e, "referenceAnswer", where),
            Strings(e, "expectedDocIds", where, allowEmpty: true), Firm(e, where), Opt(e, "source")));

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

    public static IReadOnlyList<InjectionCase> Injection(string root) => Load(root, "injection.jsonl", (e, where) =>
        new InjectionCase(Str(e, "id", where), Str(e, "question", where), Strings(e, "forbiddenStrings", where),
            Strings(e, "forbiddenTenantIds", where, allowEmpty: true), Firm(e, where), Opt(e, "source")));

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

    private static string Firm(JsonElement e, string where)
    {
        var firm = Opt(e, "firmId") ?? "firm-a";
        return Maf.Lab.Domain.Tenancy.TenantId.TryParse(firm, out var t) && !t.IsShared ? firm : throw new InvalidDataException($"{where}: firmId '{firm}' is not a firm.");
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
