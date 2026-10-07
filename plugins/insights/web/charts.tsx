import { useRef, useState, type MouseEvent, type ReactNode } from 'react';
import styles from './IntentStats.module.css';
import { linear, niceMax, ticks } from './scale';

/*
 * Hand-built SVG charts for the intent statistics screen. The marks follow one set of specs: bars at most 24 px thick
 * with a 4 px rounded data end and a square baseline, a 2 px surface gap between stacked segments, 2 px lines,
 * markers of radius 4 with a surface ring, hairline solid grid, dashed lines only for thresholds. Colour comes from
 * CSS classes, so light and dark are chosen in one place; identity never rests on colour alone — every chart with
 * more than one series has a legend, and every mark answers on hover.
 */

const W = 560;
const PAD = { top: 12, right: 12, bottom: 28, left: 40 };

export interface Series {
  label: string;
  /** A class from the CSS module that sets fill/stroke for this series. */
  className: string;
}

type TipState = { x: number; y: number; lines: string[] } | null;

/** A chart's frame: the positioned box its tooltip lives in, and the handlers marks use to show it. */
function useTip() {
  const box = useRef<HTMLDivElement>(null);
  const [tip, setTip] = useState<TipState>(null);
  const show = (event: MouseEvent<SVGElement>, lines: string[]) => {
    const rect = box.current?.getBoundingClientRect();
    if (!rect) return;
    setTip({ x: event.clientX - rect.left, y: event.clientY - rect.top, lines });
  };
  const hide = () => setTip(null);
  const frame = (children: ReactNode) => (
    <div ref={box} className={styles.frame} onMouseLeave={hide}>
      {children}
      {tip && (
        <div
          className={styles.tip}
          role="tooltip"
          style={{ left: tip.x + 12, top: Math.max(0, tip.y - 12) }}
        >
          {tip.lines.map((line, i) => (
            <div key={i} className={i === 0 ? styles.tipHead : undefined}>
              {line}
            </div>
          ))}
        </div>
      )}
    </div>
  );
  return { show, hide, frame };
}

export function Legend({ series }: { series: Series[] }) {
  return (
    <ul className={styles.legend}>
      {series.map((s) => (
        <li key={s.label}>
          <svg width="10" height="10" aria-hidden="true">
            <rect width="10" height="10" rx="2" className={s.className} />
          </svg>
          {s.label}
        </li>
      ))}
    </ul>
  );
}

/** A rectangle with only its top corners rounded: the data end, never the baseline. */
function topRounded(x: number, y: number, w: number, h: number, r: number): string {
  const rr = Math.min(r, w / 2, h);
  return `M${x},${y + h} V${y + rr} Q${x},${y} ${x + rr},${y} H${x + w - rr} Q${x + w},${y} ${x + w},${y + rr} V${y + h} Z`;
}

/** A rectangle with only its right corners rounded, for horizontal bars. */
function rightRounded(x: number, y: number, w: number, h: number, r: number): string {
  const rr = Math.min(r, h / 2, w);
  return `M${x},${y} H${x + w - rr} Q${x + w},${y} ${x + w},${y + rr} V${y + h - rr} Q${x + w},${y + h} ${x + w - rr},${y + h} H${x} Z`;
}

export interface Column {
  /** Tooltip heading. */
  label: string;
  /** One value per series, bottom to top. */
  values: number[];
}

export interface Threshold {
  /** Position in column units: 10 is the left edge of the eleventh column. */
  at: number;
  label: string;
}

/**
 * Stacked columns over ordered categories — time buckets or histogram bins. Thresholds are drawn as dashed lines at a
 * column edge with their label, so a floor reads as a floor and not as a grid line.
 */
