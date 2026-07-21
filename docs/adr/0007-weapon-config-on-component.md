# ADR 0007 — Weapon firing config lives on the PawnWeapon component, not a WeaponData SO

- **Status:** Accepted (2026-07-15)
- **Date:** 2026-07-15

## Context

Firing behaviour was described in two places at once. `WeaponData` (a `ScriptableObject`) declared the 8 firing fields — `FireMode`, `FireRate`, `AmmoPerFire`, `ShotsPerFire`, `FireDuration`, `ProjectilesPerShot`, `SpreadAngle`, `SpreadType` — and the pure core carried the *same* 8 in `WeaponConfig`. `PawnWeapon.Update` hand-copied every field from the SO into a fresh `WeaponConfig` **every frame** to call the processor (`PawnWeapon.cs:38–48`). `WeaponData` was thus a *second* "kind" authority sitting beside the committed type-object doctrine (an identity-only SO + an independent prefab copy that carries all config — the shape `ProjectileType` already follows). `WeaponDatabase` (a `Resources`-loaded singleton indexing WeaponData assets by alphabetical order) was the runtime lookup for that authority. This is modularity item **W2** from `docs/plans/parnas-modularity-plan.md`: the firing-config *format* was known in two modules, so a change touched both.

## Decision

**Firing config lives on the `PawnWeapon` component as a serialized `WeaponConfig` struct plus a `ProjectileType` reference (and a `spawnPositionOffset`).** `WeaponData` and `WeaponDatabase` (and its editor generator) are **deleted**. `PawnWeapon.Update` passes the serialized `config` straight to `WeaponProcessor.Process` / `GetEffectiveSpreadAngle` — no per-frame copy — and `ExecuteShot` reads `config.ProjectilesPerShot` / `config.SpreadType` / `spawnPositionOffset` and spawns `projectile.InstancePrefab`.

One authority owns the firing format; the projectile a weapon fires is the same identity-only `ProjectileType` SO used everywhere else. This restores the type-object doctrine: config lives on an independent prefab copy (the `PawnWeapon` on each base prefab), self-contained per prefab, with no second "database of kinds" mechanism.

## Alternatives rejected

- **Keep `WeaponData` as a second type-object authority beside `ProjectileType`.** Rejected: two competing "kind" mechanisms for one concept. Every firing-format change edits both the SO and `WeaponConfig`, and the doctrine already names `ProjectileType` as *the* kind authority. Elegance: collapse two mechanisms to one.
- **Put firing config on `ProjectileType`.** Rejected: `ProjectileType`'s role is deliberately identity-only (see the type-object doctrine) — the config lives on the independent prefab copy, not the SO. Loading firing fields onto the SO would violate that split and couple "which projectile" to "how this weapon fires it" (two weapons firing the same projectile at different cadences could no longer share the type).

## Evidence

- Recon (`unity-scout`): only **`PawnWeapon`, `CombatTestRig`, `WeaponDatabaseGenerator`, `CombatPlayModeTestFixture`** referenced `WeaponData`/`WeaponDatabase` in code — no scene carries a `PawnWeapon`.
- Only **2 `WeaponData` assets** existed, on **2 base prefabs** (`Pawn Root`, `TestShooter`) — a per-deliberate-prefab-copy count, not a data-driven roster. `WeaponDatabase` was **runtime-dead** (no live `Instance` consumer on the fire path).
- `WeaponData.WeaponName` and `WeaponData.WeaponPrefab` were **read by no consumer** — dropped, not migrated.
- The config was duplicated only to be hand-copied field-by-field every frame — pure format duplication paying no flexibility dividend.

## Consequences

- Two base prefabs (`Pawn Root`, `Tests/PlayMode/Prefabs/TestShooter`) are re-authored: their `PawnWeapon` gains the serialized `config` + `projectile` + `spawnPositionOffset`, and the three now-orphaned assets (`Default Weapon.asset`, `TestWeapon.asset`, `WeaponDatabase.asset`) are deleted — a separate editor/MCP step, since prefab and `.asset` edits cross the GUID boundary.
- The PlayMode weapon tests are the proof the behaviour is unchanged: `WeaponSpreadTests` (overrides spread via the mutated `config` struct) and the bot-fire cadence/damage tests stay green.
- `CombatTestRig.OverrideWeaponSpread` now mutates the boxed `WeaponConfig` value type via reflection instead of cloning a WeaponData SO; the dead `SetWeapon(path)` seam is gone.
- **Cheap to break:** if a data-driven weapon roster is ever genuinely needed (multiple weapons swappable at runtime from a table), the superseding path is one ADR reintroducing a *kind* SO — but only when a second real case demands it (rule of two), not by default.

**North-star rationale.** *Elegance:* collapse two "kind" mechanisms into one authority and delete a dead singleton + its generator, adding zero mechanism. *Essence:* restores the committed type-object doctrine so the firing format has a single home.
