# Spec — Trickle ICE follow-ups

_Produced by `/to-spec` on 2026-08-01 from [trickle-ice-follow-ups.md](trickle-ice-follow-ups.md), the output of the 2026-07-31 `/grill-with-docs` session. That file settled **what** to do; this one settles **how it is built and how it is proven**, and records three places where the settled plan was overridden or found stale during verification._

**Two repos, one contract.** `ArenaURP` (the game) and `ArenaInfra` (the signal server) ship in lockstep — a wire mismatch is a hard failure at handshake time, not a degraded experience. W1 spans both and must land together.

---

## Problem Statement

The trickle ICE work (ADRs 0010–0012) shipped a working join, and left behind a set of small, specific liabilities that nobody feels until they cost an afternoon:

- **A joining player can be routed into the wrong handshake.** The answer a client sends carries no **session token**, so the signal server has to *guess* which **signaling session** it belongs to by scanning for the (client, host) pair. If one client holds two live sessions with the same host, the answer can bind to the wrong one. This is not a regression — the retired routing map had the identical flaw — but it is a correctness hole sitting in the security-critical relay path, and the client already holds the token it would need.

- **A developer cannot run two join harnesses at once, or run one at all without first stopping their own server by hand.** The game hard-codes its editor signal address, including the port. `ArenaInfra` already made its listen port overridable; the game side never took the matching override, so the two cannot be moved apart.

- **A performance regression in the join would pass CI silently.** The join harness measures duration and only *logs* it. Its one real bound is a 30-second liveness timeout — which sits far above the ~8-second floor of the bundled-candidate path that trickle replaced. A full regression back to that path would go green.

- **An agent or a new reader writes comments the project's own standard forbids.** The no-comments-except-where-unclear rule now lives in `CLAUDE.md`, but the code written before it was recorded there does not follow it, so the repo teaches the opposite of the rule.

- **A reader cannot audit the relay without first proving two names mean the same thing.** The signal server invented `signalId` as a synonym for the glossary's **player id**, and compares the two against each other in the anti-spoof check itself. Separately, two unrelated constants both equal `-1`, and the five sites that branch on one spell the bare literal instead of naming which concept they mean — a wrong guess there is invisible to the compiler.

- **An ordering guarantee on the outbound trickle path has no test.** Candidates gathered before the route can be resolved are held and released in arrival order. That machine is hand-rolled inside `SignalManager` with no coverage, while an equivalent, fully-tested machine exists for the inbound direction.

- **The signal server's own test script cannot run on the Node its manifest allows**, and the harness reads the live session count by regex-matching a human-readable log line, so rewording a log message breaks tests for no behavioural reason.

## Solution

Close each liability at its cheapest correct point, in an order that never reshapes security-critical code in the same change that alters the contract that code enforces.

For the **player**, the visible outcomes are narrow but real: a join can no longer be routed into another attempt's session, and a regression that makes joining slow again fails the build instead of passing it.

For the **developer and the agents working in this repo**, the outcomes are larger: two harnesses can run side by side and beside a developer's own server; the relay reads as one vocabulary instead of two; every `-1` says which of two concepts it means; the outbound ordering guarantee is behind a test; and the code stops contradicting the project's own comment standard.

The work splits into eight workstreams. **W1 is the only one that crosses the wire contract**, and it lands first and alone. Everything else is independent and can land in any order.

## User Stories

**As a player**

1. As a player joining a room, I want my answer to reach the exact handshake my offer came from, so that a second attempt against the same host cannot silently hijack the first.
2. As a player joining a room, I want a peer that is not a member of my handshake to be unable to relay signalling into it, so that another player cannot interfere with my connection.
3. As a player joining a room, I want the join to stay as fast as it is today, so that a future change cannot quietly return me to the multi-second wait trickle ICE removed.
4. As a player whose join fails, I want the failure to be a failure and not a hang, so that I am returned to the menu rather than left watching a loading view.

