# Feature — per-observer features (invisibility · smoke · see-only-to-me health)

**Blocking decision:** **[per-observer rendering](../decisions/per-observer-rendering.md)** — grill that first. **Ship gate (smoke only):** terrain-zone playtest. Two of these three are **essence-shallow → last regardless.**

## What they are (grouped by the decision they all ride)

- **Invisibility** — become invisible after landing a hit (or for a duration). A self-buff render toggle.
- **Smoke cloud** — a cloud that blocks *other* players' sight but not the emitter's.
- **See-only-to-me health drop** — a health pickup visible only to the player who can grab it.

All three mean **each client sees a different world** — the per-observer rendering decision owns *how* that's expressed and stays server-authoritative. None is buildable until that decision lands; the decision can be *designed* now.

## Essence gate — read carefully

| Feature | Essence verdict |
|---|---|
| **Invisibility** | Passes the scope guard but is a **closed self-loop** — invis-after-hit changes the owner's play, writes no upgrade×upgrade fold. **Re-pitch to compose** (e.g. invis that interacts with an enemy's perception upgrade) or accept as a shallow-but-legal gadget. Shallow → last. |
| **Smoke** | Passes but **composes with nothing** and is the heaviest to build. It was the batch's rule-of-two prop for the per-observer decision. Shallow → last. |
| **See-only-to-me health** | **Revivable** — its two original cut-reasons are gone (per-observer adopted; map/resource control un-excluded). Re-pitch against the interaction test before committing. |

The honest position: these are **gated on the essence, not on dispatch cost.** Under the current hit model the plumbing they'd need is nearly free — so the *only* reason they wait is that they write no deep interaction. That's a stricter gate, not a looser one. If a *composing* version is pitched, it ships on its own merits.

## Notes

- **Blind is not here.** A screen-blind darkens the victim's *own* screen — a timed debuff (see [goop-slow & blind](goop-slow-and-blind.md)), not per-observer rendering. Only smoke (blocking *others'* sight) is per-observer.
- **Smoke's terrain question** — like goop, a persistent cloud is a world-space volume; settle terrain-vs-emitter-attached-aura empirically before building the volume. Recommend the emitter-attached aura first (zero new mechanism).
- **`BotVisionMath` is not this.** AI targeting geometry ≠ client rendering. Do not fuse.

## Open questions to grill

1. **Renderer toggles vs. a visibility channel** — settled in the [decision](../decisions/per-observer-rendering.md).
2. **Which of the three, if any, is worth building first** — depends on whether a *composing* re-pitch lands. **Recommend:** design the architecture via the decision; build nothing until one clears the essence gate.

## Proof

Per feature, PlayMode with two clients: assert the entity renders differently to each observer while gameplay state stays server-authoritative and identical.
