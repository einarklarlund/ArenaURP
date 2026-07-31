# Trickle ICE — follow-ups found during implementation

Things the feature work surfaced that are **out of scope for its six tickets**. None is a
regression introduced by trickle; each is either pre-existing or a consequence of a decision
recorded elsewhere. Listed so they are not lost, not because they block anything.

## F1 — The answer (0x05) carries no session token, so the server scans for its session

**Where:** `ArenaInfra/signal-server/SignalServer.js`, the `receivedAnswerFromClient` case.

The settled contract (ArenaInfra ticket 01) left 0x05 unchanged on the wire, which was the shape
both plans already assumed. The consequence is that the server cannot look the answer's session
up by token — it scans for the (client, host) signal-id pair, restricted to sessions already
bound at offer time, newest first.

That faithfully reproduces the last-write-wins semantics of the `signalConnectionID` map it
replaced, and the behaviour is pinned by a test. But it is strictly weaker than a token would be:
if one client holds two live sessions with the same host, the answer can bind to the wrong one.
**The retired map had the identical flaw, so this is not a regression** — but the fix is cheap
and obvious now that both sides already speak tokens: add `[tok]` to 0x05 and look it up
directly, deleting the scan. Doing so is a wire change and must land in both repos together.

## F2 — Port isolation is blocked on the game side, not just the server side

**Where:** `ArenaURP` `SignalManager.Start` hard-codes `ws://localhost:9001/Signal` in the editor.

ArenaInfra ticket 02 made the server's listen port env-overridable (`SIGNAL_PORT`), but that alone
cannot move a test harness, because the editor-side URL is fixed. Until the game side takes a
matching override, two PlayMode harnesses cannot run concurrently and neither can be isolated
from a developer's own running server on 9001.

## F3 — `Connection.SendThread`'s teardown path is dead code

**Where:** `Assets/CanoeWebRTC/Canoe/Core/Standalone/Common/Connection.cs:525–573`.

```csharp
bool dispose = false;
try {
    while (localConnection.ConnectionState == RTCPeerConnectionState.Connected) { ... }
    if (dispose) { ...; if (!closing) RemoteChannelsClosed(); }   // never true
}
catch (ThreadInterruptedException) { dispose = true; }            // set only in the catches
catch (ThreadAbortException)       { dispose = true; }
catch (Exception)                  { dispose = true; }
```

`dispose` is tested at `:548`, *inside* the `try`, but assigned only inside the three `catch`
handlers — which are reached by leaving the `try`. So `if (dispose)` is only ever evaluated while
`dispose` is still `false`, and the teardown has never fired. The author plainly intended a
connection to be torn down from its background send thread.

**Do not simply un-dead it.** Making it live would call `RemoteChannelsClosed()` →
`CloseConnection()` → `connections.Remove()` **from a send thread**, introducing a genuine
cross-thread race against `ServerSocket`'s dictionary — see F4 for why a snapshot would not be an
adequate answer. The right shape is to marshal that teardown onto the main thread through the
existing queued-event mechanism, not to add locking to the send path.

## F4 — `ServerSocket.SendToAll` is safe *today*, and that is load-bearing

**Where:** `Assets/CanoeWebRTC/Canoe/Core/Standalone/Server/ServerSocket.cs:232`, `:240`.

These iterate `connections.Values` and look like a classic enumerate-while-mutating hazard. They
were investigated specifically and the conclusion is that **the crash is not reachable**: every
mutation of `connections` (`Add` `:83`, `Remove` `:96`, `Clear` `:68`) and the iteration itself
run on the Unity main thread. Unity.WebRTC marshals every native callback through
`ExecutableUnitySynchronizationContext.Post`, which always enqueues and never executes inline;
nothing in the project calls `WebRTC.ExecutePendingTasks`; and there is no `ConfigureAwait(false)`,
`Task.Run` or `ThreadPool` use anywhere under `Assets/CanoeWebRTC/Canoe/`. The real threads that
do exist (`Connection.SendThread`) touch only `sendQueue`/`sendPending`/`dataChannel`.

