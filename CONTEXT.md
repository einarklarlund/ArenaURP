# Arena

An arena deathmatch FPS where players **build their characters** a la rogue-like or deck builder. The depth comes from a **handful of systems interacting**; upgrades target those systems and, above all, the **interactions between them.**

That paragraph is the **essence** — the north star every feature answers to. It is deliberately tiny, and a change to it **replaces** rather than appends: if you can't recite it, it isn't doing its job. The operative gate a feature must pass ("drastically changes how someone plays" **and** "interacts with an existing system") lives with the backlog it filters, in [docs/plans/features.md](docs/plans/features.md).

## Language

Terms specific to Arena. General engineering vocabulary lives elsewhere: the deep-module words (module, interface, seam, depth) are the `codebase-design` skill's, and the design lenses are `engineering-canon`'s.

### The game

**System**:
One of the interacting mechanisms upgrades target. The current set — **movement, health, weapons, projectiles, pickups, passive abilities, active abilities, kills** — is the scope boundary, not the essence; it changes, and adding to it is a major decision. Systems that upgrades **modify** (movement, health, weapons, projectiles) are distinct from those that **drive or deliver** interactions (pickups deliver upgrades; kills is an interaction *source*).
_Avoid_: subsystem, module, feature area

**Match**:
One run of the game loop, whose lifecycle is `MatchFlowManager.State` (`Pregame → PregameCountdown → During → PostgameCountdown → Postgame`). Managers never call each other; `MatchMediator` is the single place they are wired together.
_Avoid_: game, round, session

**Peer**:
Any process participating in a match — the server or one client. Which peer *simulates* a given piece of behaviour is a design decision made per system, never assumed.
_Avoid_: instance, node, machine

### The entities

**Player**:
A `NetworkPlayer` — the persistent participant identity holding username, score, readiness, and loadout. Serves humans **and** bots (`IsBot`); never assume a client connection backs one.
_Avoid_: user, client, connection

**Pawn**:
The in-world body a **Player** controls, and the network identity hub for it. A composition of sibling components (movement, weapon, temperature, colour, interaction) plus the per-datum **controllers** on its root. Deliberately knows little — it is a thin facade over its siblings.
_Avoid_: character, avatar, entity, actor

**Pawn input**:
The source of a Pawn's intent, behind one base type with three implementations — human (client-owned), bot (server-owned), scripted (editor and tests only). One prefab therefore serves all three, and every updater early-outs when its input is inactive.
_Avoid_: controller (that word is taken — see **Controller**), driver

**Projectile**:
A spawned, owned object carrying its own motion, collision, durability, and damage as components. Its motion is closed-form and ticked independently on every peer, so only a per-spawn anchor rides the wire.
_Avoid_: bullet, shot, missile

### Combat

**Hit**:
One typed `HitMessage` dispatched to every handler on the source — a single atomic event, not a sequence of calls. Damage, knockback, and hitstun are data the message carries, not separate hits.
_Avoid_: impact, collision (collision is the *detection*, the hit is the *event*), damage event

**Hit channel**:
The tag on a **Hit** that decides which handlers respond to it. A handler declares the channels it is enabled for; the channel is the call site, not an authored routing table.
_Avoid_: hit type, event type, category

**Controller**:
The component owning one datum of a **Hit** end-to-end — both dealing and receiving halves — living on the entity root so the collision site resolves it by walking up from the struck collider. `HealthController`, `KnockbackController`, `HitstunController`. `HealthController` is the *one* health system in the game, shared by pawns, projectiles, and props alike.
_Avoid_: handler (a handler is the *reaction*; a controller owns the *datum*), receiver, dealer, resolver

**Modifier**:
A struct a system contributes into a producer-owned collection that the producer folds each tick. The contribution channel, not a callback: an absent contributor is a neutral identity, so systems influence each other without knowing each other.
_Avoid_: buff, effect, multiplier, stat

### Composition

**Kind**:
A *type* of thing a player can own — a projectile kind, an ability kind. Expressed as an identity-only ScriptableObject (name, icon, prefab reference) plus an independent prefab copy carrying **all** gameplay config, so instances self-configure on every peer and nothing rides the wire.
_Avoid_: type (ambiguous with C# types), archetype, definition, data asset

**Ability**:
An upgrade a **Player** owns. An **active** is a spawned instance parented under the pawn, held in an ordered roster. A **passive** is a *reference* into a shared registry of kinds, held in the player's **loadout** — not a spawned instance.
_Avoid_: skill, perk, power, upgrade (an upgrade is what a player *gets*; an ability is what they *hold*)

**Loadout**:
The per-player list of **Ability** references defining what that player currently holds. Granting adds a reference, removing removes one, and stealing **moves** one — no spawn or despawn.
_Avoid_: inventory, build, kit

**Core**:
A pure `Arena.<X>.Core` processor with no engine dependency, holding logic that is determinism-critical or heavily branchy, tested in EditMode. A targeted exception to component-first logic, never the default — reuse is not a reason to make one.
_Avoid_: service, engine, model, domain layer

## Where the rest lives

- **How** each of these is built — the doctrine — is in `.claude/skills/`; start from `arena-systems-map`.
- **Why** a contested choice went the way it did is in [docs/adr/](docs/adr/).
- **What is not yet decided** is in [docs/plans/decisions/](docs/plans/decisions/), indexed with the features waiting on it in [docs/plans/features.md](docs/plans/features.md).
