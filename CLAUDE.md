# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

Unity 6 (`6000.3.7f1`) URP arena shooter. Multiplayer is server-authoritative via **FishNet** with two transports: **Tugboat** (UDP, standalone) and **CanoeWebRTC** (WebGL via WebRTC + SimpleWebTransport signaling). Solution file: `ArenaURP.slnx`.

Unity Editor path used by scripts: `/Applications/Unity/Hub/Editor/6000.3.7f1/Unity.app/Contents/MacOS/Unity`.

## Build & Run

Builds are driven from `Assets/Scripts/Editor/BuildScripts.cs`:

- **WebGL dev build (menu)**: `Build > Build WebGL Development` (uses `BuildScripts.BuildWebGLDevelopment`, output: `builds/webgl`).
- **WebGL dev build + local server**: `./deploy_local.sh` (closes Unity if running, builds via `-executeMethod BuildScripts.BuildWebGLDevelopment`, then `npx http-server -p 8080`). Use `-s` to skip the build step and just serve.
- **Standalone build (CLI)**: `Unity -quit -batchmode -projectPath . -executeMethod BuildScripts.BuildProject -buildPlatform <linux|mac|windows> [-development] [-profiler]`. Linux uses the dedicated-server subtarget; mac/windows use the player subtarget. Outputs go to `builds/<platform>_<server|client>/`.
- **CI**: `.github/workflows` runs `game-ci/unity-builder@v4` for WebGL and deploys `build/WebGL/WebGL` to `gh-pages` on every push to `main`.

ParrelSync is installed for multi-instance editor testing of networked play.

## Tests

- Edit-mode NUnit tests live in `Assets/Tests/EditMode/` under the `Arena.Tests` asmdef (Editor-only, `nunit.framework.dll`).
- Run via Unity's **Test Runner** window (Window > General > Test Runner) or CLI: `Unity -batchmode -runTests -testPlatform editmode -projectPath .`.
- Tests target the **pure `Arena.*.Core` assemblies** (Movement, MovementEffects, Knockback, Weapon, Temperature, Bot) — they construct config/state/input structs directly and assert on the returned state. No GameObject lifecycle, no FishNet, no PlayMode tests.

## Architecture: the Processor + thin-MonoBehaviour pattern

This is the most important convention in the codebase. Almost every gameplay system is split in two:

