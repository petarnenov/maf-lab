using System.Text.Json.Serialization;

namespace Maf.Lab.Domain.Topology;

/// <summary>How a service answered its probe. Serialized by name, as the web app reads it.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<NodeHealth>))]
public enum NodeHealth
{
    /// <summary>Answered, and has what it needs to work.</summary>
    Healthy,
    /// <summary>Answered, but something it needs is missing (say a collection, or one silent replica).</summary>
    Degraded,
    /// <summary>Did not answer within the probe's timeout.</summary>
    Unreachable,
    /// <summary>Deliberately not contacted; the state comes from configuration (the paid chat endpoint).</summary>
    NotProbed,
}

/// <summary>One replica of a service, as it named itself.</summary>
/// <param name="Name">The instance name the service reported (its container hostname).</param>
/// <param name="Address">Where it was reached, when discovery found it by address.</param>
public sealed record TopologyInstance(string Name, string? Address, NodeHealth Health, string? Reason = null);

/// <summary>One service of the stack. <paramref name="Facts"/> are small display strings, never secrets.</summary>
public sealed record TopologyNode(
    string Id,
    string Name,
    NodeHealth Health,
    IReadOnlyList<TopologyInstance> Instances,
    IReadOnlyDictionary<string, string> Facts,
    string? Reason = null);

/// <param name="Label">What flows along the edge, e.g. "chat" or "gRPC".</param>
public sealed record TopologyEdge(string From, string To, string? Label = null);

/// <param name="GeneratedAt">When the probes ran.</param>
/// <param name="CacheSeconds">How long this report is reused before the stack is probed again.</param>
/// <param name="DiscoveryAvailable">False outside the container network, where replicas cannot be discovered.</param>
public sealed record TopologyReport(
    DateTimeOffset GeneratedAt,
    double CacheSeconds,
    bool DiscoveryAvailable,
    string ReportedBy,
    IReadOnlyList<TopologyNode> Nodes,
    IReadOnlyList<TopologyEdge> Edges);
