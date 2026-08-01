# Trickle ICE — follow-ups (superseded)

All fifteen follow-ups this feature surfaced were walked in a `/grill-with-docs` session on
2026-07-31 and rehomed. Nothing is tracked in `.scratch` any more — this directory is disposable and
two of these items were standing invariants that must not disappear with it.

| Items | Now lives in |
|---|---|
| F1, F2, F7, F8, F10–F15 — the agreed work | [`docs/plans/trickle-ice-follow-ups.md`](../../docs/plans/trickle-ice-follow-ups.md) — input to `/to-spec` |
| F3, F4, F5, F6 — deliberately unfixed | [`docs/known-issues.md`](../../docs/known-issues.md) |
| F9 — the headless WebGL join harness | [`docs/plans/tooling/webgl-join-harness.md`](../../docs/plans/tooling/webgl-join-harness.md) |
| F13 — the eight join-phase publish sites | Closed without action; the corrected assembly facts are in the plan above |

Three claims in the original text were wrong and are corrected in the new homes: the send thread's
teardown is redundant rather than missing (seven main-thread callbacks already do it), the join
suite does have a duration bound (30s, just above the regression it should catch), and
`SignalManager` is **not** in the vendored `CanoeWebRTC` assembly.
