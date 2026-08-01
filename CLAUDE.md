# CLAUDE.md

Guidance for Claude Code in this repository.

**This file is an INDEX, not a manual.** It holds only what is non-obvious and always relevant. The detail lives in `.claude/skills/`, which load on demand — keep it that way. Start with the `arena-systems-map` skill whenever a task spans more than one system; `delegation` for who does the noisy work and how much process a change earns; `workflow-artifacts` whenever the tooling layer itself needs a change.

**The design/build process is the mattpocock flow:** `/grill-with-docs` → `/to-spec` → `/to-tickets` → `/implement` → `/code-review` (plus `/diagnosing-bugs`, `/prototype`, `/research`, `/wayfinder`); `/ask-matt` routes when unsure. Run the full flow for coupled/networked/core work; fast-path leaf code (see `delegation`).

**Before design work or a new feature, read `CONTEXT.md`** (the game's essence + the domain glossary), the **ADR index `docs/adr/README.md`** (what's decided and why — never contradict one silently), and the **grill-ready backlogs** — `docs/plans/gameplay-features.md` (open game features + the deferred decisions each rides, in `docs/plans/decisions/`) and `docs/plans/tooling-features.md` (work on the tools, which does not answer to the essence gate) — those decisions are deliberately deferred and must not be improvised. Deliberately-unfixed behaviour is in **`docs/known-issues.md`**; read it before "fixing" anything in the WebRTC transport. Before any plan is *finalized*, grill it (technique: the `grilling` skill) against the `engineering-canon` skill's seven design lenses (Parnas/Brooks/Lehman/Conway/Dijkstra/Constantine/Martin) and the `design-elegance` value — agreement is reached, not assumed, and settled positions live in the canon's shared-understandings register.

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

## Where component logic goes

**Logic goes in the component (MonoBehaviour composition) by default** — that IS the architecture, engine-native — shaped by two authoring guidelines, **generic-via-config** and **flat hierarchy**. Extract a pure `Arena.<X>.Core` processor + EditMode test ONLY when the logic is **determinism-critical or heavily branchy**; reuse is NOT a reason. → `component-design` skill (the guidelines, the shell↔core shape, the kept cores); rationale in ADR 0008.

## Composition & communication

`Pawn` is the network identity hub (`SyncVar<NetworkPlayer> ControllingPlayer`) and a thin facade over the sibling generic `HealthController` — the ONE health system and sole `IDamageable`, shared by pawns, projectiles and props. It deliberately knows little; siblings reference each other through serialized fields.

The **hit-RECEIVING** half of each datum lives on its per-datum controller (`HealthController`, `KnockbackController`, `HitstunController`) on the entity ROOT, because the collision site resolves each with the same `GetComponentInParent` walk up from the struck collider (ADR 0005 merged the old dealer/receiver pairs into these controllers).

`PawnInput` has three implementations behind one base — `HumanPawnInput` (client-owned), `BotPawnInput` (server-owned), `ScriptedPawnInput` (editor-only) — and every `Pawn<X>` updater early-outs on `if (!input.IsActive) return;`, so one prefab serves humans, bots and tests. **This decides which peer simulates what** → `fishnet-conventions` Rule 4b.

Systems never call each other. They contribute to collections the producer folds (`PawnMovement.Modifiers`, `PawnWeapon.Modifiers`, `PendingImpulses`) or fire notifications. Managers are wired in exactly one place, `MatchMediator`. → `cross-component-communication` skill.

`MatchFlowManager.State` is a `SyncVar<MatchState>`; drive lifecycle by mutating it on the server. UI events split into `LocalUIEvents` / `NetworkUIEvents`.

## Conventions you must not rediscover

- **Server-authoritative.** Any gameplay mutation runs inside a `[Server]` method or is triggered from one. → `fishnet-conventions` skill (payload avoidance, SyncVar/RPC discipline, cast-based collision, and **which peer simulates**).
- **A hit is one typed `HitMessage` dispatched to `IHitMessageHandler` components on the source** — we own no dispatch machinery (ADR 0002/0005). → `presence-dispatch` skill (the atomic dispatch sequence, the per-datum controllers, modifier folding, never mutating an authored baseline).
- **Swappable behaviour is a COMPONENT discovered by presence** (`GetComponents`); behaviour that varies only in data is an authored struct — engine-native composition, not owned dispatch machinery (ADR 0003/0005). → `presence-dispatch` skill.
- **A KIND of thing is an identity-only SO + an independent prefab copy** carrying all config; instances self-configure on every peer (ADR 0009 for the passive model). → `type-object-pattern` skill (owned-spawn `SpawnOwnerId` stamping, ability roster ordering, passives-as-references, pooling baseline rules).
- `Pawn` and `NetworkPlayer` serve **both humans and bots** (`NetworkPlayer.IsBot`). Never assume a client connection on a player.
- **No interface with one concretion.** A capability interface earns its keep only at a second, genuinely non-tree concretion (rule of two) — and when it does, it lives ON the component that carries the behaviour, discovered by presence (the `IHitMessageHandler` shape).
- **No comments, except where the code is not self-explanatory.** Name things so the comment would be redundant and leave it out; a comment restating what a line does is a signal the line should be clearer. The exception is narrow and real: knowledge with **no other home in the file** and no expression in code — a retired wire byte that must never be reused, a timing bound, a buffer that must be sliced exactly. *Why* belongs in an ADR or a skill, where it cannot rot away from the code. Docs, ADRs and commit messages are unaffected — this is about comments inside source files.

## Working with Unity from Claude

A project-aware Unity MCP server is connected (`mcp__unity__*`, schemas via `ToolSearch`). **Text search over `.prefab`/`.unity`/`.asset`/`.meta` is wrong** — that YAML stores GUIDs, not names — and a `PreToolUse` hook blocks it. Anything crossing a GUID boundary, or any write to serialized state, goes through the MCP. → `unity-mcp-vs-shell` skill (decision table + gotchas: the stale domain, the silently-reverting prefab save, scene SceneId baking).

Delegate wide investigation to the **`unity-scout`** subagent and test runs to **`unity-validator`**; main-thread context is for the design. → `delegation` skill.

## Agent skills

Config the `mattpocock-skills` engineering skills read (issue tracking, domain docs). Full detail in `docs/agents/`.

### Issue tracker

Issues/PRDs live in **GitHub Issues** (`einarklarlund/ArenaURP`), via the `gh` CLI. See `docs/agents/issue-tracker.md`.

### Triage labels

Default five-role vocabulary (`needs-triage`, `needs-info`, `ready-for-agent`, `ready-for-human`, `wontfix`) — read by the `/mattpocock-skills:triage` command. See `docs/agents/triage-labels.md`.

### Domain docs

Single-context. The glossary + game essence is **`CONTEXT.md`** (repo root); ADRs live in **`docs/adr/`** (the skills' default location), indexed by `docs/adr/README.md`. See `docs/agents/domain.md`.
