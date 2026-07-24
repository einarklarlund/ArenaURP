# 05 — Failure model: teardown at the right moments, and RFC 8863 patience

**What to build:** a join that stops being abandoned early and stops leaking when it succeeds.
The app's signalling deadline moves to **39.5s** so it no longer pre-empts the ICE agent's own
PAC timer (ADR 0012), a peer connection that reaches `failed` is torn down immediately, and a
transient `disconnected` is ignored rather than treated as death.

The leak this closes: the host currently cancels its teardown timer when the **answer arrives**,
not when the remote connection actually **starts**, so a connection that negotiates but never
establishes leaves its wrapper and peer connection alive forever. Fast joins make that the common
case rather than the rare one, which is why it lands with trickle.

`restartIce` stays unbuilt — recovery is a full rejoin for now; ADR 0011 holds the door open.

**Blocked by:** None — can start immediately. It touches the lifecycle surface rather than the
candidate path, so it does not gate 03 or 04, but landing it **after** 03 avoids editing the same
transport file from two directions.

**Status:** ready-for-agent

**Notes**

- Do **not** tear down on `disconnected`. Per MDN it is transient and routinely recovers; only
  `failed` is terminal.
- The timeout value exists twice — the field default and the Boot-scene serialized value. Change
  both, and change the scene one through the Unity MCP rather than by hand.
- Apply the same teardown-on-failed rule to the jslib side so the two transports stay identical.

- [ ] The signalling timeout is 39.5s in both the field default and the Boot scene
- [ ] The host's teardown timer is cancelled when the remote connection **starts**, not when the
      answer is received
- [ ] A host connection that negotiates but never establishes is torn down at the deadline —
      no leaked wrapper, no leaked peer connection
- [ ] A peer connection entering `failed` is torn down immediately; `disconnected` is left alone
- [ ] A PlayMode test covers the leak: after a join that never establishes, the host holds no
      connection once the deadline passes