**As the developer running the game**

5. As a developer, I want the editor's signal address to take a port override, so that I can point a harness at an isolated server instead of the one my Docker container is holding.
6. As a developer, I want to run two join harnesses concurrently, so that a slow end-to-end suite does not serialise behind another run.
7. As a developer, I want to stop having to remember to `docker stop arena-signal-server` before a harness run, so that a forgotten step stops producing a confusing failure.
8. As a developer, I want the harness's port to be derived rather than hard-coded in two files that must agree, so that changing it is one edit and not a hunt.

**As the developer maintaining the signal server**

9. As a maintainer, I want the answer to name its **signaling session** directly, so that the routing code says what it does instead of reconstructing it by search.
10. As a maintainer, I want the client-ownership check that the old scan performed implicitly to become an explicit assertion, so that a reader can audit the authorization property in one line rather than deducing it from a loop.
11. As a maintainer, I want length-prefixed framing to exist as one named pair of helpers, so that the fifth hand-computed offset in this file is not written by hand like the previous four.
12. As a maintainer, I want the relay's four direction branches collapsed into one role table, so that I can confirm at a glance that sender, recipient, and done-marker are paired correctly for both opcodes.
13. As a maintainer, I want the wire change and the code-shape changes in separate commits, so that a bisect through the relay can distinguish a contract break from a refactor.
14. As a maintainer, I want the server to call a **player id** a player id everywhere, so that the anti-spoof comparison does not read as two concepts being equated.
15. As a maintainer, I want the manifest's Node range to permit the Node its own test script requires, so that a fresh clone can run `npm test` without editing the manifest first.
16. As a maintainer, I want the live session count read through a real accessor rather than parsed out of log prose, so that improving a log message is not a breaking change.
17. As a maintainer, I want knowledge that already lives in an ADR referenced rather than paraphrased in a docblock, so that the two cannot drift apart.

**As the developer maintaining the game client**

18. As a game-side maintainer, I want each `-1` to be a named constant that says which concept it is, so that reading a branch does not require guessing between "no connection id was stamped" and "this is our own client route".
19. As a game-side maintainer, I want the outbound candidate buffer to be a pure, named, separately tested unit, so that its arrival-order guarantee is proven rather than assumed.
20. As a game-side maintainer, I want that unit tested without bending the inbound queue's meaning, so that neither direction inherits a rule it does not need.
21. As a game-side maintainer, I want the join harness to fail loudly on a slow join, so that a regression is caught by CI rather than noticed months later.
22. As a game-side maintainer, I want a slow-join failure to read differently from a never-connected failure, so that I know which of the two happened without reading the code.
23. As a game-side maintainer, I want the slow-join message to quote both the ceiling and the recorded baseline, so that raising the ceiling later is an informed decision rather than a shrug.

**As an AI agent working in this repo**

24. As an agent reading this codebase, I want the source to follow the comment rule `CLAUDE.md` states, so that imitating the surrounding code produces compliant code rather than violations.
25. As an agent, I want the hook configuration's project-directory resolution to live in one dispatcher, so that the next hook edit is made once instead of six times.
26. As an agent, I want the domain glossary's terms to be the only terms in use, so that I do not have to establish that two identifiers hold the same value before reasoning about a check.

**As a reviewer or auditor**

27. As a reviewer, I want the relay's membership check to be the only thing standing between a prober and another pair's routing state — and to be able to see that in one place, so that I can review it without tracing a scan.
28. As a reviewer, I want the wire change proven by a test that runs both real sides, so that "the two repos agree" is a fact the suite establishes rather than a claim the changelog makes.
29. As a reviewer, I want each workstream to be separately revertible, so that one problematic change does not force the rest back out.
30. As a reviewer, I want work the plan listed but that turns out to be already done to be dropped explicitly, so that a ticket does not send someone to re-fix a fixed thing.

