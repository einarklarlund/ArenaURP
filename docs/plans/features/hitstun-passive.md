# Feature — hitstun passive

**Blocking decision:** none. **Ship gate:** **[hitstun feel](../decisions/hitstun-feel.md)** playtest — build it, hold the release until the base feel is confirmed.

## What it is

A passive upgrade that adds or extends the hitstun your hits inflict — lock enemies out of shooting longer.

## Essence gate

(a) Changes how both players play — the owner controls tempo, the victim loses agency. (b) Interacts with weapons, the hit pipeline, and movement (stun affects input). Passes.

## What already exists

`HitstunController` is the merged dealer+receiver: a `SyncVar<uint>` expiry-tick window, dealer-authored duration, a resistance fold, each peer evaluating `IsStunned` against its own tick. This passive contributes to its dealt-duration seam (`OutgoingModifiers`) — the same shape the [damage-buff channel](../decisions/damage-buff-channel.md) generalizes, though this passive doesn't *require* that channel to exist (a hitstun-duration fold is narrower than damage buffs). Confirm during the grill whether it rides the channel or a local fold.

## Why it's gated (not blocked)

The mechanic is buildable now, but the **base** hitstun tuning is unplaytested and may be oppressive (sustained fire can perma-stun). Shipping a passive that *increases* stun before the base feel is settled risks amplifying a problem. So: build + test the passive, release it behind the hitstun-feel playtest.

## Open questions to grill

1. **Add flat duration or multiply?** **Recommend:** additive to the dealt-duration seam (composes cleanly with the base 0.2s).
2. **Does it interact with the stun cap** (if hitstun-feel introduces one)? **Recommend:** defer — resolve once the cap decision lands; note the dependency.

## Proof

PlayMode: owner with the passive hits a victim → stun window is longer than the un-upgraded baseline; each peer's `IsStunned` agrees on its own tick (no desync). Every networked change gets this test.
