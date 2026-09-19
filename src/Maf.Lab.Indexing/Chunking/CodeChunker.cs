using System.Text.RegularExpressions;

namespace Maf.Lab.Indexing.Chunking;

/// <summary>
/// Splits code by top-level function / class / method declarations. Brace languages use brace matching,
/// Python uses indentation. Anything that cannot be attributed to a symbol falls back to fixed windows.
/// Section path is "file > Symbol"; the symbol name is stored separately.
/// </summary>
public sealed partial class CodeChunker : IChunker
{
    public IReadOnlyList<RawChunk> Chunk(string content, string relativePath, int maxChars)
    {
        var lines = content.Replace("\r\n", "\n").Split('\n');
        var fileName = Path.GetFileName(relativePath);
        var symbols = Path.GetExtension(relativePath).ToLowerInvariant() switch
        {
            ".py" => PythonSymbols(lines),
            ".sql" => SqlSymbols(lines),
            _ => BraceSymbols(lines),
        };

        var chunks = new List<RawChunk>();
        if (symbols.Count == 0)
        {
            foreach (var piece in ChunkText.SplitToFit(content, maxChars))
            {
                chunks.Add(new RawChunk(fileName, piece));
            }
            return chunks;
        }

        foreach (var (name, start, end) in symbols)
        {
            var text = string.Join('\n', lines[start..(end + 1)]);
            foreach (var piece in ChunkText.SplitToFit(text, maxChars))
            {
                chunks.Add(new RawChunk($"{fileName} > {name}", piece, name));
            }
        }
        return chunks;
    }

    /// <summary>Top-level functions and types; members of a type become "Type.Member" chunks, the type header keeps fields.</summary>
    private static List<(string Name, int Start, int End)> BraceSymbols(string[] lines)
    {
        var result = new List<(string, int, int)>();
        var i = 0;
        while (i < lines.Length)
        {
            var m = Declaration(lines[i]);
            var end = m is null ? i : FindBlockEnd(lines, i);
            if (m is null || (end <= i && !lines[i].Contains('{')))
            {
                i++;
                continue;
            }

            var name = m.Groups["name"].Value;
            if (!IsType(m))
            {
                result.Add((name, i, end));
                i = end + 1;
                continue;
            }

            var members = new List<(string, int, int)>();
            var j = i + 1;
            while (j < end)
            {
                var mm = Declaration(lines[j]);
                if (mm is not null && !IsType(mm))
                {
                    var memberEnd = FindBlockEnd(lines, j);
                    if (memberEnd > j || lines[j].Contains('{'))
                    {
                        members.Add(($"{name}.{mm.Groups["name"].Value}", j, memberEnd));
                        j = memberEnd + 1;
                        continue;
                    }
                }
                j++;
            }
            var headerEnd = members.Count > 0 ? members[0].Item2 - 1 : end;
            result.Add((name, i, Math.Max(i, headerEnd)));
            result.AddRange(members);
            i = end + 1;
        }
        return result;
    }

    private static readonly HashSet<string> Keywords = ["if", "for", "foreach", "while", "switch", "catch", "using", "lock", "return", "new", "await", "else", "fixed", "when"];

    private static Match? Declaration(string line)
    {
        var m = BraceDeclaration().Match(line);
        return m.Success && !Keywords.Contains(m.Groups["name"].Value) ? m : null;
    }

    private static bool IsType(Match m) => m.Groups["kind"].Value is "class" or "interface" or "record" or "struct" or "enum";

    private static int FindBlockEnd(string[] lines, int start)
    {
        var depth = 0;
        var opened = false;
        for (var i = start; i < lines.Length; i++)
        {
            foreach (var c in lines[i])
            {
                if (c == '{') { depth++; opened = true; }
                else if (c == '}') { depth--; }
            }
            if (opened && depth <= 0)
            {
                return i;
            }
            if (!opened && i > start + 3)
            {
                return start;
            }
        }
        return lines.Length - 1;
    }

    private static List<(string Name, int Start, int End)> PythonSymbols(string[] lines)
    {
        var result = new List<(string, int, int)>();
        string? cls = null;
        for (var i = 0; i < lines.Length; i++)
        {
            var m = PythonDeclaration().Match(lines[i]);
            if (!m.Success)
            {
                continue;
            }
            var indent = m.Groups["indent"].Value.Length;
            var name = m.Groups["name"].Value;
            var end = i;
            for (var j = i + 1; j < lines.Length; j++)
            {
                if (lines[j].Trim().Length == 0) continue;
                if (lines[j].Length - lines[j].TrimStart().Length <= indent) break;
                end = j;
            }
            if (m.Groups["kind"].Value == "class")
            {
                cls = name;
                var firstMethod = Enumerable.Range(i + 1, Math.Max(0, end - i)).FirstOrDefault(j => PythonDeclaration().IsMatch(lines[j]), end + 1);
                result.Add((name, i, Math.Max(i, firstMethod - 1)));
            }
            else
            {
                result.Add((indent > 0 && cls is not null ? $"{cls}.{name}" : name, i, end));
                if (indent == 0) cls = null;
                i = indent == 0 ? end : i;
            }
        }
        return result;
    }

    private static List<(string Name, int Start, int End)> SqlSymbols(string[] lines)
    {
        var result = new List<(string, int, int)>();
        var start = -1;
        string? name = null;
        for (var i = 0; i < lines.Length; i++)
        {
            var m = SqlDeclaration().Match(lines[i]);
            if (m.Success)
            {
                start = i;
                name = m.Groups["name"].Value;
            }
            if (start >= 0 && lines[i].TrimEnd().EndsWith(';'))
            {
                result.Add((name!, start, i));
                start = -1;
            }
        }
        return result;
    }

    [GeneratedRegex(@"^\s*(?:(?:public|private|protected|internal|static|export|default|async|abstract|sealed|override|virtual|partial|readonly)\s+)*(?:(?<kind>class|interface|record|struct|enum|function)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)|(?:const|let)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?:async\s*)?\(|[A-Za-z_<>\[\],?. ]+\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\([^;]*$)")]
    private static partial Regex BraceDeclaration();

    [GeneratedRegex(@"^(?<indent>\s*)(?:async\s+)?(?<kind>def|class)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)")]
    private static partial Regex PythonDeclaration();

    [GeneratedRegex(@"^\s*CREATE\s+(?:OR\s+REPLACE\s+)?(?:FUNCTION|PROCEDURE|VIEW|TABLE)\s+(?<name>[A-Za-z_][A-Za-z0-9_.]*)", RegexOptions.IgnoreCase)]
    private static partial Regex SqlDeclaration();
}
