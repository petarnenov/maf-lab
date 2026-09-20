import type { DiagramNode } from './parseDiagram';

export type Point = { x: number; y: number };

/** Where an edge's line runs, and where its label sits along it. */
export type EdgeRoute = { points: Point[]; label: Point };

/** How far a detour stands clear of the boxes it goes around. */
const LANE = 40;

/** A box counts as in the way a little before the line actually touches it. */
const MARGIN = 6;

const centre = (box: DiagramNode): Point => ({
  x: box.x + box.width / 2,
  y: box.y + box.height / 2,
});

/**
 * The line between two boxes.
 *
 * Straight where a straight line is readable, and clipped to both borders so it neither starts under the source's
 * text nor ends under the target's. Where a straight line would pass through a third box — the api reaching
 * qdrant past mcp-retrieval, which used to draw a line nobody could see — it goes around instead, in the lane
 * above the row if that is clear and below it otherwise.
 */
export function routeEdge(from: DiagramNode, to: DiagramNode, others: DiagramNode[]): EdgeRoute {
  const blockers = others.filter((n) => n.id !== from.id && n.id !== to.id);
  const [a, b] = [centre(from), centre(to)];

  if (!blockers.some((box) => crosses(a, b, box))) {
    const points = [border(from, b), border(to, a)];
    return { points, label: midpoint(points[0], points[1]) };
  }

  for (const lane of [
    Math.min(from.y, to.y) - LANE,
    Math.max(from.y + from.height, to.y + to.height) + LANE,
  ]) {
    const above = lane < from.y;
    const start = { x: a.x, y: above ? from.y : from.y + from.height };
    const end = { x: b.x, y: above ? to.y : to.y + to.height };
    const points = [start, { x: a.x, y: lane }, { x: b.x, y: lane }, end];
    if (!blockers.some((box) => segments(points).some(([p, q]) => crosses(p, q, box)))) {
      return { points, label: { x: (a.x + b.x) / 2, y: lane } };
    }
  }

  // Nowhere clear to go: a straight line is still better than no line.
  const points = [border(from, b), border(to, a)];
  return { points, label: midpoint(points[0], points[1]) };
}

/** Where the line from the box's centre towards `towards` leaves the box. */
function border(box: DiagramNode, towards: Point): Point {
  const from = centre(box);
  const [dx, dy] = [towards.x - from.x, towards.y - from.y];
  if (dx === 0 && dy === 0) return from;
  const scale = Math.min(
    dx === 0 ? Infinity : box.width / 2 / Math.abs(dx),
    dy === 0 ? Infinity : box.height / 2 / Math.abs(dy),
  );
  return { x: from.x + dx * scale, y: from.y + dy * scale };
}

const midpoint = (p: Point, q: Point): Point => ({ x: (p.x + q.x) / 2, y: (p.y + q.y) / 2 });

function segments(points: Point[]): [Point, Point][] {
  return points.slice(0, -1).map((p, i) => [p, points[i + 1]]);
}

/** Whether the segment p→q passes through the box (Liang–Barsky against the box's four slabs). */
function crosses(p: Point, q: Point, box: DiagramNode): boolean {
  const [left, right] = [box.x - MARGIN, box.x + box.width + MARGIN];
  const [top, bottom] = [box.y - MARGIN, box.y + box.height + MARGIN];
  const [dx, dy] = [q.x - p.x, q.y - p.y];
  let [enter, exit] = [0, 1];

  const clip = (delta: number, distance: number): boolean => {
    if (delta === 0) return distance >= 0; // parallel to this slab: inside it, or nowhere near
    const t = distance / delta;
    if (delta < 0) {
      enter = Math.max(enter, t);
    } else {
      exit = Math.min(exit, t);
    }
    return enter <= exit;
  };

  return (
    clip(-dx, p.x - left) && clip(dx, right - p.x) && clip(-dy, p.y - top) && clip(dy, bottom - p.y)
  );
}
