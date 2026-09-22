import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, expect, it } from 'vitest';
import { DiagramError, parseDiagram } from './parseDiagram';

// The committed drawing itself, so the parser is tested against what the page actually loads.
const committed = readFileSync(resolve(__dirname, '../../../docs/topology.drawio'), 'utf8');

describe('parseDiagram', () => {
  it('reads the committed diagram', () => {
    const diagram = parseDiagram(committed);

    expect(diagram.nodes.map((n) => n.id).sort()).toEqual([
      'api',
      'chat-provider',
      'compliance',
      'jaeger',
      'lb',
      'mcp',
      'ollama-embeddings',
      'otel-collector',
      'prometheus',
      'qdrant',
      'web',
    ]);
    const api = diagram.nodes.find((n) => n.id === 'api')!;
    expect(api.label).toBe('api\nAgent Framework');
    expect(api.width).toBeGreaterThan(0);
    expect(diagram.nodes.find((n) => n.id === 'chat-provider')!.dashed).toBe(true);
    expect(diagram.edges).toContainEqual(expect.objectContaining({ source: 'api', target: 'mcp' }));
    // The extent covers every box with a margin, so it can be used as a viewBox.
    expect(diagram.extent.width).toBeGreaterThan(
      Math.max(...diagram.nodes.map((n) => n.x + n.width)) - diagram.extent.x - 1,
    );
  });

  it('drops edges whose ends are not drawn', () => {
    const xml = committed.replace(/<mxCell id="qdrant".*?<\/mxCell>/s, '');
    const diagram = parseDiagram(xml);

    expect(diagram.nodes.map((n) => n.id)).not.toContain('qdrant');
    expect(diagram.edges.some((e) => e.target === 'qdrant')).toBe(false);
  });

  it('rejects a compressed diagram with a readable message', () => {
    const compressed =
      '<mxfile host="app.diagrams.net"><diagram id="x" name="Page-1">7VpZc+I4EP41PCblA0=</diagram></mxfile>';

    expect(() => parseDiagram(compressed)).toThrow(DiagramError);
    expect(() => parseDiagram(compressed)).toThrow(/uncompressed/);
  });

  it('rejects text that is not a diagram', () => {
    expect(() => parseDiagram('not xml at all <<<')).toThrow(DiagramError);
  });
});
