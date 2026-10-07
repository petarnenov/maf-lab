import type { IntentPipelineCounts, IntentStatsSettings } from './types';
import styles from './IntentStats.module.css';

const W = 900;
const H = 230;
const BOX_W = 132;
const BOX_H = 58;
const MAIN_Y = 40;
const EXIT_Y = 160;

interface Step {
  title: string;
  detail: string;
}

/**
 * How a question becomes a forced search or not, with the floors the service runs with and how many turns in the
 * period left by each edge. The main row is the path to a forced search; every drop below it is a turn that forced
 * nothing, and why.
 */
export function PipelineDiagram({
  counts,
  settings,
}: {
  counts: IntentPipelineCounts;
  settings: IntentStatsSettings;
}) {
  const steps: Step[] = [
    { title: 'Question', detail: 'any language' },
    { title: 'Jev, one call', detail: 'Choice + Noul' },
    { title: 'Confident?', detail: `confidence ≥ ${settings.minConfidence}` },
    { title: 'Forcing intent?', detail: 'procedural or mixed' },
    { title: 'In the domain?', detail: `in-domain ≥ ${settings.minInDomain}` },
    { title: 'search_documents', detail: 'forced for the turn' },
  ];
  // What leaves each step downward, in the order the classifier decides.
  const exits: ({ label: string; count: number; kind: 'failed' | 'gated' | 'used' } | null)[] = [
    null,
    { label: `no answer in ${settings.timeoutSeconds}s`, count: counts.failed, kind: 'failed' },
    {
      label: 'below the floor',
      count: counts.belowConfidence + counts.unknownChoice,
      kind: 'gated',
    },
    { label: 'data, chitchat, other', count: counts.notForcingIntent, kind: 'used' },
    { label: 'outside the domain', count: counts.outsideDomain, kind: 'gated' },
    null,
  ];
  const along = [
    counts.classified,
    counts.answered,
    counts.answered - counts.belowConfidence - counts.unknownChoice,
    counts.forcingIntent,
    counts.forced,
  ];
  const gap = (W - steps.length * BOX_W) / (steps.length - 1);
  const x = (i: number) => i * (BOX_W + gap);
  const exitClass = {
    failed: styles.seriesFailed,
    gated: styles.seriesGated,
    used: styles.seriesUsed,
  };

  return (
    <svg
      viewBox={`0 0 ${W} ${H}`}
      className={styles.diagram}
      role="img"
      aria-label="Classification pipeline"
    >
      <defs>
        <marker
          id="pipeline-arrow"
          viewBox="0 0 10 10"
          refX="9"
          refY="5"
          markerWidth="7"
          markerHeight="7"
          orient="auto"
        >
          <path d="M0,0 L10,5 L0,10 Z" className={styles.arrowHead} />
        </marker>
      </defs>
      {steps.map((step, i) => (
        <g key={step.title}>
          <rect
            x={x(i)}
            y={MAIN_Y}
            width={BOX_W}
            height={BOX_H}
            rx={8}
            className={i === steps.length - 1 ? styles.nodeGoal : styles.node}
          />
          <text
            x={x(i) + BOX_W / 2}
            y={MAIN_Y + 24}
            textAnchor="middle"
            className={styles.nodeTitle}
          >
            {step.title}
          </text>
          <text
            x={x(i) + BOX_W / 2}
            y={MAIN_Y + 42}
            textAnchor="middle"
            className={styles.nodeDetail}
          >
            {step.detail}
          </text>
          {i < steps.length - 1 && (
            <>
              <line
                x1={x(i) + BOX_W}
                x2={x(i + 1) - 2}
                y1={MAIN_Y + BOX_H / 2}
                y2={MAIN_Y + BOX_H / 2}
                className={styles.edge}
                markerEnd="url(#pipeline-arrow)"
              />
              <text
                x={x(i) + BOX_W + gap / 2}
                y={MAIN_Y + BOX_H / 2 - 6}
                textAnchor="middle"
                className={styles.edgeCount}
              >
                {along[i]}
              </text>
            </>
          )}
          {exits[i] && (
            <g>
              <line
                x1={x(i) + BOX_W / 2}
                x2={x(i) + BOX_W / 2}
                y1={MAIN_Y + BOX_H}
                y2={EXIT_Y - 4}
                className={styles.edge}
                markerEnd="url(#pipeline-arrow)"
              />
              <text
                x={x(i) + BOX_W / 2 + 6}
                y={(MAIN_Y + BOX_H + EXIT_Y) / 2 + 4}
                className={styles.edgeCount}
              >
                {exits[i]!.count}
              </text>
              <rect
                x={x(i) + 8}
                y={EXIT_Y}
                width={10}
                height={10}
                rx={2}
                className={exitClass[exits[i]!.kind]}
              />
              <text x={x(i) + 24} y={EXIT_Y + 9} className={styles.nodeDetail}>
                {exits[i]!.label}
              </text>
              <text x={x(i) + 24} y={EXIT_Y + 26} className={styles.nodeDetail}>
                {exits[i]!.kind === 'used' ? 'used, not forced' : exits[i]!.kind}
              </text>
            </g>
          )}
        </g>
      ))}
    </svg>
  );
}