**No fix was applied, deliberately.** A snapshot would protect nothing, cost an allocation on a
per-broadcast hot path, and — worst — *look* thread-safe while leaving the unsynchronized
dictionary reads in `SendToOne` (`:206–222`), `GetConnectionState` (`:23`) and `CanoeWebRTC.cs`
(`:289`, `:498`, `:626`) racing. It would convert a loud `InvalidOperationException` into silent
corruption if concurrency were ever introduced.

This is recorded because it is a **standing invariant, not an accident**: the safety of this code
depends on all teardown staying on the main thread. F3 is the change most likely to break it.

Not analyzed: `Assets/CanoeWebRTC/Canoe/Core/WebGL/Server/WebGLServerSocket.cs:353` has its own
`SendToAll`. WebGL is single-threaded so it is very unlikely to differ, but nobody has checked.

## F7 — Code-shape findings in `SignalServer.js` left unactioned, deliberately

Raised by the code review over this feature. All are judgement calls on the **security-critical
relay path**, which had just gone green across 34 tests; reshaping it at the end of a large feature
buys legibility at the cost of the one thing that path had going for it. Recorded rather than
actioned, so the next person to open that file inherits the reasoning and not just the code.

- **`relayCandidate` branches on `direction` four separate times** — sender, recipient, log label,
  and which `doneGathering` flag to set. A single role table keyed by opcode
  (`{senderKey, recipientKey, doneKey, label}`) collapses all four and makes the sender/recipient
  pairing auditable at a glance. This is the most attractive of the three: it is the function that
  *is* the anti-spoof check, so "can a reader confirm it pairs correctly?" has real value.
- **Length-prefixed framing is hand-inlined four times** (two writes in the join hunk, two reads in
  the offer and in `relayCandidate`) with hand-computed offsets. The test harness already extracted
  `lengthPrefixed` / `readLengthPrefixed`; the server it tests did not. Hand-computed offsets on a
  wire contract are exactly where that extraction pays — and this feature already shipped one
  offset bug (the pooled-buffer slack on the Unity side).
- **`findBoundSession(clientSignalId, hostSignalId)` takes two bare int32s in adjacent positions.**
  Transposing them is silent in JS and mis-routes an answer. Ties into F1, which would delete the
  scan entirely by putting a token on `0x05` — fixing F1 makes this one moot, so do that first.

## F6 — The end-of-candidates marker is applied on WebGL and dropped on standalone

**Where:** `Assets/CanoeWebRTC/Canoe/Core/Standalone/Common/Connection.cs:315-324` versus
`WebGLWebRTCJSlib.jslib`'s `markRemoteDescriptionSet`.

The plan asks the two transports to stay behaviourally identical, and on this one point they are
not. The browser applies the marker (`addIceCandidate({candidate: ""})`); the standalone transport
logs it and drops it.

**The divergence is forced, not a shortcut.** Unity.WebRTC documents `AddIceCandidate(null)` as the
end-of-candidates indicator but dereferences the argument anyway, and `RTCIceCandidate` refuses to
construct from an empty candidate string — so on that transport there is no way to hand the marker
to the ICE agent at all. It is purely an optimisation (the agent concludes checking on its own
either way), so it is dropped rather than faked.

Two smaller asymmetries ride along: the standalone queue collapses repeated markers where the JS
buffer does not, and the jslib tears down on `failed` *or* `closed` where standalone handles only
`Failed`. Neither has been observed to matter; both are noted so a future reader does not
"fix" one side into disagreement with the other.

Worth revisiting only if a Unity.WebRTC release makes the marker representable.

## F5 — The signal server cannot run on the repo's local Node

`uWebSockets.js` v20.40.0 ships prebuilt binaries only for the Node 16/18/20/21 ABIs; the machine's
Node is 24, where `require('uWebSockets.js')` throws outright. This predates trickle — `npm start`
is equally broken — but it now also gates the PlayMode harness, which launches a real server.

Use `nvm use 20`. The dependency was left pinned on purpose: the uWS release that adds Node 22/24
binaries (v20.69.0) **drops** Node 20, which is what `signal-server/Dockerfile` deploys on, so a
bump would break the image. `package.json` now declares `engines: node >=16 <22` to make the
constraint loud instead of mysterious.

