# Trickle ICE — outstanding Unity-editor work

Everything here needs a Unity Editor **pointed at this worktree**, and is deliberately *not*
done. Two categories: authoring that crosses the GUID boundary (scene/prefab serialized values,
which go through the Unity MCP and never through hand-edited YAML), and test runs.

The agents that built these tickets had no editor available — the only running instance is
attached to the main `ArenaURP` checkout. Nothing below was faked, and the corresponding ticket
checkboxes are left unticked.

## Authoring

| # | Asset | Object / component | Change | Ticket |
|---|---|---|---|---|
| A1 | `Assets/Scenes/Boot.unity` | `Prod NetworkManager` → `CanoeWebRTC` | `signalTimeoutDurationMilliseconds`: **80000 → 39500** | 05 |
| — | — | — | *A1 and A2 are already applied in `WebRtcJoinTests`, which configures the transport itself. Until they land in the scene the harness leads Boot rather than mirroring it; landing them makes the two agree again.* | — |
| A2 | `Assets/Scenes/Boot.unity` | `Prod NetworkManager` → `CanoeWebRTC`, `iceServers` | Replace the dead `stun:stun.stunprotocol.org:3478` (see below). **Now matters more:** under trickle a dead STUN server delays gathering-complete, which is what emits the end-of-candidates marker. | 03 |
| A3 | `Assets/Prefabs/Managers/UIManager.prefab` | `Loading View` → `LoadingView` | Add a child UI `Button` labelled "Cancel"; assign it to the `cancelButton` field. No inspector `onClick` wiring — the view adds/removes its own listener. | 06 |
| A4 | `Assets/Scenes/Boot.unity` | `Prod NetworkManager` → `CanoeWebRTC` | Drop the orphaned `candidateCollectDurationMilliseconds: 4000` (`Boot.unity:381`). The C# field is gone, so this key now points at nothing (see below). | 04 |

**On A2.** `Boot.unity` configures five ICE servers (`:365`, `:368`, `:371`, `:374`, `:377`) — four
Google hosts plus stunprotocol. The two distinct hosts were probed on
2026-07-24 with real STUN binding requests, not just DNS:

- `stun:stun.l.google.com:19302` — **OK**, binding response from 74.125.250.129
- `stun:stun.stunprotocol.org:3478` — **dead**, no A record at all, so it never answers

Suggested replacement: `stun:stun.cloudflare.com:3478`, which probes clean and is independently
operated (worth keeping a second operator for redundancy). This matters more under trickle than
it did before — a dead STUN server delays *gathering-complete*, which is the trigger for the
end-of-candidates marker, rather than merely wasting a slot in a bundle.

**On A3.** Until the button exists, `LoadingView` logs a warning on enable and the Cancel path
no-ops rather than throwing, so the scene is not broken in the meantime.

**On A4.** Harmless in the meantime — Unity ignores a serialized key with no matching field, and
nothing reads the value any more. It disappears the moment an editor pointed at this worktree
re-serializes `Boot.unity` (open the scene, touch the `CanoeWebRTC` component, save). Left unticked
because it is a serialized value that crosses the GUID boundary and no editor was available; the
hand-edit of scene YAML that would remove the line is exactly what this list exists to avoid.

## Test runs

| # | What | Ticket |
|---|---|---|
| T1 | `Arena.Tests` **EditMode** suite in-editor — the payload/token/queue tests. Their logic was verified out-of-editor (23 passed under a standalone NUnit harness), but the new `CanoeWebRTC` reference in `Arena.Tests.asmdef` has never been resolved by Unity itself. | 02 |
| T2 | `Assets/Tests/PlayMode/WebRtcSignalTimeoutLeakTests.cs` — written, never executed. | 05 |
| T3 | Manual: a successful join **and** a join that times out, watching the loading view's phase text and the Cancel button. | 06 |
| T4 | Ticket 01's PlayMode ICE harness against the trickle path, to confirm the join is well under a second. **No join time has been measured** — 01's harness is in a separate, unmerged worktree, so there is neither a run nor a baseline. Ticket 03's code-level claim is only that both fixed gather stalls are gone. | 03 |
| T5 | Standalone ↔ standalone join in-editor (two ParrelSync instances) against a Node 20 signal server, watching candidates trickle both ways and the end-of-candidates marker land once per side. The wire bytes were verified against `SignalServer.js`'s own parsing expressions out of editor, but no real handshake has been run. | 03 |
| T6 | Manual: a **two-browser join** over a locally served dev build (`./deploy_local.sh`), connecting in well under a second. | 04 |
| T7 | Manual: a **browser client joining an editor host** — the cross-transport join that was the documented green-gap between tickets 03 and 04. | 04 |

**On T6/T7 — nothing has executed the WebGL path.** Between them they are the *only* way to run the
jslib at all: real browser ICE, real mDNS, real emscripten marshalling of the two new callbacks.
Neither was run, so the WebGL half of ticket 04 has **no runtime evidence whatsoever** — only a
compile of the `UNITY_WEBGL && !UNITY_EDITOR` C# under a stripped define set, a `node --check` of the
jslib, and a Node harness over the extracted `Connection` class. Treat "WebGL works" as unverified
until one of these is green.

Note for whoever runs them: the WebGL-dev and Local `Bootstrap` paths add the transport with **zero
ICE servers** — host and mDNS candidates only. Fine for a LAN test, but it does **not** exercise
STUN, so A2 is not covered by passing T6/T7.

Watch for, specifically:

- exactly **one** empty end-of-candidates trickle per peer connection per side (browsers signal
  gathering-complete twice — a null `onicecandidate` and `onicegatheringstatechange`);
- the host's first candidates arriving at the server only **after** its offer, never before (the
  server silently drops a trickle into a session with no bound `connectionId`, so a mis-ordering
  shows up as missing candidates and a slow or failed join, not as an error);
- no `Failed to add ICE candidate` spam from candidates applied before the remote description.