## Implementation Decisions

### Three deviations from the settled plan

These were established during verification against the code as it stands on 2026-08-01, and supersede the corresponding lines in the plan.

- **Dropped — the `CancelJoin` double-walk.** The plan's first W8 bullet says `RoomManager.CancelJoin` walks the transport lookup twice. **It does not.** The local was hoisted when join cancellation shipped; both connection-state reads already go through it. Nothing to do. _(This is the third time a recorded-but-unfixed trickle finding has proved stale on re-check — see the `unactioned-findings-need-evidence` memory.)_
- **Changed — W6 does not fold into `InboundCandidateQueue`.** The plan calls the outbound buffer "`InboundCandidateQueue` with the fields renamed". It is not: the inbound queue has exactly one consumer today, its entire API is named for the inbound direction, and its end-of-candidates rule — collapse repeats, force the marker last — is inbound-specific reordering that the outbound path never needs (the local ICE agent already emits the marker last by construction). **Decision: extract the outbound buffer as its own small pure type with its own EditMode tests.** The plan's stated goal — put an untested ordering guarantee behind tests — is met without giving either direction a rule it does not want. Rule of two is not satisfied here; this is a second *case*, not a second *concretion of one abstraction*.
- **Changed — the live session count becomes a test-only module export**, not a structured log key. See W8 below, including the structural consequence this carries.

### W1 — Put the session token on the answer *(both repos, lockstep)*

The **answer** gains the **session token** the client already holds, so the server can look its **signaling session** up directly instead of scanning for one.

**Wire contract.** Only the client→server direction changes. The server→host direction is untouched, so the game's inbound parser is not modified.

```
client → server   0x05  int32 targetPlayerId  [tok]  sdp…      ← [tok] is new
server → host     0x05  int32 connectionId           sdp…      ← unchanged
```

`[tok]` uses the same one-byte-length + UTF-8 framing the offer already carries, written through the existing session-token helper rather than by hand.

- **Game side (`SignalManager`)** — append the token in the answer serializer, taken from the trickle route the join callback already stored it on. No new state, no new lookup.
- **Server side (`SignalServer`)** — read the token, resolve the session directly, and **assert client ownership explicitly**: the session's client **player id** must equal the sending socket's. The scan being deleted performed this check implicitly by construction; the direct lookup does not, so it becomes a stated line. This is a gain — an implicit property of a loop becomes an auditable assertion.
- The scan function is **deleted, not fixed**. The plan's third F7 item (its two bare adjacent int32 parameters) disappears with it.

**Riding along — two code-shape changes to the signal server, in separate commits:**

- **Extract the length-prefixed framing pair.** Framing is hand-inlined four times with hand-computed offsets today, and this change adds a fifth. The test harness already has both helpers; the server it tests does not. This feature has already shipped one offset bug, which is the argument.
- **Collapse the candidate relay's four direction branches into one role table** keyed by opcode — sender, recipient, done-marker, and label per role. This function *is* the anti-spoof check, so "can a reader confirm it pairs correctly at a glance" is load-bearing, not cosmetic.

**Staging is not optional.** Three commits, in this order:

1. The wire change, green across the existing signal-server suite **and** the end-to-end join harness.
2. The framing helpers.
3. The role table.

The relay is security-critical. It must not be reshaped in the same diff that changes the contract it enforces.

### W2 — A port override for the editor's signal address

The game's editor-mode signal address becomes overridable rather than a hard-coded `localhost:9001`. The server side already honours a listen-port environment variable; this is the matching half.

The harness's port constant and the game's address must derive from **one** source rather than two that agree by convention. The harness currently documents the coupling in a constant's doc comment and again in the join fixture's class summary; both should stop needing to.

**Consequence:** two harnesses can run concurrently, and neither collides with a developer's own running server. The manual "stop the Docker container first" step disappears.

### W3 — A join-duration ceiling in the harness

