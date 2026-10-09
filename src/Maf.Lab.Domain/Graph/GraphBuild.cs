using Maf.Lab.Retrieval.Graph;

namespace Maf.Lab.Indexing.Graph;

/// <summary>What a builder produced for one subgraph, before anything is written.</summary>
/// <param name="Rejected">Source records that could not become a node (no tenant, no key), with why.</param>
/// <param name="UnresolvedCalls">Code graph only: calls whose target could not be resolved, or is outside the repository.</param>
public sealed record GraphBuild(
    string Source,
    IReadOnlyList<GraphNode> Nodes,
    IReadOnlyList<GraphEdge> Edges,
    IReadOnlyList<string> Rejected,
    int UnresolvedCalls = 0);

/// <summary>The summary the graph command prints on stdout, one per subgraph built.</summary>
public sealed record GraphBuildSummary(
    string Source,
    int NodesWritten,
    int NodesUnchanged,
    int EdgesWritten,
    int EdgesUnchanged,
    int NodesRemoved,
    int EdgesRemoved,
    int Rejected,
    int UnresolvedCalls,
    long NodesTotal,
    long EdgesTotal);
