// ---- Topology ----

export type NodeHealth = 'Healthy' | 'Degraded' | 'Unreachable' | 'NotProbed';

export interface TopologyInstance {
  name: string;
  address: string | null;
  health: NodeHealth;
  reason: string | null;
}

export interface TopologyNode {
  id: string;
  name: string;
  health: NodeHealth;
  instances: TopologyInstance[];
  facts: Record<string, string>;
  reason: string | null;
}

export interface TopologyReport {
  generatedAt: string;
  cacheSeconds: number;
  discoveryAvailable: boolean;
  reportedBy: string;
  nodes: TopologyNode[];
}
