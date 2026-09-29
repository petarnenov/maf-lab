using System.Text.RegularExpressions;

namespace Maf.Lab.Indexing.Chunking;

/// <summary>
/// Splits code by top-level function / class / method declarations. Brace languages use brace matching,
/// Python uses indentation. Anything that cannot be attributed to a symbol falls back to fixed windows.
/// Section path is "file > Symbol"; the symbol name is stored separately.
/// </summary>
/// <param name="structural">
/// The codebase's own index (add-codebase-search) asks for more than the billing samples ever needed: each symbol also
/// takes the doc comments, attributes and decorators written directly above it, and the lines no symbol claims —
/// properties, one-line records, fields, usings, top-level statements — become chunks of their enclosing type (or of
/// the file) instead of being dropped. Off, the chunker cuts exactly as it always has, so the billing corpus keeps its
/// chunks.
/// </param>
public sealed partial class CodeChunker(bool structural = false) : IChunker
{
    /// <summary>Section name of the lines outside every type and function of a file.</summary>
    public const string FileScope = "(file)";

    public IReadOnlyList<RawChunk> Chunk(string content, string relativePath, ChunkBudget budget)
    {
        var lines = content.Replace("\r\n", "\n").Split('\n');
        var fileName = Path.GetFileName(relativePath);
        var extension = Path.GetExtension(relativePath).ToLowerInvariant();
        var types = new List<(string Name, int Start, int End)>();
        var symbols = extension switch
        {
            ".py" => PythonSymbols(lines),
            ".sql" => SqlSymbols(lines),
            ".cs" or ".ts" or ".tsx" or ".js" or ".jsx" or ".java" or ".go" or ".rs" or ".c" or ".cpp" or ".h" or ".kt" or ".swift" => BraceSymbols(lines, types),
            _ when structural => [],
            _ => BraceSymbols(lines, types),
        };

        var chunks = new List<RawChunk>();
        if (symbols.Count == 0)
        {
            foreach (var piece in ChunkText.SplitToFit(content, budget))
            {
                chunks.Add(new RawChunk(fileName, piece));
            }
            return chunks;
        }

        if (structural)
        {
            symbols = WithLeadingTrivia(lines, symbols, extension);
            symbols = [.. symbols, .. Unclaimed(lines, symbols, types)];
            symbols = [.. symbols.OrderBy(s => s.Start)];
        }

        foreach (var (name, start, end) in symbols)
        {
            var text = string.Join('\n', lines[start..(end + 1)]);
            var symbol = name == FileScope ? null : name;
            foreach (var piece in ChunkText.SplitToFit(text, budget))
            {
                chunks.Add(new RawChunk($"{fileName} > {name}", piece, symbol));
            }
        }
        return chunks;
    }

    /// <summary>Moves each symbol's start up over the comment, attribute and decorator lines directly above it.</summary>
    private static List<(string Name, int Start, int End)> WithLeadingTrivia(string[] lines, List<(string Name, int Start, int End)> symbols, string extension)
    {
        // A type's header runs up to its first member, so it holds that member's doc comment: give it back first.
        var starts = symbols.Select(x => x.Start).ToHashSet();
        symbols = symbols.Select(x =>
        {
            var e = x.End;
            if (starts.Contains(e + 1))
            {
                while (e > x.Start && (lines[e].Trim().Length == 0 || IsLeadingTrivia(lines[e], extension)))
                {
                    e--;
                }
            }
            return (x.Name, x.Start, e);
        }).ToList();

        var claimed = Claimed(lines.Length, symbols);
        var result = new List<(string, int, int)>(symbols.Count);
        foreach (var (name, start, end) in symbols)
        {
            var s = start;
            while (s > 0 && !claimed[s - 1] && IsLeadingTrivia(lines[s - 1], extension))
            {
                s--;
                claimed[s] = true;
            }
            result.Add((name, s, end));
        }
        return result;
    }

    private static bool IsLeadingTrivia(string line, string extension)
    {
        var t = line.TrimStart();
        if (t.Length == 0)
        {
            return false;
        }
        return extension switch
        {
            ".py" => t.StartsWith('#') || t.StartsWith('@'),
            ".sql" => t.StartsWith("--"),
            _ => t.StartsWith("//") || t.StartsWith("/*") || t.StartsWith('*') || t.StartsWith('[') || t.StartsWith('@'),
        };
    }

    /// <summary>
    /// Runs of lines no symbol claims that hold more than braces, grouped per enclosing type (innermost), or per file
    /// outside every type. A run ends at a claimed line, so it never spans a method.
    /// </summary>
    private static IEnumerable<(string Name, int Start, int End)> Unclaimed(string[] lines, List<(string Name, int Start, int End)> symbols, List<(string Name, int Start, int End)> types)
    {
        var claimed = Claimed(lines.Length, symbols);
        string Owner(int line) => types.Where(t => t.Start <= line && line <= t.End).OrderBy(t => t.End - t.Start).Select(t => t.Name).FirstOrDefault() ?? FileScope;

        var i = 0;
        while (i < lines.Length)
        {
            if (claimed[i] || lines[i].Trim().Length == 0)
            {
                i++;
                continue;
            }
            var owner = Owner(i);
            var start = i;
            var end = i;
            while (i < lines.Length && !claimed[i] && Owner(i) == owner)
            {
                if (lines[i].Trim().Length > 0)
                {
                    end = i;
                }
                i++;
            }
            var body = lines[start..(end + 1)];
            if (body.Any(l => l.Trim().Trim('{', '}', ')', ';', ',', ' ').Length > 0))
            {
                yield return (owner, start, end);
            }
        }
    }

    private static bool[] Claimed(int count, List<(string Name, int Start, int End)> symbols)
    {
        var claimed = new bool[count];
        foreach (var (_, start, end) in symbols)
        {
            for (var i = Math.Max(0, start); i <= end && i < count; i++)
            {
                claimed[i] = true;
            }
        }
        return claimed;
    }

    /// <summary>Top-level functions and types; members of a type become "Type.Member" chunks, the type header keeps fields.</summary>
    private static List<(string Name, int Start, int End)> BraceSymbols(string[] lines, List<(string Name, int Start, int End)> types)
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
            types.Add((name, i, end));
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
