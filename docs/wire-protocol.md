# Signaling wire protocol

The byte-level contract between `ArenaURP` (`Assets/CanoeWebRTC/Extra/Signal/SignalManager.cs`) and
`ArenaInfra` (`signal-server/SignalServer.js`). **The two repos ship in lockstep** — there is no
backward-compatibility window, so a mismatch is a hard failure at handshake time rather than a
degraded experience. Neither repo may code a (de)serializer against anything but this table.

*Why* the protocol has this shape is in ADR [0010](adr/0010-trickle-ice-sdp-only.md) (full trickle,
SDP-only offer/answer), [0011](adr/0011-signaling-session-routing.md) (server-owned signaling
sessions named by an opaque token) and [0012](adr/0012-join-patience-pac-timer.md) (join patience).
This file is the *what*, and is the only copy of it on this side of the wire.

## Opcodes

The byte constants in `SignalManager.cs` must match those in `SignalServer.js`.

| Opcode | Name | Direction |
|---|---|---|
| 0x02 | `attemptToJoinRoom` | client→server, server→host |
| 0x03 | `joinRoomCallback` | server→client |
| 0x04 | `receivedOfferFromHost` | host→server→client |
| 0x05 | `receivedAnswerFromClient` | client→server→host |
| **0x08** | `trickleToClient` | host→server→client |
| **0x09** | `trickleToHost` | client→server→host |

**0x06 is retired and must never be reused.** It was `trickleICE` under the pre-trickle design.
Trickle deliberately took 0x08/0x09 rather than reclaiming it, so that a stale peer meeting a new
message can never half-parse it. Any change here that appears to need a new opcode should stop and
reopen ADR 0011 instead.

## The session token

A **signaling session** is named on the wire by an opaque, server-minted token: ASCII, **22
characters** from a 62-character alphabet (`A–Z a–z 0–9`), drawn from an unpredictable source.

It is framed exactly as the room code is — one `uint8` length byte, then that many UTF-8 bytes.
**Never fixed-width, never null-terminated.** Written `[tok]` below, and written and read through the
session-token helper rather than by hand.

The host echoes the token on its offer, so the server correlates by direct lookup rather than by
scanning for the (host, client) pair. The client echoes it on its answer for the same reason; because
that lookup is what the anti-spoof scan used to provide implicitly, the server asserts explicitly
that the session's client player id equals the sending socket's.

## Byte layouts

Integers are little-endian.

| Message | Direction | Layout |
|---|---|---|
| `joinRoomCallback` 0x03 — success | server→client | `0x03` `0x01` `[tok]` |
| `joinRoomCallback` 0x03 — failure | server→client | `0x03` `0x00` |
| `attemptToJoinRoom` 0x02 — host notify | server→host | `0x02` `int32 clientPlayerId` `[tok]` |
| `receivedOfferFromHost` 0x04 | host→server | `0x04` `int32 targetClientPlayerId` `int32 connectionId` `[tok]` `sdp…` |
| `receivedOfferFromHost` 0x04 | server→client | `0x04` `int32 hostPlayerId` `sdp…` |
| `receivedAnswerFromClient` 0x05 | client→server | `0x05` `int32 targetPlayerId` `[tok]` `sdp…` |
| `receivedAnswerFromClient` 0x05 | server→host | `0x05` `int32 connectionId` `sdp…` |
| `trickleToClient` 0x08 | host→server | `0x08` `[tok]` `candidate…` |
| `trickleToClient` 0x08 | server→client | `0x08` `int32 connectionId` `candidate…` |
| `trickleToHost` 0x09 | client→server | `0x09` `[tok]` `candidate…` |
| `trickleToHost` 0x09 | server→host | `0x09` `int32 connectionId` `candidate…` |

An **outbound trickle carries no target player id** — the token alone selects the route on the
server. The server stamps `connectionId` on both inbound directions so one parser serves both: the
host uses it to pick `_server.connections[connectionId]`, the client ignores it and uses
`connectionWithServer`.

## Candidates and the end-of-candidates marker

`candidate…` is the remainder of the buffer read as UTF-8. **Zero remaining bytes is the
end-of-candidates marker** — it is a real message, not an absence, and it is emitted once per peer
connection per side at gathering-complete.

Browsers signal gathering-complete more than once, so the marker is emitted at most once per side;
the standalone and WebGL transports are required to behave identically here. One recorded divergence
in how the marker is *applied* on receipt is in [known-issues.md](known-issues.md).

## SDP

Offers and answers advertise `a=ice-options:trickle` and **carry no candidates** — there is no
bundled-candidate path in either transport, and `OfferAnswer` has no `candidates` field on either the
C# struct or the WebGL JSON.

`TrickledICE` (`Assets/CanoeWebRTC/Canoe/Common/Supporting.cs`) is the trickle payload. It carries
`candidate` plus **either** a `sessionToken` (outbound: the token is the entire route) **or** a
`connectionId` (inbound: the server stamps exactly one). No message carries both a sender and a
receiver. `TrickledICE.NoConnectionId` marks the outbound form, and shares its value with — but means
something unrelated to — the client-route sentinel in `SignalManager`.