The join fixture keeps its existing 30-second timeout as the **liveness** bound ("did it connect at all") and gains a **second, explicit ceiling** on the measured stopwatch.

- **Ceiling: elapsed < 0.5s.** This is a loopback join between a host and a client in one process on one machine; the measured baseline is 0.05s and the bundled path this replaced had an ~8s floor. 0.5s is an order of magnitude above the baseline and an order of magnitude below the regression it must catch.
- **The two failures must read differently.** The timeout says *never connected*. The ceiling says *connected, but slowly* — and its message quotes both the ceiling and the recorded 0.05s baseline, so a future decision to raise it is informed rather than reflexive.
- The stopwatch already starts at the join attempt, after server-up and room-created, so no setup cost sits inside the measurement. This does not change.

The plan's "no timing asserts" position stands for *ICE* timing. This is not an ICE-timing assert; it is a floor-versus-ceiling regression detector with a 10× margin on both sides.

### W4 — Sweep the comments

The rule — **no comments except where the code is not self-explanatory** — is already recorded in `CLAUDE.md`, and the commit-conventions skill that formerly cited "doc comments" as a justification has already been corrected. Both were done in the grill session. What remains is the sweep.

**Modules to sweep:** the local UI event definitions, the room manager, the UI manager, the loading view, and the signal server's newly-added commentary.

**The judging line:** a comment survives only where the knowledge has **no other home in the file** and cannot be expressed in code — a retired wire byte that must never be reused, a timing bound, a buffer that must be sliced exactly. Anything restating what the code does, goes. *Why* belongs in an ADR or a skill, where it cannot rot away from the code.

**The server has an extra edge:** its session-sweep docblock and session-table comment restate ADR 0011 and the repo glossary nearly verbatim, putting the same knowledge in three files that will drift. Point at the ADR rather than paraphrasing it.

### W5 — Name the two `-1` sentinels

Two constants of equal value carry unrelated meanings: *"this wire form stamps no connection id"* (on the trickle payload struct) and *"this route is our own client connection"* (in the signal manager). Five branch sites in the transport spell the bare literal instead of naming either, and a reader cannot tell which concept is under test — nor can the compiler.

**In scope:** name it at all five sites, choosing the correct one of the two constants at each. **Not in scope:** collapsing the repeated `connectionId != -1` comparison into a named predicate. It is the same question asked five times and does want a predicate, but that touches the send path and waits for a reason.

**Noted, not scheduled:** the trickle payload struct now carries two disjoint wire forms — token XOR connection id — behind one type with two factories and two serializer pairs. That is arguably two types wearing one name, and the sentinel exists only to mark which one you hold. If it is ever split, the sentinel disappears with it.

### W6 — Extract the outbound candidate buffer *(revised — see deviations)*

The signal manager hand-rolls a small machine: hold **candidates** gathered before the route can be resolved, release them in arrival order once it can, pass through from then on. It has no test.

**Extract it as its own pure type**, a sibling of the inbound queue and in the same assembly, so the existing EditMode test assembly reaches it with no new assembly reference (it already references that one by name). Its surface is the machine and nothing else: offer a candidate, open the route, release what was held in arrival order, reset.

**It is not the inbound queue and must not become it.** The inbound queue collapses repeated end-of-candidates markers and forces the marker last, because applying "gathering is complete" before the candidates it completes would discard paths. The outbound path has no such hazard — the local agent emits the marker last by construction — and inherits nothing.

