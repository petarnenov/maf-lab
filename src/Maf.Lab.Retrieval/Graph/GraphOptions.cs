namespace Maf.Lab.Retrieval.Graph;

/// <summary>Where the graph store is. The password comes from the environment and is never printed.</summary>
public sealed class GraphOptions
{
    public const string Section = "Neo4j";

    public string Uri { get; set; } = "bolt://localhost:7687";
    public string User { get; set; } = "neo4j";
    public string Password { get; set; } = "";
    public string Database { get; set; } = "neo4j";
    /// <summary>How long a connection attempt may take before the graph counts as unavailable.</summary>
    public int ConnectTimeoutSeconds { get; set; } = 5;

    /// <summary>The address, for messages that name an unreachable service. Never the credentials.</summary>
    public string Authority => System.Uri.TryCreate(Uri, UriKind.Absolute, out var uri) ? uri.Authority : "its configured address";

    public override string ToString() => $"Neo4j {Authority} (database {Database})";
}
