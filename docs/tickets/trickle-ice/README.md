# Trickle ICE — tickets

Slices of the plan in [`docs/plans/trickle-ice.md`](../../plans/trickle-ice.md), which stays the
source of truth for file-level detail (opcode table, per-file work items, flags to resolve).
Decisions: ADR [0010](../../adr/0010-trickle-ice-sdp-only.md) (full trickle, SDP-only),
[0011](../../adr/0011-signaling-session-routing.md) (server-owned signaling sessions),
[0012](../../adr/0012-join-patience-pac-timer.md) (39.5s patience).

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

## Known green-gap

Between 03 and 04, a **cross-transport** join (editor/standalone host ↔ WebGL browser client)
does not connect: the standalone side has stopped bundling candidates into the SDP and the jslib
side has not yet learned to trickle. Same-transport joins stay green throughout — standalone
↔ standalone via 03, WebGL ↔ WebGL via the untouched bundled path. Land 04 promptly after 03.
