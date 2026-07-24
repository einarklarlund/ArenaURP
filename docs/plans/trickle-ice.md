# Trickle ICE — ArenaURP implementation plan

**Decisions:** ADR [0010](../adr/0010-trickle-ice-sdp-only.md) (full trickle, SDP-only), [0011](../adr/0011-signaling-session-routing.md) (server-owned sessions), [0012](../adr/0012-join-patience-pac-timer.md) (39.5s patience). The paired signal-server work is in **ArenaInfra** `docs/plans/trickle-ice.md`; the two must land together (both peers ship in lockstep).

**Goal:** replace the two serial ~4s gather stalls (~8s join floor) with incremental candidate exchange, so a join reaches data-channels-open in well under a second while ICE keeps promoting to the best available path.

**Two transports, one behaviour:** everything below is implemented **twice** — Unity.WebRTC C# (`#if !UNITY_WEBGL || UNITY_EDITOR`, editor + standalone) and the WebGL jslib (`#if UNITY_WEBGL && !UNITY_EDITOR`). Keep them behaviourally identical; they are compiled mutually exclusively.

---

## Wire protocol (shared with ArenaInfra — the contract)

New/changed signal opcodes (`SignalManager.cs` byte constants must match `SignalServer.js`):

| Opcode | Name | Direction | Payload after opcode byte |
|---|---|---|---|
| 0x02 | `attemptToJoinRoom` | client→server, server→host | **+ session token** now flows back to both (see 0x03 / host notify) |
| 0x04 | `receivedOfferFromHost` | host→server→client | unchanged shape, SDP now candidate-free |
| 0x05 | `receivedAnswerFromClient` | client→server→host | unchanged shape, SDP now candidate-free |
| **0x08** | `trickleToClient` | host→server→client | `sessionToken`, UTF-8 candidate string (empty string = end-of-candidates) |
| **0x09** | `trickleToHost` | client→server→host | `sessionToken`, UTF-8 candidate string (empty string = end-of-candidates) |

The old `trickleICE` (0x06) constant and its dead cases are **deleted** from both `SignalManager.cs` and `SignalServer.js`. Pick 0x08/0x09 (not reusing 0x06) so a stale peer never half-parses a new message. Session token wire form: agree the exact encoding (length-prefixed UTF-8 vs fixed width) with the ArenaInfra plan before coding — it is one shared struct in two languages.

**SDP:** offers and answers advertise `a=ice-options:trickle`. `OfferAnswer.candidates[]` is removed from the struct, its serializer, and the WebGL `{sdp, candidates}` JSON. `TrickledICE` (`Supporting.cs:76`) is repurposed as the trickle payload and its `Deserialize` must **stop discarding** `sender`/`receiver` (currently returns `-1, -1`).

---

## Work items

### T1 — Remove the bundled-candidate path (both transports)
- `Supporting.cs`: drop `candidates` from `OfferAnswer` + `Serialize`/`Deserialize`; keep `sdp`/`error`/`errorMessage`.
- `CanoeWebRTC.cs`: in `CreateOfferAsync` / `CreateResponseAsync`, delete the `Task.Delay(_candidateCollectDurationMilliseconds)` gather stall and the `iceCandidates.Select(...)` bundling — send the `OfferAnswer` as soon as `SetLocalDescription` completes.
- jslib: in the offer and answer `setTimeout(..., CandidateCollectDuration)` blocks (`WebGLWebRTCJSlib.jslib:709`, `:825`), send the SDP immediately and stop attaching `iceCandidatesArray`.
- **Delete `candidateCollectDurationMilliseconds`** from the transport component, the two `RegisterCallbacks` extern signatures + their jslib sides, and the Boot-scene serialized value.

### T2 — Outbound trickle (both transports)
- Standalone `Connection.OnIceCandidate` (`Connection.cs:180`): instead of only buffering into `iceCandidates`, hand each new UDP candidate to a per-connection outbound sink that `SignalManager` drains (or invoke a callback). On `OnIceGatheringState == Complete`, emit the **empty-candidate** end marker once.
- jslib `onicecandidate` (`:358`, `:502`): same — forward each candidate; on `onicegatheringstatechange === "complete"` send the empty marker.
- `SignalManager` gains `SendTrickle(sessionToken, candidate, toHost:bool)` choosing 0x08/0x09, and the host associates each `connectionId` with its session token at `CreateAndSendOffer` time so it can address host→client trickles.
- Apply the `filterLocalConnections` predicate **here at the gather site** beside the existing UDP-only check (see T5).

