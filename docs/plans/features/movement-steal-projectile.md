# Feature — movement-steal projectile

**Blocking decision:** **[timed-modifier lifetime](../decisions/timed-modifier-lifetime.md)** (both halves — the lifetime *and* the wire-payload). **Ship gate:** none. **Built LAST in the backlog.**

## What it is

A projectile that, on hit, **steals a movement buff** from the victim and grants it to the shooter for a duration — the victim slows, the shooter speeds up, both temporary.

## Essence gate

(a) Drastically changes both players — a tempo swing in a firefight. (b) Interacts with movement, projectiles, and passives. Passes strongly (upgrade×upgrade by construction).

## Why it's last

It forces the **two hardest sub-decisions** at once, which is why it sits behind goop-slow (which forces them more gently):

1. **Timed-modifier lifetime** — the steal is a `(modifier, expiry)` effect on both pawns; it needs the generic timed receiver.
2. **Wire-payload** — the stolen buff's magnitude is **dealer-authored** but applied to **client-simulated** movement. The victim's client can't derive what hit it, so this is the case that *forces* the sanctioned `TargetRpc` payload shape. `fishnet-conventions` Rule 1 (avoid payloads) is unsatisfiable here.

## What already exists

- The passive/loadout ref-move ships (ADR 0009): steal = move a `PlayerPassiveLoadout` ref (`Remove` + `Grant`), and `persistAcrossDeath` is the single per-life/per-game knob — the [acquisition & persistence](../decisions/acquisition-and-persistence.md) decision picks its value.
- The on-hit steal shape (`PassiveStealController : IHitMessageHandler`) is the model; a *movement* steal is that shape pointed at a movement modifier with a timer.

## Open questions to grill

1. **Steal vs. copy** — does the victim *lose* the buff or does the shooter get a copy? **Recommend:** steal (loss), for the tempo swing; copy is a weaker, less interactive version.
2. **Duration source** — dealer-authored (feeds the wire-payload) vs. fixed. **Recommend:** dealer-authored, since that's the interesting knob and it's the case the wire-payload decision needs anyway.

## Proof

PlayMode: shooter hits a moving victim → victim's speed drops and shooter's rises for the window, both restore after; each peer agrees via the sanctioned payload (no client-side guessing). Every networked change gets a test — this one especially, as the first gameplay `TargetRpc` payload.
