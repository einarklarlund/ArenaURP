# Trickle ICE follow-ups — the agreed work

_Output of a `/grill-with-docs` session on 2026-07-31 over the fifteen follow-ups the trickle-ICE feature surfaced. Every item below is **decided**; this file is the input to `/to-spec`, not a discussion. Items closed as deliberately-unfixed live in [../known-issues.md](../known-issues.md); the one item large enough to need its own grill is on the [tooling backlog](tooling-features.md)._

**Two repos.** `ArenaURP` and `ArenaInfra` ship in lockstep — a wire mismatch is a hard failure at handshake time. W1 below spans both and must land together.

---

## W1 — Put the session token on the 0x05 answer *(F1, F7)*

**Both repos, lockstep. Sequence matters — see the staging note.**

The answer (`receivedAnswerFromClient`, 0x05) carries no token, so `SignalServer.js` cannot look its session up directly. It scans (`findBoundSession`, `:477`) for the (client, host) pair among sessions already bound at offer time, newest first — faithfully reproducing the last-write-wins semantics of the `signalConnectionID` map it replaced. If one client holds two live sessions with the same host, the answer can bind to the wrong one. The retired map had the identical flaw, so this is not a regression.

**The client already holds the token at answer time** — it is stored on its own trickle route (`SignalManager.cs:409`) from the 0x03 success. So the change costs exactly one length-prefixed field.

**The scan is also an implicit authorization check.** It requires the session to be bound *and* to belong to (this client → that host), so a client cannot relay an answer into a session it does not own. A direct `sessions[tok]` lookup drops that for free, so the replacement must explicitly assert `session.clientSignalId === ws.playerID`. This is a gain, not a cost: it turns an implicit property of a scan into a one-line check a reader can audit.

**Wire change.** `0x05` becomes `0x05` `int32 targetPlayerID` `[tok]` `sdp…` in the client→server direction. The **server→host direction is unchanged** (`0x05` `int32 connectionId` `sdp…`), so the C# inbound parser at `SignalManager.cs:435` is not touched.

- `ArenaURP` — append `[tok]` in the 0x05 serializer (`SignalManager.cs:254–267`), using the same `SessionToken.Write` framing the offer already uses at `:211`.
- `ArenaInfra` — read `[tok]` in the `receivedAnswerFromClient` case, look up `sessions[tok]` directly, assert client ownership, delete `findBoundSession` and update its tests.

**Riding along, agreed:** the two surviving F7 code-shape items in `SignalServer.js`.

- **Extract `lengthPrefixed` / `readLengthPrefixed`.** Framing is hand-inlined four times today with hand-computed offsets, and this change adds a fifth. The test harness already has these helpers; the server it tests does not. This feature already shipped one offset bug (the pooled-buffer slack on the Unity side), which is exactly the argument.
- **Collapse `relayCandidate`'s four `direction` branches into one role table** keyed by opcode (`{senderKey, recipientKey, doneKey, label}`). It is the function that *is* the anti-spoof check, so "can a reader confirm it pairs correctly at a glance?" has real value.

**Staging — do not do this in one commit.** The wire change lands and goes green across the existing 34 tests *first*. The framing helpers and the role table follow as separate commits. The relay path is security-critical; it should not be reshaped in the same diff that changes the contract it enforces.

**F7's third item — `findBoundSession(clientSignalId, hostSignalId)`'s two bare int32s in adjacent positions — is deleted by this work, not fixed.**

## W2 — A port override for the editor's signal address *(F2)*

`SignalManager.cs:74` hard-codes `ws://localhost:9001/Signal` in the editor. `ArenaInfra` already made the server's listen port env-overridable (`SIGNAL_PORT`), but that alone cannot move a test harness while the game side is fixed.

Until the game side takes a matching override, two PlayMode harnesses cannot run concurrently, and neither can be isolated from a developer's own running server — the `arena-signal-server` Docker container binds the same port and has to be stopped by hand before a harness run.

`SignalServerProcess.Port` (`:29`) and the comments at `WebRtcJoinTests.cs:213` both record the constraint; both should stop needing to.

## W3 — A join-duration ceiling in the PlayMode harness *(F8)*

`WebRtcJoinTests` asserts on data-channels-open and only **logs** the duration. The plan settled on "no timing asserts" because real ICE is asynchronous and tight timing assertions are a known flakiness source.

