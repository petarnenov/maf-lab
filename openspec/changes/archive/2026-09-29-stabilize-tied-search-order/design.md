# Design

## Context

`TenantScopedSearch.QueryAsync` is the only method that queries Qdrant (enforced by `QueryPathEnumerationTests`). It
passes `limit` to the store and maps the returned points in the order the store gave them. For RRF the store computes
`Σ 1/(k + rank)` over the prefetch branches, so equal scores are exact float equals of the same arithmetic, not near
misses. Qdrant's order among equal scores depends on segment and merge order, which may differ between calls.

## Goals / Non-Goals

**Goals:** a total, repeatable order for the method's result, including which tied points fill the last positions.

**Non-Goals:** changing scores, fusion, the prefetch sizes or the floors; tie-breaking by anything that means
relevance (a tie is a tie: the chunk id is an arbitrary but stable key); fixing the test by loosening it.

## Decisions

1. **Order by (score desc, chunk id asc, ordinal).** The chunk id is unique per point and already in the payload, so
   no extra field is needed. A rule of "ordinal string order" avoids culture-dependent comparisons.
   *Alternative:* sort ties by point id — rejected, point ids are derived and not what traces or tests show.
2. **Over-fetch `limit + TieMargin` (10) and trim after ordering.** Without it, sorting only fixes the order *within*
   the store's cut, and the store alone decides which tied point lands inside. RRF tie groups are mostly pairs (the
   dense-only/sparse-only rank-*r* pair, the (*i*, *j*)/(*j*, *i*) mirror), so a margin of 10 covers them with room to
   spare. It costs ten more payloads per search. The outer limit does not influence the fused scores, so nothing
   else changes. A tie group larger than the margin at the boundary would still be cut by the store; that is
   accepted and documented in code.
   *Alternative:* re-query until the boundary group is complete — rejected as complexity for a case not observed.
3. **A pure `internal static` function (`Settle(points, limit)`)** next to `HybridPlan`, applied to every mode's result
   inside `QueryAsync`. It is unit-testable without Qdrant, and the class still has exactly one public query method.

## Risks / Trade-offs

- [Order among ties changes once, for everyone] → it was never guaranteed; evals compare sets and ranks, and ties sit
  at the same rank either way.
- [Tie group larger than the margin at the cut] → extremely unlikely with RRF; documented in the code.
