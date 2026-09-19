using System.Text;
using System.Text.RegularExpressions;

namespace Maf.Lab.Indexing.Chunking;

/// <summary>
/// Splits procedures (structured text) by section and numbered step. The first non-empty line is the title.
/// Section headers: "Section 2: ...", "## ...", or "UPPER CASE LINE:". Steps: "1." / "1)" / "Step 1:".
/// </summary>
public sealed partial class ProcedureChunker : IChunker
{
    public IReadOnlyList<RawChunk> Chunk(string content, string relativePath, int maxChars)
    {
        var lines = content.Replace("\r\n", "\n").Split('\n');
        var title = lines.FirstOrDefault(l => l.Trim().Length > 0)?.Trim().TrimStart('#', ' ') ?? Path.GetFileNameWithoutExtension(relativePath);
        var chunks = new List<RawChunk>();
        string? section = null;
        string? step = null;
        var buffer = new StringBuilder();
        var titleConsumed = false;

        void Flush()
        {
            var path = string.Join(" > ", new[] { title, section, step }.Where(s => !string.IsNullOrEmpty(s)));
            foreach (var piece in ChunkText.SplitToFit(buffer.ToString(), maxChars))
            {
                chunks.Add(new RawChunk(path, piece));
            }
            buffer.Clear();
        }

        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();
            if (!titleConsumed && line.Trim().Length > 0)
            {
                titleConsumed = true;
                continue;
            }
            if (SectionHeader().Match(line) is { Success: true } sm)
            {
                Flush();
                section = sm.Groups["title"].Value.Trim().TrimEnd(':');
                step = null;
                continue;
            }
            if (StepStart().Match(line) is { Success: true } st)
            {
                Flush();
                step = $"Step {st.Groups["n"].Value}";
            }
            buffer.Append(line).Append('\n');
        }
        Flush();
        return chunks;
    }

    [GeneratedRegex(@"^(?:#{1,3}\s+(?<title>.+)|(?<title>Section\s+\d+[^\n]*)|(?<title>[A-Z][A-Z0-9 /&\-]{3,}):?\s*)$")]
    private static partial Regex SectionHeader();

    [GeneratedRegex(@"^\s*(?:Step\s+)?(?<n>\d+)[.):]\s+\S", RegexOptions.IgnoreCase)]
    private static partial Regex StepStart();
}