The end-of-candidates marker retiring the route (which keeps a host's route table from growing one entry per join) stays in the signal manager. It is routing lifecycle, not buffering.

The WebGL jslib's third copy of this machine stays. It is another language; that duplication is forced.

### W7 — One name for the player id

The server names its per-socket identifier **player id** in the glossary and on the socket, then names the session's two members `hostSignalId` / `clientSignalId` and its sweep by `signalId` — a term the glossary never defines, sitting directly beside the `playerID` table holding the same value. The anti-spoof check compares one against the other: one comparison, two names, one concept.

**Rename to the glossary term** across the session fields and the player sweep. Adding "signal id" to the glossary instead would also be coherent, but the glossary already lists it under terms to avoid.

**Coordinate with W1**, which touches the same fields. Cheapest item on the list and the one most likely to mislead a reader auditing the relay.

### W8 — Three small ones *(the fourth was already done)*

- **The manifest's Node range.** `engines` permits Node 16, but the package's own test script needs 18 or newer. The upper bound was narrowed for the WebSocket library's ABI; the lower bound never moved with it. Raise the lower bound.

- **A real accessor for the live session count.** The harness currently regexes the count out of a human-readable log line, making log wording a test contract. **Decision: expose it as a test-only module export from the server.**

  **This carries a structural consequence that must be planned for, not discovered.** The server module today performs all of its setup as top-level side effects — it opens its listen socket and starts two intervals at load. A test that `require`s it therefore acquires a listening socket and two live timers in the test process, and `node --test` will not exit while those timers are alive. So this item is not a one-line export; it requires the server's startup and shutdown to become callable, and the harness to gain an in-process mode alongside the spawned-child mode it uses today.

  **Therefore: this is its own ticket and its own commit, and it is the largest item in W8 by a wide margin.** If it proves larger than its value, dropping it and leaving the log-scraping in place is an acceptable outcome — say so explicitly rather than half-landing it. The other log-text assertions (nine of them, across routing, session, and trickle) are **out of scope** either way: they are defensible while the log is the only observable for a silently dropped message.

- **The hook configuration's repeated project-directory resolver.** The same roughly-200-character resolver is inlined six times in the Claude settings file. One dispatcher script the hooks call collapses it, and stops the next hook edit from having to be made six times. Out of feature, cheap while the file is open.

**Explicitly not in scope:** the eight-line Redis error/connect handler duplicated between the signal server and the main server. Two servers sharing no module is the reason, and that is not worth fixing for eight lines.

## Testing Decisions

**What makes a good test here.** Assert on what a peer can observe — bytes on the wire, a connection reaching data-channels-open, an ordering of released candidates — never on internal routing state or on the shape of the code that produces it. The existing suites already hold this line: the harness drives the server as a real WebSocket peer and reads real frames, and the join fixture mocks nothing at all. The gate is *"can this test fail?"* — a test that cannot distinguish the change from its absence is not coverage.

**Three seams exist. No new ones are added.**

**Seam 1 — the signal server's fake-peer harness.** Spawns the real server on a reserved port, drives it with WebSocket peers, asserts on real frames. Already carries 33 tests across lifecycle, routing, session, and trickle. It has both framing helpers and full builders/parsers for every opcode.

- **W1 (server half)** — the answer builder gains the token. New tests: an answer naming a live session routes to the host with the session's **connection id** stamped; an answer naming an unknown token is dropped silently; an answer naming a session the sender does not own is dropped silently. That last one is the explicit ownership assertion the deleted scan used to provide implicitly, and it is the test that must exist.
- **W7** — no new tests. The rename must leave all 33 green; that is the whole assertion.
- **W8 (session count)** — the accessor replaces the log regex in the tests that already count sessions. The existing lifecycle and session assertions are the coverage; they must stay green through the change of mechanism.

**Seam 2 — the end-to-end join fixture (PlayMode).** The only seam that spans both repos: it launches the real `ArenaInfra` signal server from the sibling checkout and drives the production signal manager and production transport against it over real ICE, with the host joining its own room so one process carries both peers. Nothing is mocked.

- **W1 (lockstep)** — this is where "the two repos agree" becomes a fact rather than a claim. A token the server cannot read means the answer never routes and the join never completes, so the existing channels-open assertion catches a mismatch without a new test. **The wire change is not done until this fixture is green with both sides changed.**
- **W3** — the ceiling assertion is added here, beside the existing liveness assertion.
- **W2** — changes this fixture's own plumbing; proven by the fixture continuing to pass against a non-default port.
- **W5** — the regression net. The fixture exercises both the host route and the client route in one process, so choosing the wrong sentinel at a branch site breaks the join rather than passing silently. This is the reason W5 is safe to do at all.
- **W4** — must leave it green. A comment sweep that changes behaviour has removed something that was not a comment.

**Seam 3 — the EditMode pure-unit tests.** Construct the type, drive it, assert on returned state. Precedent is directly adjacent: the inbound candidate queue's nine ordering tests and the trickle payload struct's eight round-trip tests.

- **W6** — the extracted outbound buffer gets its own fixture, modelled on the inbound queue's. The invariants to pin: candidates offered before the route opens are held, not sent; opening releases them in **arrival order**; after opening, candidates pass straight through; opening twice releases nothing the second time; reset returns to holding with nothing held. It must **not** inherit the inbound queue's marker-collapse and marker-last tests — the absence of those is part of the decision.

**No seam — and this is stated rather than hidden.** The hook-dispatcher item has no test in either suite. Its only verification is that the hooks still fire. That is acceptable for a tooling-layer change of this size, but it should not be described as verified when it is merely exercised.

**Regression floor.** Every networked change gets a PlayMode test — the project's standing rule. W1 satisfies it through Seam 2. W2, W3, W5 and W6 are not networked changes in that sense but all sit under Seam 2's umbrella, and the fixture must be green at each landing.

## Out of Scope

- **Anything closed without action in the plan.** In particular the join phase being published from eight sites: the argument that made it look sharp — a vendored transport reaching into a first-party UI event — was verified false, because the signal manager compiles into the game's main assembly alongside the UI events it publishes. Recorded so the assembly-boundary argument is not raised again.
- **Items routed to `docs/known-issues.md`:** the send thread's dead teardown and its silent exception swallow; teardown remaining on the main thread; the signal server's Node 20 requirement; the end-of-candidates marker being applied on WebGL but dropped on standalone.
- **The headless-browser WebGL join harness** — large enough to need its own grill; it is on the tooling backlog.
- **Collapsing the repeated `connectionId != -1` comparison into a named predicate** (W5). Naming the sentinel is in; reshaping the send path is not.
- **Splitting the trickle payload struct into its two disjoint wire forms.** Noted in W5 as a real observation, deliberately not scheduled.
- **The nine log-text assertions in the server's tests**, beyond the session count. Log text stays a contract for silently-dropped messages until there is a better observable.
- **The duplicated Redis error handler** across the two servers.
- **The retired `0x06` opcode.** It stays retired and unreused. Any change here that appears to need a new opcode should stop and reopen ADR 0011 instead.

## Further Notes

**ADR posture.** Nothing here contradicts ADR 0010 (full trickle, SDP-only offer/answer), 0011 (server-owned signaling sessions named by an opaque token), or 0012 (the join patience timer). W1 is 0011 being completed rather than amended: 0011 established the token as the thing that names a session, and the answer was the one message still routing without it. **No new ADR is expected.** If W1's ownership assertion turns out to need a design decision rather than a line of code, that is the signal to stop and write one.

**Sequencing.** W1 is the only cross-repo item and the only one with a staging constraint. W7 touches the same fields and should land adjacent to it — before or after, but not interleaved. W2, W3, W4, W5, W6 and the W8 remnants are mutually independent.

**Where the count stands.** The signal server's suite is 33 tests as of 2026-08-01. The plan says 34; the difference is not a missing test, just a miscount at writing time. Use 33 as the baseline that must stay green.

**Publication.** This spec was not published to the issue tracker: `gh` is not installed on this machine. It lives here for `/to-tickets` to consume, or to be filed as an issue by hand later.