**A ceiling already exists** — `JoinTimeoutSeconds = 30f` (`:57`), enforced by the `WaitUntil` at `:103`. It just sits *above* the ~8.05s bundled floor it would need to catch, so a full regression to the bundled path would pass silently.

**Agreed: add a second, explicit ceiling, and keep the first.**

- `JoinTimeoutSeconds = 30f` stays as the **liveness** bound — "did it connect at all".
- A new assert on the measured stopwatch: **elapsed < 0.5s**. This is a loopback join between a server and client on the same machine; the whole exchange is local and the measured baseline is 0.05s.
- The two failures must read differently. The timeout says *never connected*; the ceiling must say *connected, but slowly* — and its message quotes both the ceiling and the recorded 0.05s baseline, so that if it ever does flake, raising it is an informed decision rather than a shrug.

The stopwatch starts at the join attempt (`:99`), after server-up and room-created, so no setup cost sits inside the measurement.

## W4 — Settle the comment rule, then sweep *(F10)*

The standing rule is that code carries no comments and is made self-explanatory instead. **It lives only in a memory.** The single place in the repo that mentions comments at all is `.claude/skills/commit-conventions/SKILL.md:22`, which justifies keeping reasoning out of commit messages on the grounds that it "already lives in the code's doc comments" — instructing the reader to do the thing the rule forbids. An agent reading only the repo would write these comments every time, correctly.

**The rule, as settled:** no comments, **except where the code is not self-explanatory.**

1. ~~**Give the rule a home in `CLAUDE.md`**, stated in that form.~~ **Done in the grill session** — it is now a bullet under "Conventions you must not rediscover". Until it was in the repo, any sweep would have grown straight back.
2. ~~**Fix `commit-conventions/SKILL.md:22`**~~ **Done in the grill session** — the advice (keep reasoning out of commit messages) was right; the justification now points at ADRs, `CONTEXT.md` and the skills, and names the no-comments rule explicitly so it cannot be read as licence again.
3. **Then sweep** — the remaining work, against that line: `Assets/Scripts/Events/LocalUIEvents.cs` (18 comment lines), `RoomManager.cs` (29), `UIManager.cs` (34), `LoadingView.cs` (11), and the ~60 lines added to `ArenaInfra/signal-server/SignalServer.js`.

Judging the exception: a comment survives only where the knowledge has no other home in the file and cannot be expressed in code — a retired wire byte that must never be reused, a timing bound, a buffer that must be sliced exactly. Anything restating what the code does, goes.

**The ArenaInfra half has an extra edge.** `sweepSession`'s docblock and the `sessions` block-comment restate ADR 0001 and `CONTEXT.md` almost verbatim, so the same knowledge now sits in three files that will drift apart. Point at the ADR instead of paraphrasing it.

## W5 — Name the two `-1` sentinels *(F11)*

**Where:** `Canoe/Common/Supporting.cs:153` (`TrickledICE.NoConnectionId = -1`), `Extra/Signal/SignalManager.cs:45` (`clientRouteID = -1`), and the bare literal at `CanoeWebRTC.cs:334`, `:358`, `:385`, `:419`, `:476`.

Two constants of equal value carry unrelated meanings — "this wire form stamps no connection id" and "this route is our own client connection" — and the five sites that actually branch on the sentinel spell `-1` inline rather than naming either. A reader at `CanoeWebRTC.cs:385` cannot tell which of the two concepts is being tested, and the compiler will never tell them they guessed wrong.

**The cheap half is in scope: name it at all five sites.** The cascade is the same question asked five times (`if (connectionID != -1)` = "am I the server?"), which wants a named predicate rather than a repeated comparison — but collapsing the branch touches the send path and waits for a reason.

**Noted, not scheduled:** `TrickledICE` now holds two disjoint wire forms — token XOR connectionId — behind one struct with two factories and two serializer pairs. That is arguably two types wearing one name, and the sentinel exists only to mark which one you are holding. If it is ever split, `NoConnectionId` disappears with it.

## W6 — Fold `TrickleRoute`'s buffer into `InboundCandidateQueue` *(F12)*

Trickle needs the same small machine in several places: hold candidates until the far side is ready, then release them in arrival order and pass through from then on.

`Canoe/Common/InboundCandidateQueue.cs` **is** that machine — pure, `Reset`-able, covered by EditMode tests. `SignalManager` then hand-rolls it a second time as `TrickleRoute { sessionToken, open, pending }` plus `OpenTrickleRoute` (`:53–61`, `:279–312`), with no test of its own.

