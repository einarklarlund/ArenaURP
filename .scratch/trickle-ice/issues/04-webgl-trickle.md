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

**Status:** ready-for-agent

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

- [ ] The jslib sends offer and answer immediately, with no collect timer and no attached
      candidate array
- [ ] The jslib forwards each candidate as it is gathered and sends the empty marker at
      gathering-complete
- [ ] Each per-connection JS object buffers inbound candidates until its remote description is set,
      then flushes them in arrival order
- [ ] One gather-site filter predicate on this transport too, matching the standalone behaviour
- [ ] The candidates array is gone from the offer/answer struct, its serializers and the JSON shape
- [ ] The collect-duration setting is gone from the component, both extern signatures, the jslib
      and the Boot scene
- [ ] Manual gate: a two-browser join over a locally served dev build connects in well under a
      second
- [ ] Manual gate: a browser client joins an editor host — the cross-transport gap from 03 is closed
