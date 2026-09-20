import { describe, expect, it } from 'vitest';
import { routeEdge } from './layout';
import type { DiagramNode } from './parseDiagram';

const box = (id: string, x: number, y: number): DiagramNode => ({
  id,
  label: id,
  x,
  y,
  width: 160,
  height: 80,
  dashed: false,
});

// The stack's own row: lb, api, mcp, qdrant side by side, with a second row beneath.
const api = box('api', 470, 290);
const mcp = box('mcp', 700, 290);
const qdrant = box('qdrant', 930, 290);
const chat = box('chat-provider', 470, 470);

describe('routeEdge', () => {
  it('runs straight between neighbours, stopping at both borders', () => {
    const { points } = routeEdge(api, mcp, [api, mcp, qdrant, chat]);

    expect(points).toHaveLength(2);
    // It leaves the api's right edge and arrives at the mcp's left edge, never crossing into either box.
    expect(points[0].x).toBe(api.x + api.width);
    expect(points[1].x).toBe(mcp.x);
    expect(points[0].y).toBe(330);
  });

  it('goes around a box in the way rather than under it', () => {
    // api → qdrant passes straight through mcp-retrieval, which used to hide the line completely.
    const { points, label } = routeEdge(api, qdrant, [api, mcp, qdrant, chat]);

    expect(points.length).toBeGreaterThan(2);
    expect(points.every((p) => p.y <= mcp.y)).toBe(true);
    expect(label.y).toBeLessThan(mcp.y);
  });

  it('takes the lane below when the one above is occupied', () => {
    const inTheWayAbove = box('above', 700, 180);
    const { points } = routeEdge(api, qdrant, [api, mcp, qdrant, inTheWayAbove]);

    expect(points.every((p) => p.y >= api.y)).toBe(true);
    expect(Math.max(...points.map((p) => p.y))).toBeGreaterThan(api.y + api.height);
  });

  it('puts the label on the line, not inside a box', () => {
    const { label } = routeEdge(api, chat, [api, mcp, qdrant, chat]);

    expect(label.y).toBeGreaterThan(api.y + api.height);
    expect(label.y).toBeLessThan(chat.y);
  });
});
