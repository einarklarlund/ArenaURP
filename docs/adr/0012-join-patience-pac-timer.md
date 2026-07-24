# ADR 0012 — Join patience follows RFC 8863's PAC timer (~39.5s)

- **Status:** Accepted (2026-07-24)
- **Date:** 2026-07-24

Once trickle removes the fixed gather stall, a healthy join is sub-second, but a hard-network join legitimately keeps discovering candidates for tens of seconds — and RFC 8863 §4 says an ICE agent MUST NOT declare failure while its PAC timer runs (recommended ~39.5s), even with no pairs left to check. So `signalTimeoutDurationMilliseconds` becomes **39500** (down from the authored 80000): the app no longer pre-empts the ICE agent's own patience, maximising connection success on relay-dependent networks. Because a doomed join can now hold for the full 39.5s, `LoadingView` gains a typed join-phase progress event and a Cancel button, and the host's teardown timer moves to fire on `RemoteConnectionState.Started` (fixing a connection leak) with `RTCPeerConnectionState.Failed` as immediate teardown while transient `disconnected` is ignored.

Considered options: RFC 8863 39.5s (chosen) · keep 80s (rejected — waits longer than the stack needs) · a short ~15s product deadline (rejected — would abort joins the ICE agent is still legitimately working).