1. **`Arena.<System>.Core` assembly** (pure C#, no `UnityEngine` engine references beyond math types where needed): contains four sibling files — `<X>Config`, `<X>State`, `<X>Input`, and a static `<X>Processor` exposing `Process(state, in config, in input, deltaTime) -> newState`. State flows in and out as structs; the processor has zero Unity lifecycle dependencies. This is what `Arena.Tests` consumes.
2. **`Pawn<X>` `NetworkBehaviour`** in `Assets/Scripts/Pawn/<X>/`: thin wrapper that reads serialized inspector references, builds an `<X>Input` from `PawnInput.Data` each `Update`, calls `<X>Processor.Process`, and applies the resulting state to Unity components (`CharacterController`, transform, etc.).

Examples: `MovementProcessor` ↔ `PawnMovement`, `WeaponProcessor` ↔ `PawnWeapon`, `TemperatureProcessor` ↔ `PawnTemperature`, `KnockbackProcessor` ↔ `PawnKnockback`, `MovementEffectsProcessor` ↔ `PawnMovementEffects`. **When adding gameplay logic, put it in the Core processor and write an EditMode test for it; only put Unity glue in the `Pawn<X>` MonoBehaviour.**

Bot AI follows the same split: `BotBrainLogic` / `BotStateMachine` / `BotInputMath` / `BotVisionMath` (pure, in `Arena.Bot.Core`) ↔ `BotBrain` (server-only `NetworkBehaviour` that wires NavMeshAgent + `Sensor` + `BotPawnInput`).

### Cross-system communication: the Modifier list pattern

Systems don't reference each other directly. Instead, `PawnMovement` and `PawnWeapon` each expose a public `List<MovementModifier>` / `List<WeaponModifier>`. Other systems (e.g. `PawnTemperature`) push modifiers in and pull them out as their state changes (`movement.Modifiers.Remove(prev); movement.Modifiers.Add(next);`). The processor folds all current modifiers into the per-tick simulation. Knockback uses a sibling `PendingImpulses` list that the movement processor consumes and `PawnMovement.Update` clears.

### Pawn composition

`Pawn` (`Assets/Scripts/Pawn/Pawn.cs`) is the network identity hub: `SyncVar<NetworkPlayer> ControllingPlayer`, `SyncVar<int> Health`, damage events. It deliberately knows little — sibling components (`PawnMovement`, `PawnWeapon`, `PawnInput`, `PawnTemperature`, `PawnInventory`, `PawnAmmo`, `PawnKnockback`, `PawnInteraction`, …) live on the same prefab and reference each other through serialized fields. `PawnInput` has two implementations behind one base: `HumanPawnInput` (client-owned) and `BotPawnInput` (server-owned, written by `BotBrain`). All `Pawn<X>` updaters early-out on `if (!input.IsActive) return;` so the same prefab works for both.

### Match orchestration

Server-only flow lives in `Assets/Scripts/Managers/`:

- `MatchMediator` is the central server-side wiring point — it subscribes `NetworkPlayerManager`, `PawnManager`, `MatchFlowManager`, and `DeathmatchManager` to each other. New cross-manager events should be wired here, not inside the managers themselves.
- `MatchFlowManager.State` is a `SyncVar<MatchState>` (`Pregame` / `During` / `Postgame`) — drive lifecycle changes by mutating it on the server.
- UI events are split: `LocalUIEvents` (purely local actions/HUD) and `NetworkUIEvents` (events that originate from synced state).

### Bootstrap / build stages

`Assets/Scripts/Networking/Bootstrap.cs` runs in the Boot scene and selects between **Local / Dev / Prod** flows based on `EnforceBuildStage` (with sensible `UNITY_EDITOR` / `!UNITY_EDITOR` defaults). It also swaps the FishNet transport: `Tugboat` for editor/standalone, `CanoeWebRTC` for WebGL builds. `RoomManager` handles WebRTC room creation/join. **Do not assume the same transport is active in editor vs. WebGL builds.**

Scenes: `Boot.unity` (entry) → `Offline.unity` / `Online.unity` / `Debug.unity`.

## Conventions worth knowing before editing

- The "logic in Core, glue in Pawn" rule above is enforced by asmdef boundaries — `Arena.<X>.Core` cannot reference UnityEngine wrappers, FishNet, or other gameplay code. If you need a new dependency in a processor, prefer adding a field to its `Config`/`Input` struct over adding an asmdef reference.
- Server-authoritative: any new gameplay state mutation should run inside a `[Server]` method or be triggered from one. Client-side prediction exists for projectile spawns (see `Networking/BulletPredictedSpawn.cs`) — follow that pattern rather than inventing a new one.
- `Pawn` and `NetworkPlayer` are reused for **both human and bot players** (`NetworkPlayer.IsBot`). Don't add code paths that assume a client connection on a player.
- **Abilities are instance prefabs.** An `AbilityType` SO is identity only (display name, icon, `InstancePrefab`); granting one spawns its prefab and parents it under the pawn's NetworkObject via `NetworkObject.SetParent` (never raw `transform.SetParent` — only SetParent replicates the hierarchy). Gameplay config/state lives on the prefab's `ActiveAbility` / `PassiveAbility` components (`UsesRemaining` is a SyncVar). `PawnActiveAbilities.Actives` is a `SyncList<ActiveAbility>` ROSTER of instance references (slot order + change hooks); the ordering is load-bearing: **Spawn before Add, RemoveAt before Despawn** (an unspawned ref serializes as permanent null; removal-first keeps deltas off dead objects and drives the HUD). Passives have no removal path, so `PawnPassiveAbilities` derives membership from child enumeration. No AbilityDatabase index sync — identity resolves locally via the instance's `TypeObjectRoot`.
- **Spawning anything a player can own (projectiles, ability children): stamp `NetPlayerOwnership.SpawnOwnerId` yourself.** There is no spawn factory — the sequence is `GetPooledInstantiated` at the spawn pose, set `SpawnOwnerId` (the firer's `NetworkPlayer.ObjectId`), then `ServerManager.Spawn`. The id must be set *before* `Spawn` because it rides the spawn payload (`WritePayload` runs after the OnStart callbacks inside the `Spawn` call). It cannot be self-derived by the component: bots have no owning connection, so `NetworkObject.Owner` won't stand in for it. Everything else self-configures — `TypeObjectRoot` applies type config at Awake, and `NetworkedMotion` derives its tick/pos/forward anchor in `OnStartNetwork` from the instantiate pose. Current spawn sites: `PawnWeapon.ExecuteShot` and the `SpawnNetworkObjects` effect; keep new ones to that shape.

## Unity MCP

A project-aware Unity MCP server is available (tools named `mcp__unity__*`, schemas loaded on demand via `ToolSearch`). Prefer it over `find`/`grep` for Unity-shaped questions where text search is the wrong abstraction:

- "Where is this script/asset referenced?" → `find_script_references`, `find_references` (resolves GUIDs, catches prefab/scene refs that grep misses).
- "What's in this scene/prefab?" → `get_scene_summary`, `get_scene_hierarchy_flat`, `get_prefab_summary`, `get_prefab_dependencies`.
- "What uses this shader / what's in this asmdef / what's broken?" → `list_materials_using_shader`, `list_scripts_by_assembly`, `get_broken_script_refs`, `get_broken_asset_refs`.
- Project-wide listings (`list_prefabs`, `list_scriptable_objects`, `list_all_scenes`, etc.) are faster and more reliable than `find` for typed Unity assets.

Grow this section as we hit gotchas — surprising tool behaviors, MCP queries that paid off, paths/conventions that aren't obvious from tool names alone.

### Unity MCP gotchas hit so far

- **`unity_execute_code` runs as a method body, not a file.** No top-level `using` directives — fully-qualify (`UnityEditor.AssetDatabase`, `UnityEngine.Object`) and disambiguate `Object`. `return` to send JSON back.
- **Creating a prefab variant in code:** instantiate the base with `PrefabUtility.InstantiatePrefab(baseGo)` (a *connected* instance), modify it, then `PrefabUtility.SaveAsPrefabAsset(instance, newPath)` — saving a connected instance to a new path produces a Variant. Wire serialized refs with `SerializedObject`/`FindProperty`/`ApplyModifiedPropertiesWithoutUndo`.
- **Scene NetworkObjects need a baked SceneId.** A NetworkObject created via script + `EditorSceneManager.SaveScene` ships with `SceneId=0` / `IsSceneObject=false`, so FishNet refuses to spawn it. Bake it: make the scene the ACTIVE scene (`SceneManager.SetActiveScene`), then call `NetworkObject.CreateSceneId(scene, force:true, out _)` (internal static, via reflection) and re-save. The active-scene context is required — it NREs otherwise.
- **Author scenes additively to protect the open (possibly dirty) scene.** `EditorSceneManager.NewScene(..., NewSceneMode.Additive)` + `SceneManager.MoveGameObjectToScene` + `SaveScene` + `CloseScene`, restoring the previous active scene. Never `OpenScene(..., Single)` on a dirty editor.
