# ADR 0004 — Cross-peer damage cosmetics ride the replicated health value, not a broadcast

- **Status:** Accepted (2026-07-15)
- **Date:** 2026-07-15

## Context

The weapon/health refactor moved damage off `Pawn` onto the sibling `Health` — the ONE `IDamageable`. `Health.TakeDamage` raises C# events `OnDamageTaken` / `OnDeath` **in-process, wherever it is invoked**. The standard projectile-hit path resolves **server-only** (`ServerCollision.ServerTick` → `HitResolver` → `Health.TakeDamage`), so on a dedicated server no client ever runs those events and remote players see no hit-spark or death-burst particles; on a host only the host does. The class doc claimed the events "fire on EVERY peer" — aspirational, not true for the server-only path. `PawnDamageVfx` (the one cosmetic consumer) subscribed to those events, so its bursts were lost cross-peer.

## Decision

**Cross-peer damage COSMETICS ride the replicated `CurrentHealth` SyncVar `OnChange` — the pattern `PawnColor` already uses — not a broadcast.**

1. `PawnDamageVfx` subscribes to `pawn.Health.OnChange` instead of the C# events. It bursts on a decrease (`next < prev`), adds the death burst at `next <= 0`, and gates on **peer-level client presence** (`if (!pawn.IsClientStarted) return`) so a headless dedicated server renders nothing. A `ServerOnly` SyncVar's `OnChange` fires exactly ONCE per change on any given peer — `asServer==true` on the listen-server host (which never re-reads its own write, so there is no `asServer==false` twin), `asServer==false` on a remote client — so no dedup is needed, and the gate must NOT test `asServer`: doing so suppresses the host entirely (a trap this work hit).
2. The C# `OnDamageTaken` / `OnDeath` events **stay** as server-side, full-`DamageInfo` notifications for server-side reactions and kill attribution (`PawnManager` reads `OnDeath` for the attacker). They are no longer advertised as an all-peer predictive surface.
3. The never-reached client-predictive `OnDeath` branch in `TakeDamage` is **deleted**, and the doc corrected. (This subsumes fix C's gate — the double-firing branch is gone, not merely guarded.)

## Alternatives rejected

- **ObserversRpc in `Health.TakeDamage` (replicate the damage event).** Would carry the full `DamageInfo` (`HitPoint` + `Direction`) to observers, so remote sparks land at the exact impact point. Rejected: it introduces the **first `ObserversRpc` in combat** (no precedent — the only combat RPC is a `TargetRpc` to the owner for knockback), spends per-hit bandwidth on the ONE health system shared by every damageable, and re-opens the host double-fire hazard fix C just closed — all to buy impact-point precision on a *cosmetic*. Per the Essence guard, hit-spark position is not an upgrade-driven system interaction, so it is not where this project spends a networking pattern.
- **Client-predictive collision.** Clients run the hit path locally so `TakeDamage` is genuinely invoked on every peer. Rejected: far larger change, entangled with the which-peer-simulates rule and FishNet prediction, for a cosmetic outcome. It is the *only* option that would light up the predictive `OnDeath` branch — which is precisely why that branch is dead today and is deleted here.

## Evidence

- `unity-scout` map: `PawnDamageVfx` is the **only** cosmetic consumer of the events, and it reads only `DamageInfo.HitPoint` + `Direction` (never `Amount` / `Attacker` / `Type`).
- `PawnColor` already flashes on **every peer** off `CurrentHealth.OnChange` with zero networking machinery (`PawnColor.cs:48`) — proof the synced value reaches every peer, and the precedent this decision reuses. `PawnColor` is immune to any callback-count question because its handler sets an idempotent flag; `PawnDamageVfx` *spawns* per callback, so it cannot copy that blindly.
- **FishNet source, not assumption** (`SyncVar.cs`): `CanInvokeCallbackAsServer()` returns `IsServerStarted`, and a server write (`SetValue`, `calledByUser`) invokes `OnChange` once with `asServer: true`; the `asServer: false` path runs only from `Read` (a network deserialize), which a clientHost does not do for its own writes. So a `ServerOnly` SyncVar's OnChange is single-fire per peer. The existing `if (asServer) return` idiom in `DamageIndicator` / `NetworkUIController` therefore silently **no-ops on a listen-server host** (it targets remote clients only) — acceptable for their remote-client UI, but wrong for VFX that must also render on the host, which is why this port gates on client presence instead.
- **Peer-level, not object-level, gate** (proven by a `[VFXDIAG]` run): the combat rig spawns combatants server-only (like bots), so on the clientHost they are `serverInit=True` but `clientInit=False`. An object-level `IsClientInitialized` gate therefore blocked every burst on the fixture (0 spawns, identical to the `asServer` gate — which is how it was diagnosed). `IsClientStarted` (the client *manager* is up on this peer) is `True` on the host/fixture and any real client, `False` on a headless server — the correct discriminator, and the only one exercisable on the single-host rig.
- No `ObserversRpc` exists anywhere in `ServerCollision` / `HitResolver` / the `Pawn*` components / the receivers — option 1 would be a genuinely new pattern.
- The client-predictive `OnDeath` branch had **no caller**: no path invokes `TakeDamage` on a pure client, so it never ran (on a host it only mis-fired, which fix C caught).

## Consequences

- Remote clients gain hit/death bursts that were entirely absent; a dedicated (headless) server renders none — correct.
- **The host regresses** from impact-point sparks to pawn-centered bursts — accepted, and consistent with the existing whole-body colour flash. Bursts play at the pawn transform because the synced value carries no impact point or orientation.
- VFX spawn once per hit on any client-bearing peer (host or remote); the headless server is skipped by the client-presence gate. `DamageVfxTests` pins one burst per hit / exactly one per death and catches a mis-gated port — including gating on `asServer`, which spawns zero on the host (the trap this fix hit, and the test caught before merge).
- The `Health` C# events narrow to honest server-side notifications; the dead predictive branch and the "every peer" doc claim are removed.
- **Cheap to break:** if impact-accurate remote sparks ever become a game-feel priority, the superseding path is a *scoped* `ObserversRpc` carrying `HitPoint` + `Direction` (option 1) — one superseding ADR, no wider rework.

**North-star rationale.** *Elegance:* reuse replication, add zero mechanism, and delete dead machinery rather than guard it. *Essence:* cosmetic fidelity is not an upgrade-driven system interaction, so it does not earn a networking pattern.
