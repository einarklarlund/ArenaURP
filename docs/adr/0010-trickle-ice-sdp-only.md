# ADR 0010 — Full trickle ICE; offer/answer carry no candidates

- **Status:** Accepted (2026-07-24)
- **Date:** 2026-07-24

The `CanoeWebRTC` transport stalled ~4s on each side gathering ICE candidates before sending the offer/answer (two serial stalls, ~8s join floor), then bundled them into `OfferAnswer.candidates[]`. We now send SDP the instant it exists with **no** candidates and stream each candidate as it is gathered (RFC 8838 full trickle), ending with an empty-candidate marker at gathering-complete — one delivery path, one filter site, one inbound buffer. We rejected half-trickle (keeping the bundled array): RFC 8838 §16 defines it as an interop fallback for peers of unknown capability, and we ship both peers in lockstep, so it would only re-add the stall it exists to tolerate.

Considered options: full trickle (chosen) · half-trickle / opportunistic prelude (rejected — reintroduces the stall, duplicates the candidate path for no interop gain).