### T3 — Inbound candidate buffering + apply (both transports)
- The **peer-connection wrapper owns the queue** (ADR 0011): `Connection` (standalone) and the per-connection JS object each hold an inbound `Queue`.
- On receiving a trickled candidate: if the remote description is **not yet set**, enqueue; else `AddIceCandidate` / `addIceCandidate` immediately.
- When the remote description is set (`SetRemoteDescription` completes in `ServerReceiveAnswerAsync` / `CreateResponseAsync`; `setRemoteDescription().then(...)` in the jslib), **flush the queue in arrival order**, then switch to pass-through.
- The empty-candidate marker rides the same queue → `AddIceCandidate(null)` / `addIceCandidate({candidate:""})`. This closes both the host's before-remote-description race and the local `async void` gap in `SignalManager.HandleReceivedData`.
- `SignalManager` stays a dumb pipe: it routes 0x08/0x09 to the right wrapper (client → `connectionWithServer`; host → `_server.connections[connectionId]`) and never inspects remote-description state.

### T4 — Failure model & patience (ADR 0012)
- `signalTimeoutDurationMilliseconds` → **39500** (Boot-scene serialized value + field default).
- Host: move `StopSignalTimeout(connectionId)` to fire on `RemoteConnectionState.Started` — uncomment/repair the block at `CanoeWebRTC.cs:184` and stop cancelling on answer-receipt (`:517`). This fixes the host-side `Connection` + `RTCPeerConnection` leak that fast joins make the common case.
- Add teardown on `RTCPeerConnectionState.Failed` (`Connection.OnConnectionStateChange`, `:198`, currently log-only; jslib equivalent). **Do not** tear down on `disconnected` (transient per MDN).
- Leave `restartIce` unbuilt — recovery is a full rejoin for now (door held open in ADR 0011).

### T5 — Consolidate the candidate filter
- Keep `filterLocalConnections` (confirmed `false` in Boot scene) as a disabled diagnostic.
- Move its address/host-candidate predicate into the gather site (T2), replacing the SDP-string scrub in `FilterOutLocalandIPV6Candidates` — which is deleted along with the candidates array. One emit/drop predicate per transport, beside the UDP-only check.

### T6 — Typed join-phase UI
- Add `LocalUIEvents.OnJoinPhaseChanged(JoinPhase)` with `enum JoinPhase { ContactingSignal, Negotiating, EstablishingConnection, Connected, Failed }` (+ its `Reset()` line).
- Fire it from `SignalManager` / transport at the points they already log (connected to signal server; offer/answer exchanged; `Started`; timeout/`Failed`).
- `LoadingView`: switch on the enum, **delete** the `Application.logMessageReceived` regex scrape (`HandleLog`), add a **Cancel** button that stops the client/`ClientManager` and returns to the menu.

---

## Verification (floor: every networked change gets a PlayMode test)

- **EditMode** (`Arena.Tests`): `TrickledICE` serialize→deserialize round-trip preserving `candidate`/`sender`/`receiver`; inbound-queue ordering — feed candidates then "set remote description," assert flush order == arrival order and that the empty marker is applied last. Shape the queue as a pure unit (no `RTCPeerConnection`) so it is EditMode-testable.
- **PlayMode** (`Arena.PlayTests`): start a real `CanoeWebRTC` host + client in-editor over loopback ICE against a **locally-launched signal server**, assert both data channels open via trickle. Assert on channels-open with a **generous timeout, no timing asserts** (real async ICE is a known flakiness source — see the combat-harness memory). Spec the signal-server launch/reap as fixture setup/teardown.
- **Manual gate:** `./deploy_local.sh` then a two-browser join — the only path that exercises the jslib + real browser ICE + mDNS. Note the WebGL-dev/Local `Bootstrap` paths `AddComponent<CanoeWebRTC>` with **zero ICE servers** (host/mDNS candidates only), which is fine for a LAN test but does not exercise STUN.

## Flags to resolve during build
- Verify `stun.stunprotocol.org:3478` still resolves — a dead STUN server now delays *gathering-complete* (the end-of-candidates trigger) rather than just wasting a bundle slot.
- Authoring that crosses the GUID boundary (Boot-scene serialized values: timeout, deleted collect-duration, join-phase UI wiring) goes through the Unity MCP / worktree editor, not hand-edited YAML.
