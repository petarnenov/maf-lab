using System.Text.Json.Serialization;

namespace Maf.Lab.Domain.Chat;

/// <summary>A source of an answer, as the sources panel shows it.</summary>
/// <param name="Kind">"code" for a place in the repository (add-codebase-domain); null for documentation.</param>
/// <param name="StartLine">For code: the 1-based lines of the snippet in its file, when known.</param>
public sealed record SourceRef(string DocId, string SectionPath, string SourcePath, string Snippet,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Kind = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? StartLine = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? EndLine = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Symbol = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Language = null)
{
    public const string CodeKind = "code";

    /// <summary>
    /// One item of any domain's search result as a source. Documentation carries docId, sectionPath and sourcePath; a
    /// codebase snippet carries path, lines and symbol, and is shown as "path:start-end › symbol".
    /// </summary>
    public static SourceRef FromSearchItem(System.Text.Json.JsonElement r)
    {
        static string S(System.Text.Json.JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String ? v.GetString() ?? "" : "";
        static int? I(System.Text.Json.JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.Number && v.TryGetInt32(out var i) ? i : null;

        if (!r.TryGetProperty("path", out _) || r.TryGetProperty("docId", out _))
        {
            return new SourceRef(S(r, "docId"), S(r, "sectionPath"), S(r, "sourcePath"), S(r, "snippet"),
                Kind: string.IsNullOrEmpty(S(r, "kind")) ? null : S(r, "kind"), StartLine: I(r, "startLine"), EndLine: I(r, "endLine"),
                Symbol: string.IsNullOrEmpty(S(r, "symbol")) ? null : S(r, "symbol"), Language: string.IsNullOrEmpty(S(r, "language")) ? null : S(r, "language"));
        }
        var path = S(r, "path");
        var (start, end) = (I(r, "startLine"), I(r, "endLine"));
        var symbol = S(r, "symbol");
        var place = start is { } s && end is { } e2 ? $"{path}:{s}-{e2}" : path;
        return new SourceRef(path, symbol.Length > 0 ? $"{place} › {symbol}" : place, path, S(r, "snippet"), CodeKind, start, end,
            symbol.Length > 0 ? symbol : null, string.IsNullOrEmpty(S(r, "language")) ? null : S(r, "language"));
    }
}

/// <summary>What a person answers a waiting write with.</summary>
public sealed record ConfirmationDecision(string ConversationId, string AdjustmentId, bool Approve);

public sealed record ChatRequest(string? ConversationId, string Message);

public sealed record ConversationCreated(string ConversationId);
