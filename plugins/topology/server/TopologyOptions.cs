namespace Maf.Lab.Plugins.Topology;

public sealed class TopologyOptions
{
    public const string Section = "Topology";
    public string ApiService { get; set; } = "api";
    public int ServicePort { get; set; } = 8080;
    public string LoadBalancerHealthUrl { get; set; } = "http://lb/lb-health";
    public string WebHealthUrl { get; set; } = "http://web/healthz";
    public double ProbeTimeoutSeconds { get; set; } = 2;
    public double CacheSeconds { get; set; } = 5;
}
