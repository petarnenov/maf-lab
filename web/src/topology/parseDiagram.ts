/**
 * Reads the drawn diagram (docs/topology.drawio) as mxGraph XML: the boxes, where they are, what they are called,
 * and the arrows between them. Anything the renderer does not need — styles, waypoints, draw.io's own metadata —
 * is ignored, so the file can be edited freely in draw.io without breaking the page.
 */
export type DiagramNode = {
  id: string;
  label: string;
  x: number;
  y: number;
  width: number;
  height: number;
  /** A dashed box is drawn dashed: the diagram marks external or optional services that way. */
  dashed: boolean;
};

export type DiagramEdge = {
  id: string;
  source: string;
  target: string;
  label: string;
};

export type Diagram = {
  nodes: DiagramNode[];
  edges: DiagramEdge[];
  /** The drawing's extent, used as the SVG viewBox so it scales to whatever pane it lands in. */
  extent: { x: number; y: number; width: number; height: number };
};

export class DiagramError extends Error {}

const PADDING = 24;

export function parseDiagram(xml: string): Diagram {
  const doc = new DOMParser().parseFromString(xml, 'application/xml');
  if (doc.querySelector('parsererror')) {
    throw new DiagramError('The topology diagram is not valid XML.');
  }
  const cells = [...doc.querySelectorAll('mxCell')];
  if (cells.length === 0) {
    // draw.io's compressed format stores the whole drawing as one base64 blob inside <diagram>.
    throw new DiagramError(
      'The topology diagram has no cells — it was probably saved compressed. Save it uncompressed.',
    );
  }

  const nodes: DiagramNode[] = [];
  const edges: DiagramEdge[] = [];
  for (const cell of cells) {
    const id = cell.getAttribute('id');
    if (!id) continue;
    const style = cell.getAttribute('style') ?? '';
    if (cell.getAttribute('vertex') === '1') {
      const geometry = cell.querySelector('mxGeometry');
      if (!geometry) continue;
      nodes.push({
        id,
        label: text(cell.getAttribute('value')),
        x: number(geometry.getAttribute('x')),
        y: number(geometry.getAttribute('y')),
        width: number(geometry.getAttribute('width')) || 120,
        height: number(geometry.getAttribute('height')) || 60,
        dashed: style.includes('dashed=1'),
      });
    } else if (cell.getAttribute('edge') === '1') {
      const source = cell.getAttribute('source');
      const target = cell.getAttribute('target');
      // An edge without both ends cannot be drawn; the diagram is still usable without it.
      if (source && target) {
        edges.push({ id, source, target, label: text(cell.getAttribute('value')) });
      }
    }
  }
  if (nodes.length === 0) {
    throw new DiagramError('The topology diagram has no boxes to draw.');
  }

  const left = Math.min(...nodes.map((n) => n.x));
  const top = Math.min(...nodes.map((n) => n.y));
  const right = Math.max(...nodes.map((n) => n.x + n.width));
  const bottom = Math.max(...nodes.map((n) => n.y + n.height));
  return {
    nodes,
    edges: edges.filter(
      (e) => nodes.some((n) => n.id === e.source) && nodes.some((n) => n.id === e.target),
    ),
    extent: {
      x: left - PADDING,
      y: top - PADDING,
      width: right - left + PADDING * 2,
      height: bottom - top + PADDING * 2,
    },
  };
}

/** draw.io labels may carry HTML and use &#10; for line breaks. */
function text(value: string | null): string {
  if (!value) return '';
  return value
    .replace(/<br\s*\/?>/gi, '\n')
    .replace(/<[^>]+>/g, '')
    .trim();
}

function number(value: string | null): number {
  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : 0;
}
