namespace Maf.Lab.Api.Topology;

/// <summary>Where the topology probe looks, and how hard it is allowed to look.</summary>
public sealed class TopologyOptions
{
    public const string Section = "Topology";

    /// <summary>Compose service name of the api; DNS resolves it to every replica. Empty disables discovery.</summary>
    public string ApiService { get; set; } = "api";
    /// <summary>Compose service name of the MCP server. Empty disables discovery.</summary>
    public string McpService { get; set; } = "mcp-retrieval";
    /// <summary>The second agent's container name, resolved the same way the others are.</summary>
    public string ComplianceService { get; set; } = "compliance";
    /// <summary>Port both hosts listen on inside the network.</summary>
    public int ServicePort { get; set; } = 8080;
    public string LoadBalancerHealthUrl { get; set; } = "http://lb/lb-health";
    public string WebHealthUrl { get; set; } = "http://web/healthz";
    /// <summary>Budget for every probe; one slow service cannot delay the report beyond it.</summary>
    public double ProbeTimeoutSeconds { get; set; } = 2;
    /// <summary>How long a report is reused, so the page cannot turn into a load generator.</summary>
    public double CacheSeconds { get; set; } = 5;
    /// <summary>The drawn diagram; empty falls back to walking up from the content root to docs/topology.drawio.</summary>
    public string DiagramPath { get; set; } = "";

    /// <summary>Where the signals are collected, and where they are kept. Probed like any other service.</summary>
    public string CollectorHealthUrl { get; set; } = "http://otel-collector:8889/metrics";
    public string PrometheusHealthUrl { get; set; } = "http://prometheus:9090/-/healthy";
    public string JaegerHealthUrl { get; set; } = "http://jaeger:16686/jaeger/";

    /// <summary>The shared state store. Probed by asking the api itself, which is the thing that needs it.</summary>
    public string SharedStateName { get; set; } = "redis";
}
