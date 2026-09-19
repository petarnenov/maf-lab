using System.Text.Json;

namespace Maf.Lab.Eval.Datasets;

public sealed record SelectionCase(string Id, string Question, IReadOnlyList<string> ExpectedTools, string Category, string FirmId, string? Source);
/// <param name="Language">Language of the query; null means the corpus language, so old datasets keep working.</param>
public sealed record RetrievalCase(string Id, string Query, IReadOnlyList<string> RelevantChunkIds, string FirmId, string? Source,
    string? Language = null);
public sealed record GenerationCase(string Id, string Question, string ReferenceAnswer, IReadOnlyList<string> ExpectedDocIds, string FirmId, string? Source);
public sealed record InjectionCase(string Id, string Question, IReadOnlyList<string> ForbiddenStrings, IReadOnlyList<string> ForbiddenTenantIds, string FirmId, string? Source);

/// <summary>Loads and validates the JSONL datasets. Invalid rows fail loudly with file and line.</summary>
public static class DatasetLoader
{
    public static readonly string[] Files = ["selection.jsonl", "retrieval.jsonl", "generation.jsonl", "injection.jsonl"];
    public static readonly string[] Tools = ["search_documents", "get_billing_run_status", "search_billing_runs"];
    public static readonly string[] SelectionCategories = ["obvious-docs", "obvious-data", "boundary", "negative", "feedback"];

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

    public static IReadOnlyList<RetrievalCase> Retrieval(string root) => Load(root, "retrieval.jsonl", (e, where) =>
        new RetrievalCase(Str(e, "id", where), Str(e, "query", where), Strings(e, "relevantChunkIds", where), Firm(e, where), Opt(e, "source"),
            Opt(e, "language")));

    public static IReadOnlyList<GenerationCase> Generation(string root) => Load(root, "generation.jsonl", (e, where) =>
        new GenerationCase(Str(e, "id", where), Str(e, "question", where), Str(e, "referenceAnswer", where),
            Strings(e, "expectedDocIds", where, allowEmpty: true), Firm(e, where), Opt(e, "source")));

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

    private static string? Opt(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

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
