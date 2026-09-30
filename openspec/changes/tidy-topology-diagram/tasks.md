# Tasks

## 1. Labels

- [x] 1.1 Add `placeLabels` to `web/src/topology/layout.ts`. It takes every edge's route and label text, and gives each label the first point along its polyline (fractions 0.5, 0.4, 0.6, 0.3, 0.7, 0.2, 0.8 of its length) whose box overlaps no placed label and no node, or the least-overlapping point. Use it in `TopologyDiagram`. Verify: `layout.test.ts` covers two crossing edges whose midpoints coincide ending with disjoint labels, and a label that would sit on a box moving off it
- [x] 1.2 Give edge labels a halo in the canvas colour (`paint-order: stroke`). Verify: labels read cleanly over lines in both themes on `/topology`

## 2. Layout

- [x] 2.1 Rearrange `docs/topology.drawio` in layers from a scored layout search, keep every node and edge, and restore the `embed` and `OTLP` labels on mcp-code's edges. Verify: the .NET diagram tests (same node set, no overlapping boxes) and `parseDiagram.test.ts` pass

## 3. Verification

- [x] 3.1 `make test-web`, `make lint-web` and the topology .NET tests pass
- [x] 3.2 `/topology` shows no overlapping labels. Re-take the README image with `make screenshots SHOTS=topology`. Verify: inspect both theme captures
- [x] 3.3 `openspec validate tidy-topology-diagram --strict` passes
