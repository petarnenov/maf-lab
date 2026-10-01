using System.Text.RegularExpressions;

namespace Maf.Lab.Tests;

/// <summary>
/// Only the official AG-UI protocol crosses the wire (agui-protocol-only): no custom events, no AG-UI event built
/// outside the official server's mapping hooks, no SSE code of our own. Each rule names the files that break it.
/// </summary>
public sealed class AGUIProtocolOnlyTests
{
    /// <summary>The one directory that may touch the AG-UI types: the agents' wiring to the official server.</summary>
    private const string AGUIDirectory = "src/Maf.Lab.Api/Agent/AGUI/";

    /// <summary>The one file that may construct an AG-UI event: the mapping hooks registered with that server.</summary>
    private const string MappingsFile = AGUIDirectory + "AGUIMappings.cs";

    private static readonly Regex AGUINamespace = new(@"\bAGUI\.(Abstractions|Server)\b", RegexOptions.Compiled);
    private static readonly Regex EventConstruction = new(@"\bnew\s+[A-Z]\w*Event\b", RegexOptions.Compiled);
    private static readonly Regex OwnSse = new(@"text/event-stream|ServerSentEvents|SseItem<|SseFormatter", RegexOptions.Compiled);

    [Fact]
    public void No_custom_event_anywhere() =>
        AssertNone(Sources().Where(f => f.Text.Contains("CustomEvent", StringComparison.Ordinal)), "uses CustomEvent");

    [Fact]
    public void AGUI_types_are_used_only_where_agents_meet_the_official_server() =>
        AssertNone(Sources().Where(f => !f.Path.StartsWith(AGUIDirectory, StringComparison.Ordinal) && AGUINamespace.IsMatch(f.Text)),
            $"references AGUI.* outside {AGUIDirectory}");

    [Fact]
    public void AGUI_events_are_built_only_in_the_mapping_hooks() =>
        AssertNone(Sources().Where(f => f.Path.StartsWith(AGUIDirectory, StringComparison.Ordinal) && f.Path != MappingsFile
                && EventConstruction.IsMatch(f.Text)),
            $"constructs an AG-UI event outside {MappingsFile}");

    [Fact]
    public void No_event_stream_of_our_own() =>
        AssertNone(Sources().Where(f => f.Path.StartsWith("src/Maf.Lab.Api/", StringComparison.Ordinal) && OwnSse.IsMatch(f.Text)),
            "writes its own event stream");

    private static IEnumerable<(string Path, string Text)> Sources()
    {
        var root = CorpusLoaderTests.RepoRoot();
        return Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Select(p => (Path.GetRelativePath(root, p).Replace('\\', '/'), File.ReadAllText(p)));
    }

    private static void AssertNone(IEnumerable<(string Path, string Text)> offenders, string what)
    {
        var paths = offenders.Select(o => o.Path).Order(StringComparer.Ordinal).ToList();
        Assert.True(paths.Count == 0, $"{paths.Count} file(s) {what}:\n  " + string.Join("\n  ", paths));
    }
}
