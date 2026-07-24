# 02 — Wire contract and the inbound candidate queue, as pure tested units

**What to build:** the shared vocabulary the trickle path is written in — the session-token wire
form, the two new trickle opcodes, a round-trippable trickle payload, and the inbound
candidate queue — each shaped as a pure, EditMode-testable unit with **no behaviour change yet**.
Nothing calls the queue at the end of this ticket; ticket 03 wires it in.

The queue is the piece that carries a real ordering invariant (candidates that arrive before the
remote description is set must flush in arrival order, with the end-of-candidates marker applied
last), so it earns a pure core and a test under the `component-design` rule.

**Blocked by:** None — can start immediately.

**Status:** ready-for-agent

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

- [ ] Trickle payload serialize → deserialize round-trips candidate, sender and receiver
      (EditMode test); the deserializer no longer discards sender/receiver
- [ ] The session token round-trips in the agreed encoding, and that encoding matches the one in
      ArenaInfra's plan
- [ ] `0x08` / `0x09` constants exist; the `0x06` constant and its dead cases are gone
- [ ] An inbound candidate queue unit buffers before "remote description set", flushes in **arrival
      order** on release, and passes through afterwards — covered by an EditMode test
- [ ] The empty-candidate end-of-candidates marker rides the same queue and is applied **last**
- [ ] EditMode suite green; no runtime behaviour changed by this ticket
