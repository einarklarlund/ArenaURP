# Decision — the timed-modifier lifetime (and its wire-payload)

**Status:** open. Grill in its own session → ADR. This is really *one* cluster of tightly-coupled decisions that fire on the same trigger; treat them together.

## The decision

A timed effect — a 3-second slow, a burn, a screen-blind — needs a **third modifier lifetime** that the current system can't host:

- **persistent-until-removed** (a held passive) — exists.
- **consume-once** (a spent charge) — exists.
- **`(modifier, expiry)`** — a stateful receiver owning entries that tick down and self-remove — **missing.**

Two coupled sub-decisions come with it:

1. **The lifetime shape.** A stateful receiver holding `(modifier, expiry)` entries, evaluated per-peer against its own tick. Build it **once, generically** — not a `Timed<X>Modifiers` twin per system (movement, damage, vision each getting their own). What is the one generic host?
2. **The wire-payload.** A *dealer-authored* effect applied to *receiver-simulated* state — e.g. a slow whose magnitude the dealer picks, applied to a client-simulated pawn — **cannot stay payload-free**: the victim's client can't derive what hit it. This is the hit pipeline's first gameplay `TargetRpc` payload. Decide the sanctioned payload shape and write it into `fishnet-conventions` (its Rule 1, "avoid payloads," is *unsatisfiable* for this class — knockback only escapes because its config lives receiver-side).

## Why it's deferred

The stateless effect layer can't own expiry state, and no shipped feature has needed it yet. Building it speculatively would guess the generic shape before two real consumers constrain it — exactly the rule-of-two trap. So it's built **feature-first**: the goop-slow and blind features (below) are the second and third instances of the timed-victim-debuff pattern; extract the shared receiver when the third confirms the shape, not before.

## What exists today

- `HitstunController` is already a *timed dealer-authored victim debuff*: a `SyncVar<uint>` expiry-tick window, dealer-authored duration, a resistance-modifier fold, each peer evaluating its own predicate against its own tick. **This is the shape to generalize** — the debuff channel is one generalization of a shipped component, not a new invention.
- Its config lives receiver-side, which is *why* hitstun needs no wire-payload — and why the movement-steal case, whose magnitude is dealer-authored, is the one that forces the payload decision.

## What rides on it

- **[Goop-slow & blind](../features/goop-slow-and-blind.md)** — a timed slow and a timed screen-blind. These are the *second and third* instances of the `HitstunController` shape; they earn the extraction.
- **[Movement-steal projectile](../features/movement-steal-projectile.md)** — a dealer-authored timed movement debuff; the case that forces the wire-payload sub-decision.
- Any future debuff (burn, poison) and any timed self-buff (see [per-observer](per-observer-rendering.md) for the render-toggle half of invisibility).

## Notes for the grill

- **Debuffs generally.** `ApplyDebuff` / `DebuffType` were placeholder vocabulary with no system behind them (deleted as dead weight in ADR 0002). This decision is where a *real* debuff system, if any, gets designed — as timed victim-side modifiers, not a new dispatch family.
- **Screen-blind is a timed debuff, not per-observer rendering.** A blind darkens the *blinded victim's own* screen — a local timed effect on the victim's client. Keep it here, not in the per-observer decision.
- **`BotVisionMath` is not this.** It's FOV-cone geometry for AI *targeting* — a separate concern from client rendering or debuffs. Do not fuse them.

## Trigger

The first shipped timed buff/debuff (the lifetime), and the first movement-steal-like effect (the wire-payload). Goop-slow is the natural first mover.
