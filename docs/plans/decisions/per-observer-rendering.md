# Decision — per-observer rendering

**Status:** open, adopted-in-principle (the *approach* is sanctioned; the *architecture* is the grill). Deliverable is an ADR.

## The decision

Several features mean **each client sees a different world** in a server-authoritative game:

- self-invisibility (the owner or their target renders differently to different peers),
- a health drop visible only to the player who can pick it up,
- a smoke cloud that blocks *other* players' sight but not the emitter's.

Decide **how per-observer visibility is expressed** and **how it stays server-authoritative** — the server remains the source of truth for gameplay state; only *rendering* diverges per observer.

## Why it's genuinely new

Unlike a debuff, this is not a timed victim-side modifier — it's a replication/visibility question. It was the one item in the ability batch that couldn't be re-read as "a consumer of the debuff channel." That's why it earns a design session of its own rather than riding an existing mechanism.

## Candidates

- **Owner-scoped renderer toggles** — the entity decides per-observer whether to draw itself, driven by server-known state (who owns it, who's inside the cloud). Engine-native, no new channel.
- **A visibility channel** — a dedicated mechanism that publishes per-observer visibility sets. Heavier; only earns its keep if renderer toggles can't express the cases.

Bias (per Elegance): reach for the engine-native toggle first; promote to a channel only on a case the toggle can't express.

## What rides on it

All three [per-observer features](../features/per-observer-features.md): invisibility (P3), smoke (P7), see-only-to-me health (A5). Note two of them are *also* essence-shallow (invisibility as a closed self-loop, smoke composing with nothing) — so this decision can be **designed** ahead of time, but nothing is **built** until a feature earns it on the essence.

## What this is NOT

- Not screen-blind. A blind is a timed debuff on the victim's *own* screen — see [timed-modifier lifetime](timed-modifier-lifetime.md).
- Not AI vision. `BotVisionMath` is targeting geometry, a separate decision.

## Trigger

The first entity that must be visible to some clients and not others. Decide the architecture in this session; hold the implementation until a feature clears the essence gate.