export function Columns({
  columns,
  series,
  xTicks,
  thresholds = [],
  height = 180,
  ariaLabel,
  unit = 'turns',
}: {
  columns: Column[];
  series: Series[];
  xTicks: { at: number; label: string }[];
  thresholds?: Threshold[];
  height?: number;
  ariaLabel: string;
  unit?: string;
}) {
  const { show, frame } = useTip();
  const max = niceMax(Math.max(0, ...columns.map((c) => c.values.reduce((a, b) => a + b, 0))));
  const y = linear(0, max, height - PAD.bottom, PAD.top);
  const slot = (W - PAD.left - PAD.right) / Math.max(1, columns.length);
  const x = (at: number) => PAD.left + at * slot;
  const barW = Math.min(24, Math.max(2, slot - 2));

  return frame(
    <svg viewBox={`0 0 ${W} ${height}`} className={styles.chart} role="img" aria-label={ariaLabel}>
      {ticks(max).map((t) => (
        <g key={t}>
          <line x1={PAD.left} x2={W - PAD.right} y1={y(t)} y2={y(t)} className={styles.grid} />
          <text x={PAD.left - 6} y={y(t) + 3} textAnchor="end" className={styles.axis}>
            {t}
          </text>
        </g>
      ))}
      {xTicks.map((t) => (
        <text
          key={`${t.at}-${t.label}`}
          x={x(t.at)}
          y={height - 10}
          textAnchor="middle"
          className={styles.axis}
        >
          {t.label}
        </text>
      ))}
      {columns.map((c, i) => {
        const total = c.values.reduce((a, b) => a + b, 0);
        let base = height - PAD.bottom;
        const cx = x(i) + (slot - barW) / 2;
        const top = c.values.reduce((last, v, k) => (v > 0 ? k : last), -1);
        return (
          <g
            key={i}
            onMouseMove={(e) =>
              show(e, [
                c.label,
                ...series.map((s, k) => `${s.label}: ${c.values[k]}`),
                ...(series.length > 1 ? [`total: ${total} ${unit}`] : []),
              ])
            }
          >
            {/* The hit target is the whole slot, taller and wider than the mark. */}
            <rect
              x={x(i)}
              y={PAD.top}
              width={slot}
              height={height - PAD.top - PAD.bottom}
              className={styles.hit}
            />
            {c.values.map((v, k) => {
              if (v <= 0) return null;
              const h = y(0) - y(v);
              const segTop = base - h;
              // The 2 px surface gap separates a segment from the one beneath it.
              const gap = base < height - PAD.bottom ? 2 : 0;
              const d =
                k === top
                  ? topRounded(cx, segTop, barW, Math.max(0, h - gap), 4)
                  : `M${cx},${segTop} h${barW} v${Math.max(0, h - gap)} h${-barW} Z`;
              base = segTop;
              return <path key={k} d={d} className={series[k].className} />;
            })}
          </g>
        );
      })}
      <line x1={PAD.left} x2={W - PAD.right} y1={y(0)} y2={y(0)} className={styles.baseline} />
      {thresholds.map((t) => (
        <g key={t.label}>
          <line
            x1={x(t.at)}
            x2={x(t.at)}
            y1={PAD.top - 4}
            y2={height - PAD.bottom}
            className={styles.threshold}
          />
          <text x={x(t.at) + 4} y={PAD.top + 6} className={styles.thresholdLabel}>
            {t.label}
          </text>
        </g>
      ))}
    </svg>,
  );
}

