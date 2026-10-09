## Context

User feedback and turn persistence are core state; the optional administrator review workflow is an app plugin.
The move preserves tenant authorization, queue ordering and limits, JSON shapes, stable dataset ids and labels.

## Decisions

Use ports and adapters: `IFeedbackReviewStore` reads and writes the core store from the validated principal, exposing
purpose-built review DTOs rather than EF entities. Chunk lookup stays behind the core tenant-scoped maintenance
service. The plugin owns queue projection, row construction and the two administrator routes. `IAppendEvalDataset`
is the optional exporter port; its existing Null Object lets labels persist without a developer exporter.

The web contributes its existing route and navigation through `definePlugin`. Generic plugin-api exports let it
render installed review panels inside the same contribution boundary. User feedback submission remains core.

## Verification

Move workflow tests with the plugin. Check the present and removed folder configurations with local builds,
fixture tests and docs/spec validation. Full stack CI, indexing and live evals follow all planned code migrations,
per the owner's sequencing decision.
