# Trickle ICE — tickets

Slices of the plan in [`docs/plans/trickle-ice.md`](../../../docs/plans/trickle-ice.md), which stays the
source of truth for file-level detail (opcode table, per-file work items, flags to resolve).
Decisions: ADR [0010](../../../docs/adr/0010-trickle-ice-sdp-only.md) (full trickle, SDP-only),
[0011](../../../docs/adr/0011-signaling-session-routing.md) (server-owned signaling sessions),
[0012](../../../docs/adr/0012-join-patience-pac-timer.md) (39.5s patience).

## Status (2026-07-31)

All six tickets are implemented, and everything that could be checked without a human at a running
build has been: the Boot-scene and prefab authoring is done (punch list A1-A4), the EditMode suite
is green (263), and both PlayMode tests that had never been executed now pass, inside a green full
suite (61). A join reaches both data channels open in **0.05s**, against the ~8.05s floor the
bundled-candidate path sat behind.

What is left is manual: punch list T3, T5, T6, T7 — see [`../unity-punch-list.md`](../unity-punch-list.md).
The important one is the WebGL gate below.

## Dependency order

```
01 PlayMode ICE harness ─┐
                         ├─→ 03 Standalone trickle ──→ 04 WebGL trickle
02 Wire contract + queue ┘        (+ ArenaInfra S1–S5)

05 Failure model & patience ──→ 06 Typed join-phase UI
```

01, 02 and 05 have no blockers and can run in parallel. 03 and 04 are the connectivity change;
05 and 06 are the patience/UX consequence of it and touch a different surface.

## External blocker

Ticket 03 cannot connect until the signal server ships the session table and the 0x08/0x09
routes — **ArenaInfra S1–S5**, in that repo's `docs/plans/trickle-ice.md`. Both peers ship in
lockstep; there is no backward-compat window to preserve.

## Known green-gap — closed in code by 04, unproven

Between 03 and 04, a **cross-transport** join (editor/standalone host ↔ WebGL browser client)
did not connect: the standalone side had stopped bundling candidates into the SDP and the jslib
side had not yet learned to trickle. Same-transport joins stayed green throughout — standalone
↔ standalone via 03, WebGL ↔ WebGL via the untouched bundled path.

04 has now moved the jslib across and deleted the bundled path outright, so both transports
trickle and there is only one negotiation shape left. **But nothing has run the WebGL side** — the
two manual gates that would (a two-browser join, and a browser client joining an editor host) are
both outstanding, punch list T6/T7. The gap is closed as written, not as observed.
