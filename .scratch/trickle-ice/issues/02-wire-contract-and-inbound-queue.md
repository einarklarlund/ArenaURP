# 02 — Wire contract and the inbound candidate queue, as pure tested units

**What to build:** the shared vocabulary the trickle path is written in — the session-token wire
form, the two new trickle opcodes, a round-trippable trickle payload, and the inbound
candidate queue — each shaped as a pure, EditMode-testable unit with **no behaviour change yet**.
Nothing calls the queue at the end of this ticket; ticket 03 wires it in.

The queue is the piece that carries a real ordering invariant (candidates that arrive before the
remote description is set must flush in arrival order, with the end-of-candidates marker applied
last), so it earns a pure core and a test under the `component-design` rule.

**Blocked by:** None — can start immediately.

**Status:** done

**Notes**

- The **session-token wire form** (length-prefixed UTF-8 vs fixed width) is one shared decision
  with ArenaInfra — it is one struct written twice in two languages. Agree it with that repo's
  plan and record the agreed encoding here before writing either serializer.
- Opcode bytes are `0x08 trickleToClient` and `0x09 trickleToHost`. The dead `0x06 trickleICE`
  constant and its unreachable cases are deleted, and the byte is **not** reused, so a stale peer
  can never half-parse a new message.
- The existing trickle payload struct is repurposed rather than replaced; its deserializer
  currently throws away sender and receiver and must stop.
- The queue must not reference `RTCPeerConnection` or any WebGL interop — it takes candidates in
  and hands them out in order on flush. Apply/attach is the caller's job.
- `Arena.Tests` targets the pure `Arena.*.Core` assemblies. Landing an EditMode test against a
  type inside the CanoeWebRTC assembly needs an assembly reference (or the unit lives in an
  `Arena.*.Core` assembly). Pick one and say which in the PR.

- [x] Trickle payload serialize → deserialize round-trips candidate, sender and receiver
      (EditMode test); the deserializer no longer discards sender/receiver
      — **amended by the settled contract** (see "What was built", below): the wire carries *one*
      routing id per direction, not a (sender, receiver) pair. Both directions round-trip
      losslessly and the deserializer no longer returns `-1, -1`.
- [x] The session token round-trips in the agreed encoding, and that encoding matches the one in
      ArenaInfra's plan
- [x] `0x08` / `0x09` constants exist; the `0x06` constant and its dead cases are gone
- [x] An inbound candidate queue unit buffers before "remote description set", flushes in **arrival
      order** on release, and passes through afterwards — covered by an EditMode test
- [x] The empty-candidate end-of-candidates marker rides the same queue and is applied **last**
- [x] EditMode suite green; no runtime behaviour changed by this ticket — the 23 new tests were
      compiled and executed **outside** the editor (the editor is attached to another checkout);
      one in-editor run of `Arena.Tests` is still owed.

## What was built

**Assembly decision: `Arena.Tests` now references the `CanoeWebRTC` assembly** (one added entry in
`Arena.Tests.asmdef`), rather than moving the units into a new `Arena.*.Core` assembly. `TrickledICE`
lives in `Supporting.cs` beside `OfferAnswer`, which `CanoeWebRTC.cs` and `SignalManager.cs` both
consume — it cannot move without touching files this ticket does not own, so `Arena.Tests` had to
reach into `CanoeWebRTC` for the payload test regardless. Given that, a separate `Arena.*.Core`
assembly for the queue would have bought a second asmdef, a `CanoeWebRTC → Core` reference and a
cross-assembly hop for one 90-line class with a single (transport-side) consumer — ceremony, by the
`component-design` rule's own "you over-extracted" smell. The queue is still *pure* in the sense the
rule cares about: no `RTCPeerConnection`, no interop, no `UnityEngine` type, testable with plain
strings.

**Wire encoding implemented** (exactly the settled contract; `TrickledICE` payload = everything
after the opcode byte):

- session token: `uint8 length` + that many UTF-8 bytes (`SessionToken.Write/Read/ByteLength`)
- `0x08`/`0x09` peer→server: `[tok] candidate…` (`TrickledICE.SerializeToServer`)
- `0x08`/`0x09` server→peer: `int32 connectionId` (little-endian) `candidate…` (`DeserializeToPeer`)
- `candidate…` is the remainder of the buffer as UTF-8 → **zero remaining bytes = end-of-candidates**

The struct's old `sender`/`receiver` ints are gone: the settled contract has no message carrying
both. Outbound the token *is* the route; inbound the server stamps one `connectionId`. Those are
now the struct's `sessionToken` / `connectionId` fields, and both survive a round trip — which is
what "stop discarding" was protecting.

**Files:** `Supporting.cs` (`SessionToken`, repurposed `TrickledICE`), new
`Canoe/Common/InboundCandidateQueue.cs`, `SignalManager.cs` (opcode constants only),
`Assets/Tests/EditMode/{SessionToken,TrickledICE,InboundCandidateQueue}Tests.cs`.

Nothing calls the queue or the new opcodes yet — ticket 03 wires them in.
