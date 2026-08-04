# Architecture

Why the netcode is shaped the way it is. Most of what follows is here because the obvious
alternative was tried first and broke in a specific way — those failures are recorded
alongside the decisions, because without them the code looks needlessly complicated.

## The shape of it

```
NetworkBootstrap     connects: host / join, capacity, disconnects, LAN discovery
     |
TriviaNetworkSync    router: owns the queue and every live match, sends per-match messages
     |
Matchmaker           who plays whom, grouped by skill, one queue entry per player
     |
MatchSession         ONE running match. Server-side, no UI. Several exist at once.
     |
TriviaDuelManager    the view. Renders what arrives. Also the 1v1 rules for local play.
TeamDuelManager      the same for 2v2.
```

The single most important rule: **`MatchSession` is the authority, every client is only a
view — including the host.** Under the old design the host's `TriviaDuelManager` was both
the authority and its own view, which worked only while exactly one match existed.

## Server authority

The server decides everything. Clients forward taps and render what comes back; they never
decide a score, a round state, or who may answer.

Two mechanisms, used for different things:

- **`NetworkVariable`** for state a late joiner must catch up on. Note these only fire
  `OnValueChanged` for changes *after* spawn — a value already in place at spawn time
  never fires. `OnNetworkSpawn` explicitly reads current values to catch up.
- **`ClientRpc`** for events, targeted with `ClientRpcParams.Send.TargetClientIds` so a
  message only reaches the match it belongs to.

Those targeted RPCs deliberately **do not skip the server**. The host is a player like any
other and needs to render its own match.

## Concurrent matches

Pressing Start joins a queue rather than starting a match. Whenever anyone readies up, the
matchmaker forms every complete group it can: 4 players ready for 1v1 makes 2 matches, 6
makes 3, 8 makes 4. Leftovers stay queued.

**Mode is per player, not per room.** Each player queues for what they picked on their own
device, and both modes drain independently — a 1v1 and a 2v2 can be forming at once.

> Making the mode room-wide (so clients could change it at all) meant one phone tapping 2v2
> switched every other phone, as though four devices were one. A related bug: the queue was
> single-mode and got *cleared* on a mode change, so one player picking 2v2 threw everyone
> already waiting back out.

## Per-match identity

Every client holds a `PlayerSideIdentity` for **every** connected player, and seat numbers
restart per match — match 1 and match 2 both have a "side 1". So each identity carries a
`MatchId`, and name/avatar pushes are gated on sharing a match with the local player.

Seats are assigned **only** by `AssignSeatsForMatch`, never at spawn, and are cleared back
to 0 when a match ends. 0 means "not seated yet" and nothing renders.

> Both of these are scar tissue. Spawn used to give side 1 to the host and side 2 to
> everyone else — fine with one match, wrong the moment two run at once: a match of two
> non-host players had both carrying a stale side 2, and whichever pushed last owned both
> name slots. The symptom was a duel showing the same player on both sides.

## Connection

- The host binds **`0.0.0.0`**, not its own LAN address, so it accepts loopback (Editor
  virtual players) and Wi-Fi (phones) at the same time.
- `ConnectionApproval` must be set on **every** device, not just the host — `NetworkConfig`
  is hashed and compared during the handshake, so setting it on one side only makes every
  client fail to connect.
- Capacity is a constant (`MaxRoomPlayers = 8`), deliberately **not** derived from the
  selected mode: players connect when the app opens, long before anyone picks a mode.

### Discovery

Hosts announce themselves on UDP 47777, and searching clients **also actively ask**, with
hosts replying unicast.

> Broadcast alone was not enough. Android needs a `MulticastLock` to reliably receive
> packets sent to 255.255.255.255, so a phone opened before the host would listen forever
> and hear nothing. A unicast reply needs no such permission.

Broadcast rather than multicast for the same reason: multicast needs the lock, broadcast
does not, and home routers pass broadcast by default.

### Lifecycle

`NetworkBootstrap` shuts the transport down on `OnDestroy`, `OnApplicationQuit` **and
`OnApplicationPause`**. Android usually does not kill the process when an app is swiped
away, so without the pause hook the next launch inherited a bound socket and a
`NetworkManager` that still believed it was connected — the app had to be opened twice.

The trade: backgrounding the app mid-match disconnects you.

## Gotchas worth knowing

- **`IsServer` is not enough to send an RPC.** It stays true briefly after a shutdown while
  `IsListening` has already gone false. Send guards use `CanSendRpc`, which checks both.
- **`[ServerRpc(RequireOwnership = false)]` cannot be migrated** to
  `[Rpc(SendTo.Server, ...)]` while the method takes `ServerRpcParams` — netcode's ILPP
  rejects it outright. Those deprecation warnings are deliberate.
- **1v1 and 2v2 are separate state machines on purpose.** They share a router, not rules.
  Merging them means one state machine with a mode flag threaded through every branch.

## UI scaling

The UI was authored at **1179 × 2556**. One `CanvasStretchFitter` on a single `UIRoot`
scales that to fill any screen, so every element keeps the anchors it was authored with.

The Canvas Scaler must be **Constant Pixel Size, scale 1** — otherwise it and the fitter
both scale and you get the two multiplied together. `Trivia Duel > Setup` has menu items
for this; run either the stretch or the scaler, never both.

## Open questions for review

- Match difficulty is the **average** of its players' levels, so a strong player matched
  with a weak one gets questions between the two. Is that the right call, or should it
  follow the stronger player, or adapt mid-match?
- IQ is stored per device with no server validation — trivially edited, and lost on
  reinstall. Fine for testing, not for release.
- There is no reconnect. A dropped player's match aborts for everyone in it.