## F8 — Nothing in the suite defends the join time the feature exists to deliver

**Where:** `Assets/Tests/PlayMode/Networking/WebRtcJoinTests.cs`.

The harness asserts on data-channels-open and **logs** the duration; the plan settled on "no timing
asserts" deliberately, because real ICE is asynchronous and tight timing assertions are a known
flakiness source. That reasoning holds for a tight bound. But the consequence is that the whole
point of trickle — the join going from an ~8.05s floor to the **0.05s** measured on 2026-07-31 — is
an observation, not a guard. If a future change reintroduced a multi-second gather stall, every test
in the suite would still pass.

The gap between 0.05s and 8.05s is two and a half orders of magnitude, so a **generous ceiling** —
assert the join completes in under, say, 3s — would be nowhere near the flakiness regime the "no
timing asserts" rule was protecting against, while failing loudly on exactly the regression that
matters. Not applied here, because it edits an assertion the plan settled on and that is a decision
to take deliberately rather than in passing.

## F9 — The WebGL gate is a human with two browsers; it could be a headless harness

**Where:** the two manual gates in ticket 04 (punch list T6/T7), against `builds/webgl` as served by
`./deploy_local.sh`.

Both gates passed on 2026-07-31, so the jslib has now run for real — browser ICE, mDNS, and the
emscripten marshalling of the two new trickle callbacks. That is the **only** runtime evidence the
WebGL transport has ever had, and it is not repeatable by anything but a person opening two windows.

The asymmetry is the problem. The standalone transport is defended by `WebRtcJoinTests`, which
launches a real signal server and drives a real handshake on every run. The jslib — the half the
plan requires to stay *behaviourally identical*, and which already carries three recorded
divergences (F6) — is defended by a compile, a `node --check`, and a Node harness over the extracted
`Connection` class. Every future jslib edit silently returns this path to unverified until someone
volunteers an afternoon.

### The opening

A Unity WebGL build exposes `unityInstance.SendMessage(gameObject, method, arg)` to page JavaScript.
That is a real control surface into a running build from outside it, which means a headless-Chrome
driver (Playwright or Puppeteer) can load the served build in two pages, tell one to host and the
other to join, and read the outcome back off the console — no clicking into a canvas, no pixel
matching. The same driver could assert the specific things a human is asked to eyeball today:

- exactly **one** empty end-of-candidates trickle per peer connection per side (browsers signal
  gathering-complete twice, so this is precisely the kind of once-only condition an eye slides past);
- the host's first candidates reaching the server only **after** its offer (a mis-ordering shows up
  as a slow or failed join, never as an error);
- no `Failed to add ICE candidate` spam;
- and a join duration, which would extend F8's proposed ceiling to the transport that has no
  timing evidence at all.

### What has to be answered before this is a ticket

- **Is there a `SendMessage`-reachable entry point?** `RoomManager`'s host/join paths are driven by
  UI today. This may need a small dev-only bridge component on a known GameObject, present only in
  development builds — which is a design question (what is the smallest surface that does not become
  a second, drifting way to start a match?), not a scripting one.
- **Does headless Chrome gather usable candidates?** Data channels need no media, so no fake-device
  flags, but mDNS candidates may be absent headless. The Local/WebGL-dev bootstrap paths configure
  **zero ICE servers**, so a loopback join should have host candidates to work with — this needs
  proving, not assuming, and it is the single most likely reason the idea fails.
- **T7 is the harder half.** A browser client against an *editor* host means driving the editor too.
  A standalone player build as the host is probably the tractable substitute, at the cost of no
  longer testing the editor path specifically.
- **Where does it run?** Locally as a `deploy_local.sh` companion is the cheap version; CI needs a
  WebGL build per run, which is minutes, so it likely belongs on a pre-merge or nightly job rather
  than on every push.

Sized as a `/prototype` first — the candidate-gathering question above decides whether the rest is
worth designing. If it works, it converts the feature's least-defended surface from "ask Einar" into
something an agent can run unattended, which is the same move that took the signal server from
unverifiable to 34 tests.