/** Horizontal bars, one series: a category per row, the value at the bar's end. */
export function Bars({
  rows,
  format = String,
  max,
  ariaLabel,
  className = styles.seriesUsed,
}: {
  rows: { label: string; value: number; className?: string; tip?: string[] }[];
  format?: (value: number) => string;
  max?: number;
  ariaLabel: string;
  className?: string;
}) {
  const { show, frame } = useTip();
  const rowH = 26;
  const left = 150;
  const right = 60;
  const height = rows.length * rowH + 8;
  const top = max ?? Math.max(1e-9, ...rows.map((r) => r.value));
  const x = linear(0, top, left, W - right);

  return frame(
    <svg viewBox={`0 0 ${W} ${height}`} className={styles.chart} role="img" aria-label={ariaLabel}>
      <line x1={left} x2={left} y1={0} y2={height - 4} className={styles.baseline} />
      {rows.map((r, i) => {
        const y = 4 + i * rowH;
        const w = Math.max(r.value > 0 ? 2 : 0, x(r.value) - left);
        return (
          <g key={r.label} onMouseMove={(e) => show(e, r.tip ?? [r.label, format(r.value)])}>
            <rect x={0} y={y} width={W} height={rowH} className={styles.hit} />
            <text x={left - 8} y={y + rowH / 2 + 4} textAnchor="end" className={styles.rowLabel}>
              {r.label}
            </text>
            {w > 0 && (
              <path
                d={rightRounded(left, y + 5, w, rowH - 10, 4)}
                className={r.className ?? className}
              />
            )}
            <text x={left + w + 6} y={y + rowH / 2 + 4} className={styles.value}>
              {format(r.value)}
            </text>
          </g>
        );
      })}
    </svg>,
  );
}

export interface LineSeries extends Series {
  /** One value per x position; null leaves a gap. */
  values: (number | null)[];
}

/** Lines over ordered positions on one shared y-axis, with an end label for each line. */
export function Lines({
  lines,
  xLabels,
  xTicks,
  yDomain,
  format,
  ariaLabel,
  height = 180,
  thresholds = [],
}: {
  lines: LineSeries[];
  xLabels: string[];
  xTicks: { at: number; label: string }[];
  yDomain: [number, number];
  format: (value: number) => string;
  ariaLabel: string;
  height?: number;
  thresholds?: { value: number; label: string }[];
}) {
  const { show, frame } = useTip();
  const n = xLabels.length;
  const right = 70;
  const x = linear(0, Math.max(1, n - 1), PAD.left, W - right);
  const y = linear(yDomain[0], yDomain[1], height - PAD.bottom, PAD.top);
  const gridValues = [0, 0.25, 0.5, 0.75, 1].map((f) => yDomain[0] + f * (yDomain[1] - yDomain[0]));

  return frame(
    <svg viewBox={`0 0 ${W} ${height}`} className={styles.chart} role="img" aria-label={ariaLabel}>
      {gridValues.map((v) => (
        <g key={v}>
          <line x1={PAD.left} x2={W - right} y1={y(v)} y2={y(v)} className={styles.grid} />
          <text x={PAD.left - 6} y={y(v) + 3} textAnchor="end" className={styles.axis}>
            {format(v)}
          </text>
        </g>
      ))}
      {thresholds.map((t) => (
        <g key={t.label}>
          <line
            x1={PAD.left}
            x2={W - right}
            y1={y(t.value)}
            y2={y(t.value)}
            className={styles.threshold}
          />
          <text x={W - right + 4} y={y(t.value) + 3} className={styles.thresholdLabel}>
            {t.label}
          </text>
        </g>
      ))}
      {xTicks.map((t) => (
        <text
          key={`${t.at}-${t.label}`}
          x={x(t.at)}
          y={height - 10}
          textAnchor="middle"
          className={styles.axis}
        >
          {t.label}
        </text>
      ))}
      {lines.map((line) => {
        // Consecutive measured points form segments; a missing value breaks the line rather than drawing a zero.
        const segments: string[][] = [[]];
        line.values.forEach((v, i) => {
          if (v == null) segments.push([]);
          else segments[segments.length - 1].push(`${x(i)},${y(v)}`);
        });
        const last = line.values.reduce<number>((at, v, i) => (v == null ? at : i), -1);
        return (
          <g key={line.label} className={line.className}>
            {segments
              .filter((s) => s.length > 1)
              .map((s, k) => (
                <polyline key={k} points={s.join(' ')} className={styles.line} />
              ))}
            {line.values.map((v, i) =>
              // Every point is marked while there are few; past that only the end and any point with no line
              // through it, which would otherwise not be drawn at all.
              v == null ||
              (n > 24 &&
                i !== last &&
                line.values[i - 1] != null &&
                line.values[i + 1] != null) ? null : (
                <circle key={i} cx={x(i)} cy={y(v)} r={4} className={styles.dot} />
              ),
            )}
            {last >= 0 && (
              <text x={x(last) + 8} y={y(line.values[last]!) + 3} className={styles.value}>
                {format(line.values[last]!)}
              </text>
            )}
          </g>
        );
      })}
      {xLabels.map((label, i) => (
        <rect
          key={i}
          x={x(i) - (W - right - PAD.left) / Math.max(1, n - 1) / 2}
          y={PAD.top}
          width={(W - right - PAD.left) / Math.max(1, n - 1)}
          height={height - PAD.top - PAD.bottom}
          className={styles.hit}
          onMouseMove={(e) =>
            show(e, [
              label,
              ...lines.map(
                (l) => `${l.label}: ${l.values[i] == null ? 'no data' : format(l.values[i]!)}`,
              ),
            ])
          }
        />
      ))}
    </svg>,
  );
}

