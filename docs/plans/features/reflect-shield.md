# Feature — reflect shield

**Blocking decision:** none. **Ship gate:** **essence** — shallow, so it waits. **Last in line regardless.**

## What it is

A shield that **reflects incoming damage back at the shooter** (reflect-only — drop any damage-*block*, which would negate the interaction rather than create one).

## Essence gate — why it waits

Passes the scope guard: (a) changes how an attacker plays (punishes attacking a shielded target), (b) interacts with the hit pipeline. **But it writes no upgrade×upgrade fold** — it's a victim-side reaction that doesn't deepen the composition P1's combo or P2's splash do. So it's **legal but shallow → last**.

Note the honest framing: this is an **essence gate, not a dispatch-cost gate.** Under the current hit model a reflect *is* an `IHitMessageHandler` component; as a lone victim-side responder it costs one direct call at the hit site. There is no "build it cheap as a one-off vs. adopt a whole channel" trade — the one-off and the general version are the same code. So no architectural excuse defers it; it waits purely because it doesn't compose.

**When R2 fires:** reflect and invis-after-hit are *two* unrelated victim-side responders. If both are ever wanted, the second one mechanically promotes the shared victim-hit-received event to a message — no design session, just the handlers you were writing anyway. That promotion is a side effect, not a reason to build reflect early.

## If it graduates

The way to move reflect off the bench is to **re-pitch a composing version** — e.g. a reflect whose reflected damage scales with a damage passive, or that interacts with the attacker's own upgrades — so it earns the deep interaction the essence rewards. Absent that, accept it as a shallow gadget shipped whenever there's slack.

## Open questions to grill (only once it graduates)

1. **Reflect fraction** — full or partial? **Recommend:** partial, so it's a deterrent not a hard counter.
2. **Does reflected damage carry the shooter's own buffs?** Ties into the [damage-buff channel](../decisions/damage-buff-channel.md).

## Proof

PlayMode: attacker hits a reflecting victim → attacker takes the reflected fraction, victim takes the remainder (or none, per the fraction decision).
