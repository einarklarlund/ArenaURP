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
| A2 | `Assets/Scenes/Boot.unity` | `Prod NetworkManager` → `CanoeWebRTC`, `iceServers` | Replace the dead `stun:stun.stunprotocol.org:3478` (see below). **Now matters more:** under trickle a dead STUN server delays gathering-complete, which is what emits the end-of-candidates marker. | 03 |
| A3 | `Assets/Prefabs/Managers/UIManager.prefab` | `Loading View` → `LoadingView` | Add a child UI `Button` labelled "Cancel"; assign it to the `cancelButton` field. No inspector `onClick` wiring — the view adds/removes its own listener. | 06 |

**On A2.** `Boot.unity` configures two ICE servers (`:365`, `:377`). Both were probed on
2026-07-24 with real STUN binding requests, not just DNS:

- `stun:stun.l.google.com:19302` — **OK**, binding response from 74.125.250.129
- `stun:stun.stunprotocol.org:3478` — **dead**, no A record at all, so it never answers

Suggested replacement: `stun:stun.cloudflare.com:3478`, which probes clean and is independently
operated (worth keeping a second operator for redundancy). This matters more under trickle than
it did before — a dead STUN server delays *gathering-complete*, which is the trigger for the
end-of-candidates marker, rather than merely wasting a slot in a bundle.

**On A3.** Until the button exists, `LoadingView` logs a warning on enable and the Cancel path
no-ops rather than throwing, so the scene is not broken in the meantime.

## Test runs

| # | What | Ticket |
|---|---|---|
| T1 | `Arena.Tests` **EditMode** suite in-editor — the payload/token/queue tests. Their logic was verified out-of-editor (23 passed under a standalone NUnit harness), but the new `CanoeWebRTC` reference in `Arena.Tests.asmdef` has never been resolved by Unity itself. | 02 |
| T2 | `Assets/Tests/PlayMode/WebRtcSignalTimeoutLeakTests.cs` — written, never executed. | 05 |
| T3 | Manual: a successful join **and** a join that times out, watching the loading view's phase text and the Cancel button. | 06 |
| T4 | Ticket 01's PlayMode ICE harness against the trickle path, to confirm the join is well under a second. **No join time has been measured** — 01's harness is in a separate, unmerged worktree, so there is neither a run nor a baseline. Ticket 03's code-level claim is only that both fixed gather stalls are gone. | 03 |
| T5 | Standalone ↔ standalone join in-editor (two ParrelSync instances) against a Node 20 signal server, watching candidates trickle both ways and the end-of-candidates marker land once per side. The wire bytes were verified against `SignalServer.js`'s own parsing expressions out of editor, but no real handshake has been run. | 03 |

## Not yet reached

Ticket 04 adds more of both — deleting the collect-duration serialized value from the Boot scene,
and the manual two-browser join over a locally served dev build, which is the **only** path that
exercises the jslib plus real browser ICE plus mDNS. That is the gate for ticket 04, not an
optional extra.
