# Proposal

## Why

`/topology` is hard to read. Its 31 edges are straight lines between box centres, and the node layout makes many of
them cross. Each label sits at the exact midpoint of its line, with no background and no knowledge of the others.
So labels land on top of each other ("/jaeger" over "OTLP", "/code/mcp via lb" over "index admin", "embed" over
"gRPC") and on top of lines. The page is the live picture of the stack, and it is also a README screenshot.

## What Changes

- **Labels are placed, not dropped at the midpoint.** After all edges are routed, each label is put at the first
  point along its own line (the middle first, then further out on either side) where it overlaps no other label
  and no box. If no such point exists, it goes where the overlap is smallest.
- **Labels are readable over lines.** Each label gets a halo in the canvas colour, so a line passing under it does
  not strike through the text. Labels are drawn above the boxes, so a label on a short line with no free spot,
  such as api → mcp-portfolio, is still read instead of hidden.
- **A cleaner layout in `docs/topology.drawio`.** Nodes are rearranged in layers: entry → application → the three
  MCP servers → backing services, with observability along the bottom. The positions were chosen by scoring
  candidate layouts on lines through boxes, crossings and length, using the renderer's own routing. Every node and
  edge is kept, and so are the labels removed earlier for `mcp-code`.

## Capabilities

### New Capabilities
<!-- None. -->

### Modified Capabilities
- `web-ui`: the Topology screen requirement adds legible, non-overlapping edge labels.

## Impact

- `web/src/topology/layout.ts` (a `placeLabels` step), `TopologyDiagram.tsx`, `Topology.module.css`, `layout.test.ts`.
- `docs/topology.drawio` (x/y only, plus the two restored labels). The `No_two_boxes_in_the_diagram_overlap` and
  node-set tests keep guarding the drawing.
- The README topology screenshot is re-taken.
