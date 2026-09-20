import type { TopologyNode } from '../api/types';
import { HEALTH_MARK } from './health';
import { routeEdge } from './layout';
import type { Diagram } from './parseDiagram';
import styles from './Topology.module.css';

type Props = {
  diagram: Diagram;
  state: Map<string, TopologyNode>;
  selected: string | null;
  onSelect: (id: string) => void;
};

export function TopologyDiagram({ diagram, state, selected, onSelect }: Props) {
  const { extent } = diagram;
  return (
    <svg
      className={styles.canvas}
      viewBox={`${extent.x} ${extent.y} ${extent.width} ${extent.height}`}
      role="group"
      aria-label="Topology diagram"
    >
      <defs>
        <marker
          id="arrow"
          viewBox="0 0 10 10"
          refX="9"
          refY="5"
          markerWidth="6"
          markerHeight="6"
          orient="auto-start-reverse"
        >
          <path d="M 0 0 L 10 5 L 0 10 z" className={styles.arrowHead} />
        </marker>
      </defs>
      {diagram.edges.map((edge) => {
        const from = diagram.nodes.find((n) => n.id === edge.source)!;
        const to = diagram.nodes.find((n) => n.id === edge.target)!;
        const { points, label } = routeEdge(from, to, diagram.nodes);
        return (
          <g key={edge.id}>
            <polyline
              points={points.map((p) => `${p.x},${p.y}`).join(' ')}
              className={styles.edge}
              markerEnd="url(#arrow)"
            />
            {edge.label && (
              <text x={label.x} y={label.y - 6} className={styles.edgeLabel} textAnchor="middle">
                {edge.label}
              </text>
            )}
          </g>
        );
      })}
      {diagram.nodes.map((node) => {
        const live = state.get(node.id);
        const mark = HEALTH_MARK[live?.health ?? 'NotProbed'];
        const lines = node.label.split('\n');
        const replicas = live?.instances ?? [];
        return (
          <g
            key={node.id}
            className={`${styles.node} ${mark.className} ${selected === node.id ? styles.selected : ''}`}
            onClick={() => onSelect(node.id)}
            role="button"
            tabIndex={0}
            aria-label={`${node.label.replace('\n', ' — ')}: ${live ? mark.label : 'state unknown'}`}
            onKeyDown={(event) => {
              if (event.key === 'Enter' || event.key === ' ') {
                event.preventDefault();
                onSelect(node.id);
              }
            }}
          >
            <rect
              x={node.x}
              y={node.y}
              width={node.width}
              height={node.height}
              rx={6}
              className={`${styles.box} ${node.dashed ? styles.dashed : ''}`}
            />
            {lines.map((line, index) => (
              <text
                key={line + index}
                x={node.x + node.width / 2}
                y={node.y + 24 + index * 16}
                className={index === 0 ? styles.nodeTitle : styles.nodeSubtitle}
                textAnchor="middle"
              >
                {line}
              </text>
            ))}
            <text x={node.x + 10} y={node.y + node.height - 10} className={styles.state}>
              {live ? `${mark.symbol} ${mark.label}` : '? state unknown'}
            </text>
            {replicas.length > 0 && (
              <text
                x={node.x + node.width - 10}
                y={node.y + node.height - 10}
                className={styles.replicas}
                textAnchor="end"
              >
                {replicas.filter((r) => r.health === 'Healthy').length}/{replicas.length} up
              </text>
            )}
          </g>
        );
      })}
    </svg>
  );
}
