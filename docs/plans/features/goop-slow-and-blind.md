# Feature — goop-slow & blind (the timed-victim-debuff pair)

**Blocking decision:** **[timed-modifier lifetime](../decisions/timed-modifier-lifetime.md)** — grill that first. **Ship gate (goop only):** terrain-vs-attached playtest (below).

## What it is

Two features that share one channel:

- **Goop-slow** — an area of slowing goop; standing in it slows your movement for a duration.
- **Blind** — a hit briefly darkens the victim's own screen.

They're grouped because they are the **second and third instances** of the timed-victim-debuff pattern — the pair that *earns* extracting the shared channel out of the shipped `HitstunController` shape. Build them feature-first; extract the shared receiver when the third confirms the shape.

## Essence gate

- **Goop-slow** (a): area denial changes both players' positioning; (b) interacts with movement + (open) terrain. Passes.
- **Blind** (a): denies the victim their aim briefly; (b) interacts with the hit pipeline + perception. Passes at the *shallow* end — it's a clean timed debuff, legal but not deeply composing. Fine as a channel-earning instance.

## What already exists

`HitstunController` is already a timed dealer-authored victim debuff (`SyncVar<uint>` expiry tick, resistance fold, per-peer predicate). Goop-slow is the *second* instance of that shape, blind the *third*. The [timed-modifier lifetime](../decisions/timed-modifier-lifetime.md) decision owns the generic `(modifier, expiry)` receiver and the wire-payload; these features are its consumers.

## Notes

- **Blind is local, not per-observer.** It darkens the *blinded victim's own* screen — a timed effect on their client, driven by the timed-modifier channel. It is **not** a per-observer-rendering feature. (Contrast smoke, which blocks *others'* sight — that one is per-observer.)
- **Goop knockback/resist** rides the same resistance-fold shape hitstun uses; the *magnitude* is dealer-authored, so goop is one of the cases that forces the wire-payload sub-decision.

## Goop's own open question — terrain persistence (ship gate)

Goop as imagined is a **persistent world-space volume that ticks overlap** — a genuinely new standing mechanism (explosions are instantaneous). Before building the volume, settle empirically whether terrain-persistence is load-bearing, or whether goop is better as a **victim-attached resistible slow** (no world volume) — judged against the interaction test and "cut a system to make room." **Recommend:** prototype attached-slow first (zero new mechanism); only build the terrain volume if the playtest shows persistence changes play. This is the ship gate for goop; blind has no terrain question.

## Open questions to grill

1. **One generic timed receiver or per-system?** Settled in the [decision](../decisions/timed-modifier-lifetime.md); these features must not each grow their own `Timed<X>Modifiers`.
2. **Slow magnitude on the wire** — how the dealer-authored slow reaches the client-simulated pawn (the wire-payload sub-decision).

## Proof

PlayMode: victim enters goop → movement speed drops for the duration, restores after; each peer agrees on expiry. Blind: hit → victim's screen effect toggles for the window (local, assert on the victim client). Every networked change gets a test.
