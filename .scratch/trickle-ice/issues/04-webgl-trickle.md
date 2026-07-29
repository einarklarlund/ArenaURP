# 04 — WebGL trickle parity, and delete the bundled-candidate path for good

**What to build:** the same behaviour in the **WebGL jslib** transport, and the deletion of the
bundled-candidate path everywhere it still exists. Two browsers join a room through a locally
served build in well under a second, and a browser client can once again join an editor host.

The two transports are compiled mutually exclusively and must stay behaviourally identical — the
jslib gets the same immediate-SDP send, the same per-connection inbound queue, the same
end-of-candidates marker at gathering-complete, and the same single gather-site filter predicate.

With both sides trickling, the old path is dead weight and goes: the candidates array on the
offer/answer struct and its serializers, the `{sdp, candidates}` JSON shape, the jslib's collect
timers and candidate bundling, the collect-duration field on the transport component, both
callback-registration signatures that pass it, and its Boot-scene serialized value.

**Blocked by:** 03 (which establishes the shape the jslib mirrors and owns the shared C# surface).

**Status:** in-progress — code complete; three ACs need a Unity Editor / a browser (see the punch list)

**Notes**

- Deleting the collect-duration parameter spans the C# extern signatures **and** the jslib
  functions — do them in the same change or the WebGL build breaks.
- The Boot-scene serialized value and any other authoring that crosses the GUID boundary goes
  through the Unity MCP, not hand-edited YAML. If working in a worktree, the interactive editor
  defaults to the **main** checkout — point it at the worktree first.
- The manual two-browser join is the **only** path that exercises the jslib plus real browser ICE
  plus mDNS; it is the gate for this ticket, not an optional extra.
- Note for the tester: the WebGL-dev and Local bootstrap paths add the transport with **zero ICE
  servers** — host and mDNS candidates only. Fine for a LAN test, but it does not exercise STUN.

- [x] The jslib sends offer and answer immediately, with no collect timer and no attached
      candidate array
- [x] The jslib forwards each candidate as it is gathered and sends the empty marker at
      gathering-complete
- [x] Each per-connection JS object buffers inbound candidates until its remote description is set,
      then flushes them in arrival order
- [x] One gather-site filter predicate on this transport too, matching the standalone behaviour
- [x] The candidates array is gone from the offer/answer struct, its serializers and the JSON shape
- [ ] The collect-duration setting is gone from the component, both extern signatures, the jslib
      and the Boot scene
      — **code done, scene not.** The field, `_candidateCollectDurationMilliseconds`, both
      `RegisterCallbacks` signatures and their jslib parameter lists, and both `setTimeout` blocks
      are gone. `Boot.unity:381` still carries the now-orphaned
      `candidateCollectDurationMilliseconds: 4000` — a serialized value, punch list A4.
- [ ] Manual gate: a two-browser join over a locally served dev build connects in well under a
      second
      — **not run.** No browser and no dev build were available. Punch list T6.
- [ ] Manual gate: a browser client joins an editor host — the cross-transport gap from 03 is closed
      — **not run.** The only Unity Editor is attached to the main checkout. Punch list T7.

## What was built

**The jslib mirrors the standalone shape.** `CreateOffer` and `HandleOffer` hand the SDP back the
instant `setLocalDescription` resolves — both `setTimeout(..., CandidateCollectDuration)` blocks are
gone, and the payload is `{sdp}` with no `candidates`. Each connection's `onicecandidate` now emits
straight out through the new `localIceCandidateCallback`, filtered once by
`Connection.shouldTrickle` — UDP-only, then the off-by-default `filterLocalConnections` diagnostic
reading the candidate's typed `protocol`/`type`/`address` rather than splitting the SDP line.

**End-of-candidates, exactly once.** `Connection.emitEndOfCandidates` is guarded by a per-connection
`endOfCandidatesEmitted` flag, and *both* of the browser's completion signals funnel through it: an
`onicecandidate` event whose candidate is null or empty, and `onicegatheringstatechange === "complete"`.
Whichever fires, and however often, one marker goes out.

**Inbound queue, in arrival order.** Each JS `Connection` holds `inboundCandidates` plus an
`endOfCandidatesBuffered` flag — the same split `InboundCandidateQueue` makes, so the marker is
always released *last*, after the candidates it completes. `markRemoteDescriptionSet()` is called
where `setRemoteDescription` resolves in both directions (`HandleAnswer` on the host, `HandleOffer`
on the client), flushes in order, then switches to pass-through.

**The ordering guarantee is untouched.** The host's trickle route still starts closed and opens only
after `client.Send(offer)` — that lives in `SignalManager`, which is transport-agnostic, so the
WebGL host inherits it unchanged. This matters: the signal server *silently* drops a trickle into a
session whose `connectionId` is not yet bound.

**Deletions.** `OfferAnswer.candidates` and its serializer/deserializer legs; the `{sdp, candidates}`
JSON on both sides; `ApplyBundledCandidates`; `candidateCollectDurationMilliseconds` and
`_candidateCollectDurationMilliseconds`; the parameter in both extern signatures **and** both jslib
functions, changed in the same commit. `filterLocalConnections` took the freed parameter slot, since
the gather site is where that predicate now lives on this transport too.

**Verified out of editor.** All four affected assemblies type-check under both define sets,
including a build with `UNITY_EDITOR` stripped so the `UNITY_WEBGL && !UNITY_EDITOR` branch actually
compiles — proven by planting a deliberate type error inside that branch and confirming only the
stripped build fails. The reshaped `OfferAnswer` bytes were round-tripped through `SignalServer.js`'s
own parsing expressions for `0x04` and `0x05` (opcode, both `int32`s, the length-prefixed token, the
`10 + tokenLength` slice), including the error form, a pooled buffer with slack, and a non-ASCII SDP.
The jslib passes `node --check`, and its `Connection` class was extracted and driven under Node:
arrival-order flush, marker-released-last, duplicate markers collapsing, the once-only emit guard,
and the filter predicate.

**No WebGL path was executed.** No browser, no dev build, no emscripten output. The two manual gates
are the only things that would exercise the jslib for real, and neither ran.
