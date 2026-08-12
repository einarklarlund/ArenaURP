# Known issues

Things that are **wrong, or look wrong, and are deliberately not being fixed.** Each entry records what the behaviour is, why it was left, and what would change the answer. This is not a backlog — nothing here is scheduled. It exists so the next person to find one of these re-reads a decision instead of re-deriving it, and so a "fix" that would break a standing invariant is recognised as such before it lands.

An entry leaves this file in one of two ways: someone decides to fix it (it becomes work, on a backlog), or the condition it depends on changes (a library release, a platform, a threading model) and it is re-decided.

---

## The CanoeWebRTC send thread swallows its own death

**Where:** `Assets/CanoeWebRTC/Canoe/Core/Standalone/Common/Connection.cs:525–573`.

`SendThread` declares `bool dispose = false`, tests it at `:548` *inside* the `try`, and assigns it only inside the three `catch` handlers — which are reached by leaving the `try`. So `if (dispose)` is only ever evaluated while `dispose` is still `false`. The teardown block has never executed.

**The block is redundant, not missing.** `RemoteChannelsClosed()` is already wired to seven main-thread callbacks — both local channels' `OnError` (`:118`, `:124`), both remote channels' `OnError` and `OnClose` (`:141`, `:152`, `:164`, `:176`), and the connection-state site (`:229`). A dying connection *is* torn down; the send-thread path was belt-and-braces and the braces hold. Reviving it would add a second, racier route to a teardown that already happens correctly, and would do it from a background thread — see the invariant below for why that is the wrong direction.

Two live consequences ride inside the dead code, both accepted:

- **`Profiler.EndThreadProfiling()` is only called inside the dead block**, so every send thread leaks its profiler registration on exit.
- **All three catch handlers swallow the exception** — `//Debug.Log(e)` is commented out in each. If `dataChannel.Send` throws while the peer connection is still `Connected`, the send thread dies *silently*, its queue never drains again, and no callback fires because the connection is still up. **A dead send thread is currently undiagnosable in the field.** This is the real hazard in this file; it is known and accepted rather than unnoticed.

**Decided 2026-07-31: leave entirely alone.** It is vendored code that has never misbehaved, and the diff is not worth taking into it. What would change the answer: an actual field report of a stalled connection with a healthy peer connection — at which point the fix is to log the exception, not to revive the teardown.

## All CanoeWebRTC teardown stays on the Unity main thread — a standing invariant

**Where:** `Assets/CanoeWebRTC/Canoe/Core/Standalone/Server/ServerSocket.cs:232`, `:240`.

`SendToAll` iterates `connections.Values` and looks like a textbook enumerate-while-mutating hazard. **It is not reachable.** Every mutation of `connections` (`Add` `:83`, `Remove` `:96`, `Clear` `:68`) and the iteration itself run on the Unity main thread. Unity.WebRTC marshals every native callback through `ExecutableUnitySynchronizationContext.Post`, which always enqueues and never executes inline; nothing in the project calls `WebRTC.ExecutePendingTasks`; and there is no `ConfigureAwait(false)`, `Task.Run` or `ThreadPool` use anywhere under `Assets/CanoeWebRTC/Canoe/`. The real threads that do exist (`Connection.SendThread`) touch only `sendQueue`, `sendPending` and `dataChannel`.

**No fix was applied, deliberately.** A snapshot would protect nothing, cost an allocation on a per-broadcast hot path, and — worst — *look* thread-safe while leaving the unsynchronized dictionary reads in `SendToOne` (`:206–222`), `GetConnectionState` (`:23`) and `CanoeWebRTC.cs` (`:289`, `:498`, `:626`) racing. It would convert a loud `InvalidOperationException` into silent corruption if concurrency were ever introduced.

**This is the entry to read before "fixing" anything in this transport.** The safety of the code depends on all teardown staying on the main thread, and that is a discipline rather than a structural guarantee — nothing in the code enforces it. Moving any teardown onto a background thread breaks it silently.

**Not analyzed:** `Assets/CanoeWebRTC/Canoe/Core/WebGL/Server/WebGLServerSocket.cs:353` has its own `SendToAll`. WebGL is single-threaded so it is very unlikely to differ, but nobody has checked.

## The end-of-candidates marker is applied on WebGL and dropped on standalone

**Where:** `Assets/CanoeWebRTC/Canoe/Core/Standalone/Common/Connection.cs:315–324` versus `WebGLWebRTCJSlib.jslib`'s `markRemoteDescriptionSet`.

The trickle plan asks the two transports to stay behaviourally identical, and on this one point they are not. The browser applies the marker (`addIceCandidate({candidate: ""})`); the standalone transport logs it and drops it.

**The divergence is forced, not a shortcut.** Unity.WebRTC documents `AddIceCandidate(null)` as the end-of-candidates indicator but dereferences the argument anyway, and `RTCIceCandidate` refuses to construct from an empty candidate string — so on that transport there is no way to hand the marker to the ICE agent at all. It is purely an optimisation (the agent concludes checking on its own either way), so it is dropped rather than faked.

Two smaller asymmetries ride along: the standalone queue collapses repeated markers where the JS buffer does not, and the jslib tears down on `failed` *or* `closed` where standalone handles only `Failed`. Neither has been observed to matter; both are recorded so a future reader does not "fix" one side into disagreement with the other.

**What would change the answer:** a Unity.WebRTC release that makes the marker representable.

## The signal server cannot run on the machine's default Node

**Where:** `ArenaInfra/signal-server/`.

`uWebSockets.js` v20.40.0 ships prebuilt binaries only for the Node 16/18/20/21 ABIs. The machine's Node is 24, where `require('uWebSockets.js')` throws outright. This predates trickle — `npm start` is equally broken — but it now also gates the PlayMode join harness, which launches a real server.

**Use `nvm use 20`.** The `WebRtcJoinTests` fixture finds it on its own (`$ARENA_SIGNAL_NODE`, else the newest `~/.nvm/versions/node/v20.*`).

**The dependency is pinned on purpose.** The uWS release that adds Node 22/24 binaries (v20.69.0) **drops** Node 20, which is what `signal-server/Dockerfile` deploys on, so a bump would break the image. `package.json` declares `engines: node >=16 <22` to make the constraint loud instead of mysterious.

**What would change the answer:** a uWS release whose ABI support overlaps both the deployed Node and the machine's, or moving the Dockerfile off Node 20.

---

## Adjacent, and not an issue

**~~Running the join harness needs port 9001 free.~~ Resolved.** The editor address was hard-coded to `ws://localhost:9001/Signal`, so the join fixture collided with the `arena-signal-server` Docker container and with a second concurrent harness. `SignalManager` now reads the `ARENA_SIGNAL_ADDRESS` environment variable in the editor and falls back to that default — the matching half of ArenaInfra's `SIGNAL_PORT`.
