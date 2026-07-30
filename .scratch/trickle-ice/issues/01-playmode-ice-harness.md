# 01 — PlayMode ICE harness against a locally-launched signal server

**What to build:** a PlayMode test that stands up a real `CanoeWebRTC` host and client in the
editor, talking to a signal server the fixture launches and reaps itself, and asserts both data
channels open. Built **against today's bundled-candidate path**, where it should already pass —
so the harness is proven before the protocol changes underneath it, and it records the join time
we are trying to beat.

This is the prefactor that makes the trickle change easy: ticket 03 inherits a working
end-to-end assertion instead of debugging a new fixture and a new wire protocol at once.

**Blocked by:** None — can start immediately.

**Status:** done

**Notes**

- Assert on **channels-open with a generous timeout**. No timing assertions — real async ICE is a
  known flakiness source (see the combat-harness fixture for the house pattern).
- The join time is captured as a **logged baseline**, not an assertion. Expect roughly the ~8s
  floor (two serial ~4s gather stalls).
- The signal server lives in the sibling **ArenaInfra** repo and already listens on 9001. If the
  fixture needs port isolation, that repo threads a port env var through — coordinate rather than
  hard-coding a second port here.
- Reap the server process on teardown including on test failure, so a crashed run does not leave a
  listener holding the port for the next one.

- [x] A PlayMode test in `Arena.PlayTests` launches a signal server, hosts, joins, and asserts both
      peers reach data-channels-open
- [x] The fixture reaps the server on teardown, on both the pass and the fail path
- [x] The test passes repeatedly against the current (pre-trickle) code — run it enough times to
      show it is not flaky
- [x] The observed join duration is logged so 03 can be compared against it
- [x] If the harness proves irreducibly flaky in-editor, say so in the ticket and stop — do not
      absorb the flake into the suite; the manual two-browser gate carries verification instead
      (not triggered: five consecutive headless runs passed, spread 8.05–8.07s — see Outcome)

---

## Outcome

**Built**

- `Assets/Tests/PlayMode/Networking/WebRtcJoinTests.cs` — the test (`Arena.PlayTests`,
  `Joining_a_room_opens_both_peers_data_channels`). It stands up the production stack: the real
  `SignalManager` on a real websocket driving a real `CanoeWebRTC` transport over real ICE, in the
  exact order `RoomManager` uses in game (the host joins its own room, so one editor process carries
  both peers of the handshake). Transport values mirror Boot.unity's authored ones — MTU 2047, 4s
  candidate collect, 25s operation timeout, 80s signal timeout, the five Boot STUN servers — so the
  baseline measures what players experience, not the shorter code defaults.
- `Assets/Tests/PlayMode/Networking/SignalServerProcess.cs` — launch/readiness/reap for the server.
- `Arena.PlayTests.asmdef` now references `CanoeWebRTC`. `SignalManager` lives in Assembly-CSharp,
  so it is reached by reflection contained to one region of the fixture (the `*TestRig` house rule).

**Assertion shape.** Channels-open on *both* peers, via the transport's own
`OnClientConnectionState`/`OnRemoteConnectionState` — those fire from
`Connection.RemoteChannelsOpened`, i.e. only once a peer holds both remote data channels. Timeout is
60s and nothing asserts on time.

**BASELINE join duration: ~8.05s** (join attempt → both data channels open). Five runs:
8.07 / 8.05 / 8.05 / 8.05 / 8.05s. That is the predicted ~8s floor from the two serial 4s gather
stalls, and it is the number ticket 03 has to beat. The test logs it as
`[IceHarness] BASELINE join duration (join attempt -> both data channels open): N.NNs`.

**How it was verified.** Six headless runs on 6000.3.7f1 against a worktree branched from the
pre-trickle commit:
`Unity -batchmode -projectPath <worktree> -runTests -testPlatform PlayMode -testFilter Arena.PlayTests.WebRtcJoinTests -testResults <xml> -logFile <log>`
(note: **no `-quit`** — with `-runTests` it makes the editor exit before the tests run). All six
passed; the port was free after every one. Gate 5 ("can it fail?") was checked directly by
temporarily cutting the join timeout to 1s: the test failed with
*"the joining peer never reached data-channels-open within 1s"*, and the server was still reaped —
that run also exposed a real teardown bug (cancelling a signal timeout mutates the dictionary being
enumerated), now fixed by snapshotting first.

**Needed from ArenaInfra**

1. **Port isolation.** Not hard-coded to a second port here, as instructed. The port is pinned at
   9001 on *both* sides: `SignalManager.Start` hard-codes `ws://localhost:9001/Signal` in the editor,
   and `SignalServer.js` hard-codes `.listen(9001, …)`. Making the harness port-configurable needs
   ArenaInfra's `PORT`/`SIGNAL_PORT` env var **and** a matching editor-side override in
   `SignalManager` — one cannot move without the other. Until then, two harnesses cannot run
   concurrently and a developer's own signal server on 9001 will collide.
2. **A Node version pin.** `uWebSockets.js` v20.40.0 ships prebuilds only up to Node 20's ABI, so the
   server dies instantly on Node 22/24 (`Cannot find module './uws_darwin_arm64_137.node'`). The
   fixture works around it by preferring an `~/.nvm/versions/node/v20.*` install, overridable with
   `ARENA_SIGNAL_NODE`. `signal-server/package.json` should declare an `engines` field (and ideally
   bump uWebSockets.js) so this is a documented constraint rather than folklore.
3. `node_modules` is not committed; the fixture fails with an explicit "run `npm install`" message
   rather than a timeout. Redis is *not* required — the server logs reconnect warnings and serves
   fine without it.
