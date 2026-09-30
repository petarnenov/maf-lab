using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Maf.Lab.TestGen.Coverage;

/// <summary>A report that is not Cobertura. Its message is safe to show: it never quotes the input.</summary>
public sealed class CoberturaFormatException(string message) : Exception(message);

/// <summary>
/// Reads Cobertura XML from either toolchain into one model. The .NET extension writes absolute file names and no
/// sources; Vitest writes names relative to one source directory and a DOCTYPE, which is ignored, never fetched.
/// A file split over several classes (partial classes, nested types, lambdas) is merged line by line.
/// </summary>
public static partial class CoberturaParser
{
    /// <summary>Largest report accepted, well above the lab's own (about 3 MB for .NET).</summary>
    public const long MaxBytes = 64L * 1024 * 1024;

    public static RawCoverageReport Parse(Stream xml)
    {
        XDocument doc;
        try
        {
            using var reader = XmlReader.Create(xml, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Ignore,
                XmlResolver = null,
                MaxCharactersInDocument = MaxBytes,
            });
            doc = XDocument.Load(reader);
        }
        catch (XmlException)
        {
            throw new CoberturaFormatException("The report is not well-formed XML.");
        }

        var root = doc.Root;
        if (root is null || root.Name.LocalName != "coverage" || root.Element("packages") is null)
        {
            throw new CoberturaFormatException("The report is not Cobertura: it has no <coverage> with <packages>.");
        }

        var sources = root.Element("sources")?.Elements("source").Select(s => s.Value.Trim()).Where(s => s.Length > 0).ToList() ?? [];
        var byFile = new Dictionary<string, Dictionary<int, LineCoverage>>(StringComparer.Ordinal);
        foreach (var cls in root.Descendants("class"))
        {
            var fileName = (string?)cls.Attribute("filename");
            if (string.IsNullOrWhiteSpace(fileName))
            {
                continue;
            }
            if (!byFile.TryGetValue(fileName, out var lines))
            {
                byFile[fileName] = lines = [];
            }
            // Class-level <lines> only: method-level lines repeat them.
            foreach (var line in cls.Elements("lines").Elements("line"))
            {
                var parsed = Line(line);
                lines[parsed.Line] = lines.TryGetValue(parsed.Line, out var seen) ? Merge(seen, parsed) : parsed;
            }
        }

        var files = byFile
            .Select(f => new FileCoverage(f.Key, f.Value.Values.OrderBy(l => l.Line).ToList()))
            .ToList();
        return new RawCoverageReport(sources, files);
    }

    private static LineCoverage Line(XElement line)
    {
        if (!int.TryParse((string?)line.Attribute("number"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) || number < 1)
        {
            throw new CoberturaFormatException("A <line> has no valid number.");
        }
        // Hit counts can exceed int in a hot loop; the view needs "how many", not an exact 64-bit figure.
        var hits = long.TryParse((string?)line.Attribute("hits"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var h)
            ? (int)Math.Clamp(h, 0, int.MaxValue)
            : 0;
        var covered = 0;
        var total = 0;
        if (string.Equals((string?)line.Attribute("branch"), "true", StringComparison.OrdinalIgnoreCase)
            && ConditionCoverage().Match((string?)line.Attribute("condition-coverage") ?? "") is { Success: true } m)
        {
            covered = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            total = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
        }
        return new LineCoverage(number, hits, Math.Min(covered, total), total);
    }

    private static LineCoverage Merge(LineCoverage a, LineCoverage b) => new(
        a.Line,
        Math.Max(a.Hits, b.Hits),
        Math.Max(a.BranchesCovered, b.BranchesCovered),
        Math.Max(a.BranchesTotal, b.BranchesTotal));

    [GeneratedRegex(@"\((\d+)/(\d+)\)")]
    private static partial Regex ConditionCoverage();
}
