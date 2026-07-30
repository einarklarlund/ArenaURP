# 03 — Standalone trickle: instant SDP, streamed candidates, sub-second join

**What to build:** the whole trickle path for the **Unity.WebRTC / standalone** transport (editor
and desktop builds). An editor host and client join through the signal server and reach
data-channels-open in well under a second instead of the ~8s two-stall floor, with ICE still
promoting to the better path afterwards as candidates keep arriving.

End to end: offer and answer go out the instant the local description is set, advertising
`a=ice-options:trickle` and carrying **no** candidates; every candidate the agent gathers is
relayed as it appears, through the server-owned signaling session addressed by its token; each
side buffers inbound candidates until its remote description exists, then flushes them in order;
gathering-complete sends the empty end-of-candidates marker.

**Blocked by:** 01 (the harness that verifies it), 02 (the contract and the queue).
**Also gated externally by ArenaInfra S1–S5** — the session table and the 0x08/0x09 relay routes.
Nothing connects until that ships; the two repos land together.

**Status:** in-progress — code complete; two ACs need a Unity Editor (see the punch list)

**Notes**

- The **signal manager stays a dumb pipe**: it routes a trickled candidate to the right peer-
  connection wrapper and never inspects remote-description state. The wrapper owns the queue
  (ADR 0011).
- The host associates each connection with its session token when it creates and sends the offer,
  so it can address host→client trickles.
- **Fold in the candidate-filter consolidation (plan T5).** The `filterLocalConnections` diagnostic
  moves to a single emit/drop predicate at the gather site, beside the existing UDP-only check,
  replacing the SDP-string scrub that ran over the bundled array. Keep the flag off by default.
- **Leave the WebGL surface alone.** The bundled-candidate field on the offer/answer struct, the
  collect-duration setting and the jslib all stay as they are — ticket 04 deletes them together, so
  both tickets stay green and WebGL ↔ WebGL keeps working. A **cross-transport** join is expected
  to be broken between 03 and 04; see the README's green-gap note.
- **Verify the configured STUN host still resolves.** A dead STUN server now delays
  *gathering-complete* — the end-of-candidates trigger — rather than just wasting a bundle slot.
  Replace it if it is dead.

- [x] Offer and answer are sent as soon as the local description is set — the fixed gather delay is
      gone from both the offer and the answer path
- [x] Offers and answers advertise `a=ice-options:trickle` and carry no candidates
- [x] Each gathered UDP candidate is relayed as it appears, in the correct direction, addressed by
      the session token
- [x] Gathering-complete emits the empty-candidate marker exactly once per connection
- [x] Candidates arriving before the remote description is set are buffered and flushed in arrival
      order; later ones apply immediately
- [x] One filter predicate per transport at the gather site; the SDP-string candidate scrub is gone
- [ ] Ticket 01's PlayMode test still passes, and the logged join is **well under a second**
      against the baseline it recorded
      — **not run.** Ticket 01's harness is being built in a separate worktree and has not merged,
      so there is nothing to run and no baseline to compare against. **No join time was measured**;
      the claim this ticket can honestly make is only that both fixed multi-second stalls are gone
      from the code. Punch list T4.
- [x] The configured STUN host is confirmed reachable (or replaced) — resolved in `fbcd57bd`:
      `stun.stunprotocol.org:3478` is dead (no A record), `stun.l.google.com:19302` answers, and the
      replacement `stun.cloudflare.com:3478` probes clean. Applying it is a Boot-scene serialized
      value, punch list A2.

## What was built

**The seam.** `CanoeWebRTC` gained three members and nothing else knows about the split:
`OnLocalIceCandidate` (raised by the gather site, subscribed by `SignalManager`) plus
`AddRemoteCandidateToClient` / `AddRemoteCandidateToServer`. It only runs one way at compile time —
`SignalManager` lives in `Assembly-CSharp`, which references the `CanoeWebRTC` assembly and cannot
be referenced back, so the transport cannot call the signal manager directly.

**Host token threading.** `0x02` join notify → `pendingSessionTokens[clientSignalId]`. At
`CreateAndSendOffer` the token moves to `trickleRoutes[connectionId]` **before** the offer is
created, because `SetLocalDescription` starts gathering and the first candidates can appear while
the offer is still being awaited. The route starts **closed**: the server binds `connectionId` onto
the session only as it relays the offer and silently drops any trickle for a session that has none,
so candidates gathered early buffer in the route and flush, in order, immediately after
`client.Send(offer)`. The client's route opens at once on `0x03` — the server has necessarily bound
the connection id before the offer it relays can reach the client, and the client gathers nothing
until it answers that offer.

**End-of-candidates.** Emitted at the gather site (`Connection.EmitEndOfCandidates`), guarded by a
per-connection `bool`. Both of libwebrtc's completion signals — a null candidate and
`OnIceGatheringStateChange == Complete` — funnel through it, so whichever fires (or fires twice)
produces exactly one marker. Sending it also retires the route, which doubles as a bound on the
host's route table.

**Inbound.** `SignalManager` picks the wrapper by opcode alone and hands the candidate over;
`Connection` owns the `InboundCandidateQueue` and decides whether it can be applied yet (ADR 0011).
`RemoteDescriptionSet()` is called where `SetRemoteDescription` completes in both paths.

**`OfferAnswer.candidates` survives** for ticket 04: the WebGL jslib still reads and writes it in the
`{sdp, candidates}` JSON. Standalone now always sends an empty array and applies whatever a bundling
peer sends through the same queue.

**Wire format verified out of editor.** The `sendOfferSerializer` / `trickleSerializer` /
`trickleBody` bodies were lifted verbatim into a throwaway console app, and the bytes they emit were
parsed by ArenaInfra's own expressions from `SignalServer.js`: opcode, both int32s, the
length-prefixed token, the `2 + tokenLength` candidate slice, and zero-remaining-bytes for the
marker all match. Both serializers now return exactly-sized `ArraySegment`s — a pooled buffer's
slack would otherwise have been read as trailing garbage on a candidate, since `candidate…` is
"the rest of the buffer".