The jslib copy is forced — it is another language. This one is not: `TrickleRoute`'s buffer is `InboundCandidateQueue` with the fields renamed. Folding it in puts an untested ordering guarantee behind tests that already exist.

## W7 — Stop the signal server inventing a synonym for `player id` *(F14)*

**Where:** `ArenaInfra/signal-server/SignalServer.js:36`, `:193–197`, `:356–368`, `:387–390`.

`ArenaInfra/CONTEXT.md:10` names the per-socket identifier a **player id** (`ws.playerID`), and `docs/agents/domain.md` says to use the glossary term and not drift to synonyms. The new session fields are `hostSignalId` / `clientSignalId`, and the sweep is `sweepSessionsOfPlayer(signalId)` — a term the glossary never defines, sitting directly beside `ws.playerID` and the `playerID` table that hold exactly the same value. `:193` reads `ws.playerID !== session.hostSignalId`: one comparison, two names, one concept.

This is the cheapest item on the list and the one most likely to mislead — a reader has to prove the two are the same value before they can audit the anti-spoof check.

**Rename to `hostPlayerId` / `clientPlayerId`**, or add "signal id" to `CONTEXT.md` and use it everywhere. Doing neither is the only bad outcome. Coordinate with W1, which touches the same fields.

## W8 — Four small ones *(F15)*

None is worth a section; all are cheap if the file is open anyway.

- **`RoomManager.CancelJoin` walks `InstanceFinder.TransportManager.Transport.GetConnectionState` twice** (`:81–82`). A local for the transport removes the second walk and the null-guard duplication with it.
- **`signal-server/package.json` forbids the Node its own test script needs.** `engines` says `>=16 <22`; `"test": "node --test test/"` needs ≥18. The range was narrowed for the uWS ABI; the lower bound never moved with it. Should be `>=18 <22`. *(Verified still unchanged as of 2026-07-31.)*
- **The server's tests assert on `console.log` wording** — nine `assert.match(server.log(), ...)` across `routing`/`session`/`trickle`, plus `harness.js:363` parsing `"N live."` to count sessions. Log text is now a contract: rewording a message breaks tests for no behavioural reason. Defensible while the log *is* the only observable for a silent drop, but **the session count at least deserves a real accessor**.
- **`.claude/settings.json` inlines the same ~200-character project-dir resolver six times.** Out of feature. One dispatcher script the hooks call would collapse it, and would stop the next hook edit from having to be made six times.

**Not in scope:** the eight-line ioredis `on('error'|'connect')` block pasted into both `SignalServer.js:11–17` and `MainServer.js:5–11`. Two servers sharing no module is the reason, and that is not worth fixing for eight lines.

---

## Closed without action

**F13 — the join phase is published from eight sites.** `LocalUIEvents.OnJoinPhaseChanged` fires from five points in `SignalManager` (`:140`, `:239`, `:245`, `:397`, `:415`) and three in `RoomManager` (`:109`, `:116`, `:132`), three of them publishing `Failed` for unrelated reasons.

**The argument that made this look sharp does not hold.** The claim was that a vendored transport reaches into a first-party UI event. It does not: `CanoeWebRTC.asmdef` covers `Assets/CanoeWebRTC/Canoe/` only, and `SignalManager.cs` lives at `Assets/CanoeWebRTC/Extra/Signal/` — **outside** it, compiling into Assembly-CSharp alongside `RoomManager` and `LocalUIEvents`. There is no vendored→first-party assembly dependency. It is a folder that reads like a vendor boundary without being one, over code the trickle work forked heavily anyway.

What remained was five phases published from eight correctly-placed sites. **Closed 2026-07-31, no action** — recorded here so the assembly-boundary argument is not re-raised.

## Elsewhere

| Item | Where it went |
|---|---|
| F3 — the send thread's dead teardown, and its silent exception swallow | [known-issues.md](../known-issues.md) |
| F4 — teardown stays on the main thread (standing invariant) | [known-issues.md](../known-issues.md) |
| F5 — the signal server needs Node 20 | [known-issues.md](../known-issues.md) |
| F6 — end-of-candidates marker applied on WebGL, dropped on standalone | [known-issues.md](../known-issues.md) |
| F9 — a headless-browser WebGL join harness | [tooling-features.md](tooling-features.md) → [tooling/webgl-join-harness.md](tooling/webgl-join-harness.md) |
