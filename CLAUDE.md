# CLAUDE.md

Guidance for Claude Code in this repository.

**This file is an INDEX, not a manual.** It holds only what is non-obvious and always relevant. The detail lives in `.claude/skills/`, which load on demand — keep it that way. Start with the `arena-systems-map` skill whenever a task spans more than one system, and `ai-workflow` whenever the *process* feels repetitive or error-prone.

**Before design work or a new feature, read `DESIGN.md`** (open questions + ADR index + the two north stars) and the **roadmap/sequencing in `Docs/plans/master-execution-dag.md`** — several decisions are deliberately deferred in DESIGN.md and must not be improvised. Before any plan is *finalized*, interview it through the `engineering-canon` skill's seven lenses (Parnas/Brooks/Lehman/Conway/Dijkstra/Constantine/Martin) — agreement is reached, not assumed, and settled positions live in its shared-understandings register.

## Project

Unity 6 (`6000.3.7f1`) URP arena shooter. Multiplayer is server-authoritative via **FishNet** with two transports: **Tugboat** (UDP, standalone) and **CanoeWebRTC** (WebGL via WebRTC + SimpleWebTransport signaling). Solution: `ArenaURP.slnx`.

Editor path used by scripts: `/Applications/Unity/Hub/Editor/6000.3.7f1/Unity.app/Contents/MacOS/Unity`. ParrelSync is installed for multi-instance networked testing.

Scenes: `Boot.unity` (entry) → `Offline.unity` / `Online.unity` / `Debug.unity`. `Networking/Bootstrap.cs` picks Local/Dev/Prod and **swaps the transport** (Tugboat in editor, CanoeWebRTC in WebGL) — never assume the same transport in editor vs. build. `RoomManager` handles WebRTC rooms.

## Build & Run

Driven from `Assets/Scripts/Editor/BuildScripts.cs`:

- **WebGL dev**: menu `Build > Build WebGL Development` → `builds/webgl`.
- **WebGL dev + local server**: `./deploy_local.sh` (`-s` skips the build and just serves on :8080).
- **Standalone**: `Unity -quit -batchmode -projectPath . -executeMethod BuildScripts.BuildProject -buildPlatform <linux|mac|windows> [-development] [-profiler]` → `builds/<platform>_<server|client>/`. Linux uses the dedicated-server subtarget.
- **CI**: `.github/workflows/deploy.yml` builds WebGL and deploys to `gh-pages` on push to `main`.

## Tests

EditMode (`Assets/Tests/EditMode/`, asmdef `Arena.Tests`) targets the **pure `Arena.*.Core` assemblies** — construct structs, assert on returned state. PlayMode (`Assets/Tests/PlayMode/`, asmdef `Arena.PlayTests`) uses FishNet host fixtures and reaches Assembly-CSharp through `*TestRig` classes by reflection.

**Delegate test runs to the `unity-validator` subagent** rather than driving the runner from the main thread — it knows the traps (PlayMode's JSON is never written; a stale results file reads as a fresh pass). Rules for what deserves a test, and the gate *"can this test fail?"*, are in the `integration-testing` skill. **Every networked change gets a PlayMode test.**

## The one architectural rule

**Gameplay logic goes in a pure `Arena.<System>.Core` processor; the `Pawn<X>` NetworkBehaviour is Unity glue only** — read serialized refs, build an `<X>Input`, call `<X>Processor.Process`, apply the result. Enforced by asmdef boundaries: a Core assembly cannot reference UnityEngine wrappers, FishNet, or other gameplay code. Need a new dependency in a processor? Add a field to its `Config`/`Input` struct instead.

Bot AI follows the same split (`BotBrainLogic`/`BotStateMachine`/`BotInputMath`/`BotVisionMath` ↔ `BotBrain`). → `functional-core-imperative-shell` skill.

## Composition & communication

`Pawn` is the network identity hub (`SyncVar<NetworkPlayer> ControllingPlayer`) and a thin facade over the sibling generic `Health` — the ONE health system and sole `IDamageable`, shared by pawns, projectiles and props. It deliberately knows little; siblings reference each other through serialized fields.

The **hit-RECEIVING** systems (`Health`, `KnockbackReceiver`, `HitstunReceiver`) sit on the entity ROOT, because the collision site resolves each with the same `GetComponentInParent` walk up from the struck collider.

`PawnInput` has three implementations behind one base — `HumanPawnInput` (client-owned), `BotPawnInput` (server-owned), `ScriptedPawnInput` (editor-only) — and every `Pawn<X>` updater early-outs on `if (!input.IsActive) return;`, so one prefab serves humans, bots and tests. **This decides which peer simulates what** → `fishnet-conventions` Rule 4b.

Systems never call each other. They contribute to collections the producer folds (`PawnMovement.Modifiers`, `PawnWeapon.Modifiers`, `PendingImpulses`) or fire notifications. Managers are wired in exactly one place, `MatchMediator`. → `cross-component-communication` skill.

`MatchFlowManager.State` is a `SyncVar<MatchState>`; drive lifecycle by mutating it on the server. UI events split into `LocalUIEvents` / `NetworkUIEvents`.

## Conventions you must not rediscover

- **Server-authoritative.** Any gameplay mutation runs inside a `[Server]` method or is triggered from one. → `fishnet-conventions` skill (payload avoidance, SyncVar/RPC discipline, cast-based collision, and **which peer simulates**).
- **A hit exchange is dealer component + receiver component, fired from component presence** at the hit site (`HitResolver`) — no courier effect, no authored row; each end folds its own modifiers; never mutate an authored baseline (ADR 0002). → `presence-dispatch` skill.
- **Swappable behaviour is a COMPONENT discovered by presence** — a hit reaction is an `IHitHandler` component filtered by channel (the call site), an ability's spawn is a `SpawnNetworkObjects` component; the site finds them with `GetComponents`, no authored operation list. Varies-in-data (stat buffs, on-spawn buffs) is authored struct data. Engine-native composition, not owned dispatch machinery (ADR 0003). → `presence-dispatch` skill.
- **A KIND of thing is an identity-only SO + an independent prefab copy** carrying all config; instances self-configure on every peer. Spawning anything a player can own means stamping `NetPlayerOwnership.SpawnOwnerId` *before* `ServerManager.Spawn`. Abilities are instance prefabs with load-bearing roster ordering (**Spawn before Add, RemoveAt before Despawn**). → `type-object-pattern` skill.
- `Pawn` and `NetworkPlayer` serve **both humans and bots** (`NetworkPlayer.IsBot`). Never assume a client connection on a player.
- **No interface with one concretion.** A capability interface earns its keep only at a second, genuinely non-tree concretion (rule of two) — and when it does, it lives ON the component that carries the behaviour, discovered by presence (the `IHitHandler` shape).

## Working with Unity from Claude

A project-aware Unity MCP server is connected (`mcp__unity__*`, schemas via `ToolSearch`). **Text search over `.prefab`/`.unity`/`.asset`/`.meta` is wrong** — that YAML stores GUIDs, not names — and a `PreToolUse` hook blocks it. Anything crossing a GUID boundary, or any write to serialized state, goes through the MCP. → `unity-mcp-vs-shell` skill (decision table + gotchas: the stale domain, the silently-reverting prefab save, scene SceneId baking).

Delegate wide investigation to the **`unity-scout`** subagent and test runs to **`unity-validator`**; main-thread context is for the design. → `ai-workflow` skill.
