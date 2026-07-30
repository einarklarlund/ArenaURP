# 06 — Typed join-phase UI with a Cancel button

**What to build:** a player who joins a room sees which phase the join is actually in —
contacting the signal server, negotiating, establishing the connection, connected, failed — and
can **cancel** at any point and go back to the menu instead of staring at a spinner.

This is the UX half of ADR 0012's bargain: because the app now waits a patient 39.5s rather than
giving up early, a doomed join can hold the loading screen far longer than before, so the player
needs to see progress and needs a way out.

The loading view currently infers its state by subscribing to Unity's log stream and
**regex-matching log messages** — that goes away entirely, replaced by a typed local UI event
carrying the phase, fired from the points that already log those transitions.

**Blocked by:** 05 — the failed phase is fired from the teardown path that ticket adds.

**Status:** ready-for-agent

**Notes**

- The phase event is a **local** UI event, not a networked one — it describes this peer's own join
  attempt. Add it to the local channel and remember its reset line.
- Fire from where the transitions are already logged; do not invent new hooks or a new pub/sub
  channel for this (see the `cross-component-communication` skill).
- Cancel must stop the client cleanly and return to the menu — a cancelled join should leave no
  half-open connection behind, which is exactly the teardown 05 built.

- [x] A typed join-phase event exists on the local UI channel with the five phases, and is reset
      with the rest of the channel
- [x] It fires at each transition: signal server connected, offer/answer exchanged, connection
      started, and timeout or failure
- [x] The loading view switches on the phase and shows distinct text per phase
- [x] The log-message regex scrape is deleted, along with its log subscription
- [ ] A Cancel button stops the client and returns to the menu, leaving no half-open connection
- [ ] Manually verified for both a successful join and a join that times out

**Remaining**

- The Cancel path is implemented in C# (`RoomManager.CancelJoin` stops the client - and the server,
  if this peer was hosting - through FishNet, which runs the transport's own `CloseAll` teardown -
  then fires `LocalUIEvents.OnJoinCancelled`, which `UIManager` maps to `MainMenuView`). The button
  asset itself is still unauthored: `Assets/Prefabs/Managers/UIManager.prefab` → GameObject
  `Loading View` → add a child `Button` and assign it to the `LoadingView` component's
  `cancelButton` field. No inspector `onClick` wiring is needed; the view adds and removes its own
  listener. `LoadingView` warns and no-ops while the field is unassigned.
- Manual verification of a successful join and a timed-out join was not performed: the only running
  Unity Editor is attached to the main checkout, not this worktree. The change was type-checked by
  building the generated csprojs against this worktree's sources (0 errors).
- The `Connected` / `Failed` phases are published from `RoomManager` off
  `ClientManager.OnClientConnectionState` rather than from the transport's own log sites: the
  transport lives in the `CanoeWebRTC` assembly, which cannot reference `LocalUIEvents`
  (Assembly-CSharp). Same transitions, legal side of the assembly boundary.
