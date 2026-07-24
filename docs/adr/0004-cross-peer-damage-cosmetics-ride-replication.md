# ADR 0004 — Cross-peer damage cosmetics ride the replicated health value, not a broadcast

- **Status:** Accepted (2026-07-15)
- **Date:** 2026-07-15

## Context

The weapon/health refactor moved damage onto the sibling `Health` — the ONE `IDamageable`. Its C# events `OnDamageTaken`/`OnDeath` fire in-process, and the standard projectile-hit path resolves server-only, so on a dedicated server no client ever ran them — remote players saw no hit-spark or death-burst. The class doc's claim that the events "fire on EVERY peer" was aspirational, not true. `PawnDamageVfx`, the one cosmetic consumer, subscribed to those events.

## Decision

**Cross-peer damage COSMETICS ride the replicated `CurrentHealth` SyncVar `OnChange` — the pattern `PawnColor` already uses — not a broadcast.**

1. `PawnDamageVfx` subscribes to `pawn.Health.OnChange`: burst on a decrease, death burst at `next <= 0`, gated on **peer-level client presence** (`if (!pawn.IsClientStarted) return`) so a headless dedicated server renders nothing. A `ServerOnly` SyncVar's `OnChange` fires exactly ONCE per change on any given peer, so no dedup is needed — and the gate must NOT test `asServer`: doing so suppresses the host entirely (a trap this work hit).
2. The C# `OnDamageTaken`/`OnDeath` events **stay** as server-side, full-`DamageInfo` notifications for server-side reactions and kill attribution (`PawnManager` reads `OnDeath`). They are no longer advertised as an all-peer predictive surface.
3. The never-reached client-predictive `OnDeath` branch in `TakeDamage` is **deleted**, and the doc corrected.

## Alternatives rejected

- **ObserversRpc in `Health.TakeDamage`:** would land remote sparks at the exact impact point, but introduces the first `ObserversRpc` in combat (no precedent), spends per-hit bandwidth on the ONE health system shared by every damageable, and re-opens the host double-fire hazard — all to buy impact-point precision on a *cosmetic*. Per the Essence guard, hit-spark position is not where this project spends a networking pattern.
- **Client-predictive collision:** the only option that would light up the predictive `OnDeath` branch (which is precisely why that branch is dead and deleted) — a far larger change, entangled with which-peer-simulates and FishNet prediction, for a cosmetic outcome.

## Evidence

- `unity-scout` map: `PawnDamageVfx` is the **only** cosmetic consumer of the events, reading only `HitPoint` + `Direction`. `PawnColor` already flashes on every peer off `CurrentHealth.OnChange` with zero networking machinery — the precedent this decision reuses (though its handler is idempotent; `PawnDamageVfx` *spawns* per callback, so it cannot copy blindly).
- **FishNet source, not assumption** (`SyncVar.cs`): a server write invokes `OnChange` once with `asServer: true`; the `asServer: false` path runs only on a network deserialize, which a clientHost never does for its own writes. So the existing `if (asServer) return` idiom (`DamageIndicator`/`NetworkUIController`) silently no-ops on a listen-server host — acceptable for remote-client UI, wrong for VFX that must also render on the host.
- **Peer-level, not object-level, gate** (proven by a diagnostic run): the combat rig spawns combatants server-only, so an object-level `IsClientInitialized` gate blocked every burst on the fixture; `IsClientStarted` is the correct discriminator, and the only one exercisable on the single-host rig.
- No `ObserversRpc` exists anywhere on the hit path, and the client-predictive `OnDeath` branch had **no caller**.

## Consequences

- Remote clients gain hit/death bursts that were entirely absent; a headless server renders none — correct. **The host regresses** from impact-point sparks to pawn-centered bursts — accepted, and consistent with the whole-body colour flash; the synced value carries no impact point.
- `DamageVfxTests` pins one burst per hit / exactly one per death and catches a mis-gated port — including gating on `asServer`, which the test caught before merge.
- The `Health` C# events narrow to honest server-side notifications; the dead predictive branch and the "every peer" doc claim are removed.
- **Cheap to break:** if impact-accurate remote sparks ever become a game-feel priority, the superseding path is a *scoped* `ObserversRpc` carrying `HitPoint` + `Direction` — one superseding ADR, no wider rework.

**North-star rationale.** *Elegance:* reuse replication, add zero mechanism, and delete dead machinery rather than guard it. *Essence:* cosmetic fidelity is not an upgrade-driven system interaction, so it does not earn a networking pattern.
