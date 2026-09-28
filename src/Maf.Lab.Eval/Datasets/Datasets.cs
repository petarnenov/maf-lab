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
public sealed record GuardrailCase(string Id, string Side, string Text, bool Malicious, string Category, string Language, string Split);

public sealed record InjectionCase(string Id, string Question, IReadOnlyList<string> ForbiddenStrings, IReadOnlyList<string> ForbiddenTenantIds, string FirmId, string? Source);

/// <summary>Loads and validates the JSONL datasets. Invalid rows fail loudly with file and line.</summary>
public static class DatasetLoader
{
    public static readonly string[] Files =
        ["selection.jsonl", "retrieval.jsonl", "generation.jsonl", "injection.jsonl", "confirmation.jsonl", "intent.jsonl", "guardrail.jsonl",
            "domain.jsonl"];
    public static readonly string[] Tools =
        ["search_documents", "get_billing_run_status", "search_billing_runs", Maf.Lab.Domain.Billing.FeeAdjustmentTool.Name,
            Maf.Lab.Domain.Portfolio.PortfolioTools.Search, Maf.Lab.Domain.Portfolio.PortfolioTools.GetPortfolio,
            Maf.Lab.Domain.Portfolio.PortfolioTools.AumHistory];
    public static readonly string[] SelectionCategories = ["obvious-docs", "obvious-data", "boundary", "negative", "feedback", "portfolio", "cross-domain"];

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

    public static readonly string[] DomainExpectations = ["billing", "portfolio", "both", "none"];

    /// <summary>Questions for Jev's domain verdict alone, each labelled with the domains it belongs to.</summary>
    public static IReadOnlyList<DomainCase> Domain(string root) => Load(root, "domain.jsonl", (e, where) =>
    {
        var expected = Str(e, "expected", where);
        var language = Str(e, "language", where);
        var split = Str(e, "split", where);
        if (!DomainExpectations.Contains(expected) || !IntentLanguages.Contains(language) || split is not ("design" or "holdout"))
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
        return new GuardrailCase(Str(e, "id", where), side, Str(e, "text", where), m.GetBoolean(), Str(e, "category", where), language, split);
    });

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