export interface ScatterPoint {
  x: number;
  y: number;
  className: string;
  tip: string[];
}

/** Points on a unit square with a floor on each axis; the quadrant past both floors is where retrieval can be forced. */
export function Scatter({
  points,
  xFloor,
  yFloor,
  xLabel,
  yLabel,
  ariaLabel,
}: {
  points: ScatterPoint[];
  xFloor: number;
  yFloor: number;
  xLabel: string;
  yLabel: string;
  ariaLabel: string;
}) {
  const { show, frame } = useTip();
  const height = 300;
  const left = 48;
  const bottom = 40;
  const x = linear(0, 1, left, W - PAD.right);
  const y = linear(0, 1, height - bottom, PAD.top);
  const grid = [0, 0.25, 0.5, 0.75, 1];

  return frame(
    <svg viewBox={`0 0 ${W} ${height}`} className={styles.chart} role="img" aria-label={ariaLabel}>
      {grid.map((g) => (
        <g key={g}>
          <line x1={x(0)} x2={x(1)} y1={y(g)} y2={y(g)} className={styles.grid} />
          <line x1={x(g)} x2={x(g)} y1={y(0)} y2={y(1)} className={styles.grid} />
          <text x={left - 6} y={y(g) + 3} textAnchor="end" className={styles.axis}>
            {g}
          </text>
          <text x={x(g)} y={height - bottom + 14} textAnchor="middle" className={styles.axis}>
            {g}
          </text>
        </g>
      ))}
      <text x={(x(0) + x(1)) / 2} y={height - 6} textAnchor="middle" className={styles.axisTitle}>
        {xLabel}
      </text>
      <text
        x={12}
        y={(y(0) + y(1)) / 2}
        textAnchor="middle"
        transform={`rotate(-90 12 ${(y(0) + y(1)) / 2})`}
        className={styles.axisTitle}
      >
        {yLabel}
      </text>
      <line x1={x(xFloor)} x2={x(xFloor)} y1={y(0)} y2={y(1)} className={styles.threshold} />
      <text x={x(xFloor) + 4} y={y(1) + 10} className={styles.thresholdLabel}>
        confidence floor {xFloor}
      </text>
      <line x1={x(0)} x2={x(1)} y1={y(yFloor)} y2={y(yFloor)} className={styles.threshold} />
      <text x={x(0) + 4} y={y(yFloor) - 4} className={styles.thresholdLabel}>
        in-domain floor {yFloor}
      </text>
      {points.map((p, i) => (
        <circle
          key={i}
          cx={x(p.x)}
          cy={y(p.y)}
          r={4}
          className={`${styles.dot} ${p.className}`}
          onMouseMove={(e) => show(e, p.tip)}
        />
      ))}
    </svg>,
  );
}
