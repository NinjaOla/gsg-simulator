# Console Game Loop Plan

Status: **Phases 1–5 done** (4d Steam transport is a later, separate phase).
Related: `adr-001-mp-runtime-akka.md`, `multiplayer-implementation-plan.md`, `.github/copilot-instructions.md`.

## Goal

A playable console game loop with the same flow for single-player and multiplayer:

1. Start the app → static content (world catalog) is available.
2. Main menu → **Single player** / **Host multiplayer** / **Join multiplayer** / **Load game** / **Quit**.
3. Lobby → SP: only yourself, starts immediately. MP: players join until the host starts.
4. The game loads into an active session that **ticks on its own** at a chosen speed.
5. Players pause/unpause and change speed while the game runs; commands can be typed while it ticks.

SP vs MP stays purely wiring (who hosts + how many clients connect). The session actor remains the sole simulation authority.

## Prerequisite (done)

The console no longer reads the server's engine. `GameSession.Engine` and `ILocalEngineProvider` are removed; clients read dynamic state from `SessionStateCache` (`GameClient.SubscribeAsync`) and static data from `StaticWorld` (`SimEngine.Game.Worlds`). Joining presents the local content hash.

## Design Decisions

| # | Topic | Decision |
|---|---|---|
| D1 | Command permissions | Any player may issue any command. Per-game **settings** (chosen by the host in the lobby) decide whether it is allowed. The server validates and rejects with a reason. |
| D2 | Speed | Levels 1–5. Each level is a **minimum wall-clock time per tick**. The next tick starts at `max(previousStart + interval, previousFinish)`. 1 = 5 s, 2 = 2 s, 3 = 1 s, 4 = 0.25 s, 5 = no wait (as fast as possible). |
| D3 | `step` | A **debug session command**: the session actor runs N ticks immediately. Only allowed while paused and when the game's settings permit debug commands. Ticks still run through the session actor, so results are identical to normal play. |
| D4 | Typing while running | The game keeps ticking while a player types commands. |
| D5 | Server process | SP: in-process local server (current). MP host from the game: spawns a dedicated `--server` child process and connects as a remote client; the child exits when the parent process dies (`--parent-pid` watchdog, covers crashes). A standalone `--server` (no parent) runs independently. |

---

## Phase 1 — Server: session lifecycle, settings, tick scheduler

**Goal:** sessions have a lobby stage, start on host command, tick on their own at a chosen speed, and validate every command against game settings.

### Work

1. **Session stages** in `GameSessionActor`: `Lobby → Running ⇄ Paused → Ended`.
   - `Initialize` / `InitializeFromSave` leave the session in `Lobby` (engine loaded, not ticking).
   - Join is only accepted in `Lobby` (reconnect handling is out of scope).
   - First player to join becomes **host** (or the creator passes the host id explicitly).
2. **Game settings** contract, e.g. `GameSettings`:
   - `PauseControl`: `HostOnly | Anyone`
   - `SpeedControl`: `HostOnly | Anyone`
   - `AllowDebugCommands`: `bool`
   - `InitialSpeed`: `1–5`
   - Host may change settings only while in `Lobby`.
3. **Issuer identity on commands.** `PlayerCommand` has no sender today. Add the issuing `PlayerId` to session command messages (e.g. `SessionProtocol.EnqueueCommand(SessionId, PlayerId, Command)`); `PlayerActor` forwards its own id. The session rejects commands from non-members.
4. **New commands/messages** (contracts):
   - `StartGame` (host only, `Lobby → Running`)
   - `PausePlayerCommand` / `ResumePlayerCommand` (exist; now validated against `PauseControl`)
   - `SetSpeedPlayerCommand(int Speed)` (validated against `SpeedControl`, range 1–5)
   - `DebugStep(int Ticks)` replaces the manual `Advance` path for clients (D3)
   - `UpdateSettings(GameSettings)` (host only, `Lobby` only)
   - Rejections reply with a typed failure (e.g. `CommandRejectedException` with a reason).
5. **Tick scheduler** in `GameSessionActor`:
   - Self-message `Tick` scheduled via Akka scheduler; one engine step per `Tick` message so pause/speed messages are processed between ticks, including at speed 5.
   - Timing per D2: measure step duration, schedule the next `Tick` at `max(0, interval − elapsed)`.
   - Queued player commands are applied at the tick boundary (existing `DeferredEventBus` model).
   - Paused/Lobby/Ended: no `Tick` scheduled.
6. **Per-client delivery with backlog (no global update rate):**
   - Every tick produces one `SessionStreamUpdate`, sent to every subscriber before the next tick runs. Clients that keep up get every tick.
   - Each update carries its tick number; clients **ack** the last tick applied (`SessionProtocol.AckUpdate`). Akka remoting has no per-receiver backpressure, so acks are how the server detects lag.
   - The session keeps a per-subscriber backlog of unacked updates. When a client is more than *K* ticks behind (setting, e.g. 10), its backlog is **merged** into one update (latest tick/date, latest absolute treasury per country, events concatenated and capped) and sent as a catch-up. Absolute values make merging safe.
   - A client far behind (e.g. backlog over a hard cap) is resynced with a fresh `SessionSnapshot` instead.
   - Lagging clients never slow down the server or other clients; the simulation stays authoritative regardless of client speed.
7. **Session status in the read model:** add `Stage`, `Speed`, `HostPlayerId`, `Settings`, and `Players` to `SessionSnapshot`; add stage/speed changes to `SessionStreamUpdate` so clients see pause/resume/speed immediately (also pushed when no tick runs).
8. **Lobby actor:** `GameListing` gains `Stage` and player count so join screens can filter open games.

### Files (expected)

- `src/SimEngine.Contracts/Commands/PlayerCommand.cs`, `Messages/SessionProtocol.cs`, `SessionSnapshot.cs`, `SessionStreamUpdate.cs`, `GameListing.cs`, new `GameSettings.cs`, `SessionStage.cs`, `CommandRejectedException.cs`
- `src/SimEngine.Server/Actors/GameSessionActor.cs`, `PlayerActor.cs`, `LobbyActor.cs`
- `src/SimEngine.Client/SessionClient.cs`, `SessionStateCache.cs`

### Tests (`tests/SimEngine.Server.Tests`)

- New session is in `Lobby` and does not tick.
- Only the host can `StartGame`; non-host is rejected.
- Join after start is rejected.
- Running session ticks without client requests; speed 1 ticks no faster than 5 s (use short test intervals via injectable speed table).
- Pause stops ticking; resume continues; stage changes reach subscribers.
- `PauseControl = HostOnly` rejects non-host pause; `Anyone` accepts.
- `SetSpeed` out of range rejected; `SpeedControl` enforced.
- `DebugStep` rejected while running and when `AllowDebugCommands = false`; accepted while paused.
- Determinism: N ticks via scheduler produce a save byte-identical to N direct engine steps (extend `ActorEngineParityTests`).
- Speed 5 still processes a pause between ticks.
- A subscriber that acks every update receives one update per tick.
- A subscriber that stops acking receives a merged catch-up update once *K* ticks behind; its cache ends up equal to the authoritative snapshot. Other subscribers are unaffected.
- A subscriber past the hard cap is resynced with a snapshot.

### Done when

All of the above tests pass, existing tests are updated for the new command signatures, and no client can advance the simulation except through `StartGame`/resume/`DebugStep`.

---

## Phase 2 — Console: live game screen

**Goal:** the in-game screen updates as ticks arrive and accepts hotkeys and commands without stopping the game (D4).

### Work

1. Replace the blocking `TextPrompt` loop in `GameLoop` with a **live render loop**:
   - Header: date, tick, speed, stage (Running/Paused), host/player info.
   - Redraw on cache change (`SessionStateCache` change notification or polling at ~10 Hz).
2. **Input without blocking:** poll `Console.KeyAvailable`.
   - `Space` → pause/resume
   - `+` / `-` → speed up/down
   - `Enter` → command line (type a command, e.g. `countries`, `province 12`, `save x`); the game keeps ticking while typing.
   - `Esc` → in-game menu (save / quit to main menu).
3. Rejected commands (D1) show the server's reason inline.
4. `step` command → `DebugStep`; shows a clear message when not paused or not allowed.
5. Event log panel shows the latest `SessionStateCache.Events`.

### Files (expected)

- `src/SimEngine.Game.Ui.Console/Game/GameLoop.cs`, `GameSession.cs`, `Game/Commands/StepCommand.cs`, new pause/speed handling
- `src/SimEngine.Client/SessionStateCache.cs` (change notification, stage/speed)

### Done when

Starting a game shows the date advancing on its own; space/+/- work; commands run while ticking; quitting returns to the main menu and shuts the session down.

---

## Phase 3 — Main menu and single-player lobby

**Goal:** SP goes through the same lobby path as MP, with one player.

### Work

1. `MainMenu`: **Single player / Host multiplayer / Join multiplayer / Load game / Quit**.
2. SP flow: pick world, start date, seed → create game via `GameClient.CreateGameAsync` (lobby actor) → join with local content hash → lobby screen (settings, one player) → `StartGame` → live game screen.
3. Optional "quick start" that skips the lobby screen for SP but still sends the same messages.
4. Replace `NewGameFlow` direct `SessionClient.InitializeAsync` with the lobby path.

### Files (expected)

- `src/SimEngine.Game.Ui.Console/App.cs`, `Ui/MainMenu.cs`, `Ui/NewGameFlow.cs` (→ SP flow), new `Ui/LobbyScreen.cs`
- `src/SimEngine.Game.Ui.Console/Game/GameSessionFactory.cs`

### Done when

SP: menu → world → lobby (self only) → start → ticking game → quit back to menu, with no SP-only code path in server or client.

---

## Phase 4 — Multiplayer host and join

**Goal:** a player can host from the console and others can join over the network. Networking must work over **direct TCP** now and over **Steam networking** (SteamNetworkingSockets / Steam Datagram Relay) later, without touching the server actors, the client API, or the UI flows.

### Akka stays; only the client edge changes

Akka.NET remains the server runtime for the lobby, sessions, players, ticking, and future clustering. The only open question is how a **client process** reaches the actors. The original plan used Akka.Remote (`GameClient.ConnectRemoteAsync`) for that. Phase 4 instead puts a thin **network gateway** in front of the actors. This is the pattern in the reference architectures:

- The *Architectural Blueprint* diagram: Steam socket thread → `Tell()` → `NetworkGateway` actor → per-user actor → domain actors.
- scalable-solutions: akka-http + WebSocket in front of the actors; it authenticates and validates the JSON, then tells actors.
- chester.codes: SignalR → `GameHub` actor in, `ClientResponder` actor out.
- Only the gigi.nullneuron demo uses Akka.Remote straight to clients. It is a trusted-LAN demo.

Why not Akka.Remote to the client:

1. **Steam can't be plugged in cleanly.** Akka.Remote addresses peers as `host:port` over DotNetty TCP. Steam peers are SteamIDs over a relayed, message-based API, read on a callback/poll thread. A custom Akka.Remote transport is possible but couples us to remoting internals. A gateway just `Tell`s what the Steam thread reads, exactly like the diagram.
2. **Security.** An Akka remoting endpoint lets any connected peer send any message to any actor path (`Shutdown`, `RemoveGame`, `Save` with an arbitrary server path, `InitializeFromSave`) and impersonate any `PlayerId`. `akka.remote.untrusted-mode` only narrows `ActorSelection` paths. It doesn't bind identity or validate payloads, and Akka documents remoting for trusted networks.
3. **Identity.** `PlayerId` comes from the client today. It must be bound to the connection instead: server-assigned for TCP, the authenticated SteamID for Steam.

Akka.Remote/Cluster stays available **between server nodes** (trusted) if clustering is added later.

```
Client process                                         Server process (Akka ActorSystem)
GameClient ─ ClientConnection ─ ITransportConnection ══ ITransportListener ─(Tell)→ GatewayActor
              (request/reply,     (InProc | Tcp | Steam)   (socket/Steam thread)        │ spawns 1 per connection
               push dispatch)                                                          ConnectionActor ── identity, decode, authorization
                                                                                        │
                                                                                       PlayerActor (per user) ⇄ Lobby / GameSessionActor
```

Mapping to the blueprint: Steam callback thread = `SteamTransport` listener pump; `NetworkGateway` = `GatewayActor`; `PlayerSession (per user)` = `ConnectionActor` (transport-bound) + existing `PlayerActor` (game-bound); `World/Region domain actors` = `LobbyActor` / `GameSessionActor`. Keeping the connection and the player separate means a dropped connection doesn't destroy the player, which prepares for reconnect later.

SP uses the same path with the in-process transport, so SP and MP stay pure wiring (copilot-instructions "same transport" rule still holds; the transport becomes the wire protocol instead of Akka messages).

### Design decisions

| # | Topic | Decision |
|---|---|---|
| N1 | Boundary | Clients never hold server `IActorRef`s. All client↔server traffic is `ClientMessage` / `ServerMessage` wire records. |
| N2 | Transport shape | **Message-oriented, reliable, ordered**, max message size 16 MB. TCP adds length-prefix framing; Steam fragments above its 512 KB reliable limit. An unreliable channel can be added later. |
| N3 | Endpoints | Opaque `TransportEndpoint` URIs: `inproc://<name>`, `tcp://host:port`, later `steam://<steamid64>`. A `TransportRegistry` maps a scheme to a connector. |
| N4 | Serialization | `System.Text.Json` with source-generated, polymorphic contexts (`[JsonDerivedType]` on `ClientMessage`, `ServerMessage`, `PlayerCommand`). Only listed types are deserialized (no type names on the wire). In-process connections also serialize, so SP tests catch wire issues. All encoding sits behind `IWireCodec`, so moving to MessagePack later only touches the codec and type attributes. To keep that switch cheap, wire records are plain immutable records with arrays and no STJ-only features (no converters with custom semantics, no `JsonExtensionData`). |
| N5 | Identity | Handshake `Hello(ProtocolVersion, PlayerName)` → `Welcome(PlayerId)`. The server assigns the `PlayerId` (TCP/InProc: new id; Steam: SteamID). Every later request uses the connection's id, and client-supplied ids are removed from the wire. |
| N6 | Authorization (gateway) | Host-only: `StartGame`, `UpdateSettings`, `RemoveGame`, `Save`. `Save` takes a **file name** only, which is sanitized and resolved under the server's save directory. `InitializeFromSave`, `Shutdown`, and raw paths are not exposed remotely (Phase 5 adds a load-by-name request). |
| N7a | Per-user actors | `ConnectionActor` handles wire concerns only (decode, identity, auth, send). Game requests go through the player's `PlayerActor`, which keeps buffering/forwarding commands and survives connection loss. |
| N7 | Streaming | `ConnectionActor` is the session subscriber. It forwards `SessionStreamUpdate`/`SessionSnapshot` to the wire and relays client `AckUpdate` messages back. Acks stay end-to-end, so the Phase 1 backlog/merge/resync logic works unchanged for slow network clients. |
| N8 | Liveness | Transport-level `Ping`/`Pong` every 5 s, closed after 15 s silence (TCP only; Steam has its own). `Closed` is surfaced as a task on the connection. |
| N9 | Disconnect | Server: connection closed → `Leave` in Lobby; while Running/Paused the player stays a member, marked *disconnected* (reconnect is out of scope). **If the host leaves or disconnects (any stage), the session ends.** Remaining clients get `Disconnecting("Host left")` and the game is removed from the lobby. Client: connection closed → current flow ends → main menu with reason. |
| N10 | Hosting process | Per D5: MP host spawns a child `--server`. Child starts → binds → prints one line `READY <endpoint>` on stdout → parent connects. Child stdout/stderr go to a log file under `GameTempDirectory` after the ready line. `--parent-pid` watchdog stops the child when the parent exits. |
| N11 | Discovery | Out of the transport: the join UI produces a `TransportEndpoint`. Direct = typed `host:port`. Steam (later) = invite/lobby browser yields `steam://…`. Then the same flow: connect → `ListGames` → join. |

### Steam implications (design only, not implemented in Phase 4)

- The listen socket identity is a SteamID, so the Steam listener must live in a process that has Steam initialized. Options, to be decided when Steam is added:
  - **(a)** The child server uses the Steam *GameServer* API (own SteamID, SDR supported).
  - **(b)** The host console owns the Steam listen socket and **bridges** each Steam connection to the child over loopback TCP, forwarding the verified SteamID in a bridge header.
  - **(c)** Steam-hosted games run the server in-process.
- The abstractions above support all three. Only (b) needs extra work: a trusted bridge handshake (a per-launch secret passed to the child) so a loopback client can't claim an arbitrary SteamID.
- Steam lobbies (`ISteamMatchmaking`) can replace typed addresses for discovery and invites (N11). The game lobby itself stays in `LobbyActor`.
- **SDR tickets / Game Coordinator are not needed for player-hosted games.**
  - P2P sockets (`CreateListenSocketP2P` / `ConnectP2P`) already relay through SDR without exposing IPs. Steam authenticates both SteamIDs with its own certificates.
  - Signed connection tickets from a Game Coordinator are only needed for *hosted dedicated servers* reached via `ConnectToHostedDedicatedServer` (studio-run server fleets). That is out of scope.
- The Steam API is not thread-safe for our purposes. The Steam listener pumps callbacks and `ReceiveMessages` on one dedicated thread and only `Tell`s into Akka. Sends from actors go through a queue drained by that thread.

### Production notes (not in Phase 4)

- **Tick timer:** `GameSessionActor` schedules ticks with `Context.System.Scheduler.ScheduleTellOnceCancelable`. Switching to `IWithTimers` (auto-cancel on stop/restart) is a small cleanup and can ride along in 4a.
- **Crash recovery:** the engine is one deterministic `SimulationEngine` owned by `GameSessionActor`; it isn't spread over province actors. So Akka.Persistence per province doesn't fit. The natural equivalent is snapshot + command journal: periodic autosave via `Save()` plus the accepted commands per tick since then. Replay is exact because the engine is deterministic. This can use Akka.Persistence on `GameSessionActor` (snapshot store = save bytes, journal = accepted command batches) in a later phase.

### Work

#### 4a — Wire protocol, transport abstraction, gateway (SP migrated, no behavior change) — DONE

**As built (differences from the plan below):**

- **Transport API:**
  - One `ITransport` per scheme (`ListenAsync` + `ConnectAsync`); there is no separate connector type.
  - `ITransportConnection.TrySend` is non-blocking and preserves call order (safe to call from actors).
  - `Completion` replaces `Closed`.
- **Listener ownership:** callers bind the listener first and pass it to `WithSimEngineGateway(listener, options)`. The gateway is a `GatewayActor`, not a hosted service. This lets `--server` print the bound endpoint (4b).
- **N7a (per-user actors) deferred:** `ConnectionActor` talks to the session directly with its bound player id. Routing through `PlayerActor` comes with reconnect.
- **N9 (disconnect) simplified:** a closed connection always sends `Leave`. There is no "disconnected member" yet; that comes with `PlayerInfo` in 4c. The host leaving ends the session; the actor stays as an `Ended` tombstone until the lobby prunes it.
- **Session-scoped requests:**
  - They carry the `SessionId`, which must match the joined session (`NotJoined` otherwise).
  - `JoinGame` implicitly leaves the previous session.
  - Stream messages carry `SessionId`.
- **Load:** done through `LobbyProtocol.CreateGameFromSave` (pulled forward from Phase 5, step 1). `GatewayOptions.AllowArbitrarySavePaths` is enabled for the trusted single-player gateway only. Remote clients get plain file names under `SaveDirectory`, and reserved device names are rejected.
- **Trusted Akka access:** the old Akka facades moved to `SimEngine.Server.Direct` (`ActorGameClient`/`ActorSessionClient`) for tests and tools.
- **Akka still reachable from the client:** `SimEngine.Client` has no Akka package reference, but still gets Akka transitively because `SimEngine.Contracts` holds the server actor protocols. Moving `*Protocol` into the server is a follow-up.
- **Ping/Pong:** not added; only TCP needs it (4b).

**Original plan:**

1. **New project `SimEngine.Networking`** (no Akka dependency; referenced by Server and Client):
   - `ITransportConnection` — `ValueTask SendAsync(ReadOnlyMemory<byte>, CancellationToken)`, `ChannelReader<ReadOnlyMemory<byte>> Incoming`, `string RemoteDescription`, `Task Closed`, `IAsyncDisposable`.
   - `ITransportListener` — `IAsyncEnumerable<ITransportConnection> AcceptAsync(CancellationToken)`, `TransportEndpoint LocalEndpoint`, `IAsyncDisposable`.
   - `ITransportConnector` — `Task<ITransportConnection> ConnectAsync(TransportEndpoint, CancellationToken)`.
   - `TransportEndpoint` (scheme + address, `Parse`/`ToString`), `TransportRegistry` (scheme → connector).
   - `InProcTransport` — paired `Channel`s; named listeners in a process-local hub.
2. **Wire protocol** in `SimEngine.Contracts/Wire/` (records only; Contracts keeps its Akka reference for server messages):
   - `ClientMessage` (each request has `long RequestId`): `Hello`, `ListWorlds`, `ListGames`, `CreateGame`, `RemoveGame`, `JoinGame(SessionId, ContentHash)`, `LeaveGame`, `StartGame(Paused)`, `UpdateSettings`, `SendCommands(PlayerCommand[])`, `DebugStep(Ticks)`, `SaveGame(FileName)`, `GetStatus`, `Subscribe`, `Unsubscribe`, `AckUpdate(Sequence)` (no reply), `Ping`.
   - `ServerMessage`: `Welcome(PlayerId, ServerVersion)`, `Reply(RequestId, payload)`, `ErrorReply(RequestId, ErrorCode, Message, Details)`, `StreamUpdate(SessionStreamUpdate)`, `StreamSnapshot(SessionSnapshot)`, `Disconnecting(Reason)`, `Pong`.
   - `ErrorCode`: `ContentMismatch`, `CommandRejected`, `NotHost`, `NotMember`, `WrongStage`, `UnknownSession`, `ProtocolVersionMismatch`, `BadRequest`, `Internal`. The client maps them back to `ContentMismatchException` / `CommandRejectedException`. `ContentMismatch` details carry the server's world id, content version, and hash so the client can say what differs.
   - `WireCodec` — STJ source-gen context and a single `Serialize`/`Deserialize` entry point. Reject unknown discriminators and enforce max size.
   - Check that `SessionSnapshot`, `SessionStreamUpdate`, `GameSettings`, `GameListing`, `SessionStatus`, `WorldOption`, and `TickResult` round-trip (immutable arrays/dictionaries, `DateTimeOffset`, `ulong`).
3. **Server gateway** (`SimEngine.Server/Gateway/`):
   - `ConnectionActor` — owns one `ITransportConnection` (pumps `Incoming` into self-messages and sends replies). Handles the handshake, then maps requests to `LobbyProtocol`/`SessionProtocol` using its own `PlayerId` and applies N6. Also handles N7 subscriptions and N9 disconnects. Unknown or bad frames → `ErrorReply`; repeated abuse → close.
   - `GatewayHostedService` — for each configured `ITransportListener`, accepts connections and spawns `ConnectionActor`s under `/user/connections`.
   - `SimEngineServerExtensions.WithSimEngineGateway(params TransportEndpoint[])` registers the gateway.
4. **Client rewrite on top of the connection** (`SimEngine.Client`, Akka reference removed):
   - `ClientConnection` — handshake, `RequestId` correlation (`TaskCompletionSource` map), push dispatch to subscriptions, and `Closed`/`DisconnectReason`.
   - `GameClient.ConnectAsync(TransportEndpoint, string playerName, TransportRegistry, CancellationToken)` → `GameClient` (`IAsyncDisposable`) with `PlayerId`. It keeps `CreateGameAsync`/`ListGamesAsync`/`GetGameAsync`/`RemoveGameAsync`/`ListWorldsAsync`/`GetSession`/`SubscribeAsync`.
   - `SessionClient` — same methods minus `playerId` parameters, `InitializeAsync`, `InitializeFromSaveAsync`, and `ShutdownAsync`. `SaveAsync(fileName)`.
   - `SessionSubscription` — no actor. It applies pushes to `SessionStateCache` and sends `AckUpdate`, keeping the subscribe-then-snapshot ordering.
   - `FromLocalRegistry` / `ConnectRemoteAsync` are removed.
5. **Console SP** (`Program.cs`): start the server host with the gateway on `inproc://local`, then `GameClient.ConnectAsync(inproc://local)`. `GameSessionFactory` uses the server-assigned `PlayerId` instead of `"local"`. `GameSession` dispose: leave the game, and remove it only when this client is host and the server is host-owned (SP).
6. **Tests:**
   - Existing actor-level tests keep talking Akka. Move today's Akka facades into the test project as `ActorSessionClient`/`ActorGameClient` helpers so the test bodies barely change.
   - New gateway tests use in-proc: handshake, version mismatch, spoofing (a non-host can't `StartGame`), `Save` path traversal rejected, content mismatch details, two clients see identical ticks, lagging client (no acks) gets merged updates/snapshot through the gateway, and closing the connection in the lobby removes the player.
   - Wire codec round-trip tests for every message and `PlayerCommand` type.

#### 4b — TCP transport and server process — DONE

**As built (differences from the plan below):**

- **Liveness:** no Ping/Pong messages. A zero-length frame is a heartbeat; both sides send one every 5 s and close after 15 s without receiving anything (`TcpTransportOptions`). Empty data messages are therefore rejected by `TcpTransport.TrySend`.
- **Close:** disposing a TCP connection flushes queued frames (up to 2 s) before closing, so a final `Disconnecting` reaches the peer.
- **`READY` line:** `READY <endpoint> [<endpoint>...]`, one entry per `--listen`.
- **Watchdog:** `ParentProcessWatchdog` lives in `SimEngine.Server` so it is testable without the console (which can't build without the Six Labors license). The test uses a short-lived process instead of spawning the console server.
- **Gateway actor names** get a counter suffix so one server can listen on several endpoints of the same scheme.
- `Akka.Remote` is no longer referenced by the console or the spike (`Akka.Cluster` still pulls it in for future server clustering). The spike's external client and broadcast measurements now go through the TCP gateway and `GameClient`.

**Original plan:**

1. `TcpTransport` in `SimEngine.Networking`: `TcpListener`/`Socket` with 4-byte big-endian length prefix, max frame 16 MB, `NoDelay`, ping/pong (N8), and send serialization via a channel. Port 0 means ephemeral, reported via `LocalEndpoint`.
2. `ServerMode`:
   - Replace `--host/--port` + Akka remoting with `--listen <endpoint>` (repeatable; default `tcp://0.0.0.0:8110`). Keep `--host`/`--port` as shorthand for `tcp://host:port`.
   - Add `--parent-pid <pid>` (watchdog: `Process.WaitForExitAsync` → `IHostApplicationLifetime.StopApplication`).
   - Print `READY <endpoint>` after the listeners bind (N10).
   - Remove `WithRemoting`. Keep the `Akka.Remote` package only if clustering still needs it.
3. `Game/ServerProcess.cs` (console): spawn `Environment.ProcessPath --server --listen tcp://0.0.0.0:<port> --parent-pid <self>`, wait for `READY` (timeout and surface stderr on failure), then redirect the rest to the log. Kill on dispose/`ProcessExit` (best effort; the watchdog covers crashes).
4. Update `tools/SimEngine.MpSpike` to the TCP transport (or retire the remoting half).
5. **Tests:** TCP loopback reuses the 4a gateway scenarios over `tcp://127.0.0.1:0`; framing with large snapshots and split reads; ping timeout closes the connection; `--parent-pid` watchdog (spawn the server with a short-lived parent, assert the server exits).

#### 4c — Console host/join flows — DONE

**As built (differences from the plan below):**

- **Lobby state is pushed:** `SessionStreamUpdate` now carries `HostPlayerId`, `Settings`, and `Players`, and the session publishes a status update on join, leave, disconnect, and settings change. `LobbyGame` subscribes at join; `LobbyScreen` is a live, hotkey-driven screen (no `GetStatus` polling).
- **N9 completed:** a dropped connection sends `SessionProtocol.Disconnect`. In the lobby it is a leave; after the start the player stays a member with `Connected = false`; the host disconnecting ends the session. An explicit `LeaveGame` still removes the player. Wire protocol version is now 2.
- **Disconnect handling:** `GameClient` already marks subscriptions ended when the connection closes, so `LobbyScreen`/`GameLoop` watch `SessionStateCache.EndReason` (which covers "host left" and "connection lost") instead of `GameClient.Closed` directly.
- **Ownership:** `LobbyGame`/`GameSession` own a `MultiplayerConnection` (client + hosted `ServerProcess`), disposed after leaving. Only the host removes the game.
- **Save:** the console sends the typed name as-is; the server resolves it. Full paths still work against the local SP server and are rejected by an MP server.
- **Known gap:** in-game `load` always loads into the local SP server (leaving the MP game). Load-into-lobby for MP is Phase 5.
- Player name: `%AppData%/gsg/settings.json` (`PlayerProfile`); SP uses the saved name or `Environment.UserName` without prompting.

**Original plan:**

1. `Ui/HostGameFlow.cs`: port prompt (default 8110) → `ServerProcess.StartAsync` → connect to `tcp://127.0.0.1:<port>` → world/date/seed (shared with `SinglePlayerFlow`) → `CreateGame` → join → `LobbyScreen` in MP mode. The screen shows shareable addresses (local IPv4s and the port; note about port forwarding) and live players.
2. `Ui/JoinGameFlow.cs`: address prompt (`host[:port]`) → connect → list games with `Stage == Lobby` → pick → load the world's `StaticWorld` locally → join with content hash → `LobbyScreen` in waiting mode (non-host: settings read-only, "waiting for host") → auto-enter the game screen when the stage leaves `Lobby`.
3. `LobbyScreen` reads players/stage/settings from the subscription cache, not by polling `GetStatus`. Player list shows names (`SessionStatus.Players` becomes `PlayerInfo(Id, Name, Connected)`).
4. Disconnect handling: `GameLoop`/`LobbyScreen` watch `GameClient.Closed`. On close they return to the main menu with the reason ("Host closed the game", "Connection lost", "Kicked: …").
5. `App.cs`: wire Host/Join menu items; the MP host's `ServerProcess` is disposed when the host leaves the game.
6. Player name: prompt once and remember it in the user settings file (or default to `Environment.UserName`).

#### 4d — Steam transport (later, separate phase)

`SteamTransport` (`steam://`) implementing the same interfaces, plus a hosting choice from the Steam section and Steam-lobby discovery. No changes to gateway, client API, or UI flows other than an alternate join source.

### Files (expected)

- New `src/SimEngine.Networking/` (`ITransportConnection.cs`, `ITransportListener.cs`, `ITransportConnector.cs`, `TransportEndpoint.cs`, `TransportRegistry.cs`, `InProc/*`, `Tcp/*`)
- `src/SimEngine.Contracts/Wire/*` (`ClientMessage.cs`, `ServerMessage.cs`, `ErrorCode.cs`, `WireCodec.cs`, `WireJsonContext.cs`), `Commands/PlayerCommand.cs` (polymorphism attributes), `SessionStatus.cs` (`PlayerInfo`)
- `src/SimEngine.Server/Gateway/ConnectionActor.cs`, `GatewayHostedService.cs`, `SimEngineServerExtensions.cs`, `Actors/GameSessionActor.cs` (player names, disconnected flag, host handover)
- `src/SimEngine.Client/GameClient.cs`, `ClientConnection.cs`, `SessionClient.cs`, `SessionSubscription.cs`, `SimEngine.Client.csproj`
- `src/SimEngine.Game.Ui.Console/Program.cs`, `ServerMode.cs`, `App.cs`, `Game/ServerProcess.cs`, `Game/GameSessionFactory.cs`, `Game/GameSession.cs`, `Ui/HostGameFlow.cs`, `Ui/JoinGameFlow.cs`, `Ui/LobbyScreen.cs`
- `tests/SimEngine.Server.Tests/*` (actor helpers, gateway, codec, TCP, watchdog), `tools/SimEngine.MpSpike`
- Docs: `adr-001-mp-runtime-akka.md` (addendum: client boundary = wire protocol), `.github/copilot-instructions.md` (transport wording)

### Done when

- SP runs through the in-proc gateway with identical behavior to Phase 3.
- Two console instances on one machine (and across machines over TCP) can host/join, see each other's names in the lobby, start, and play in sync.
- Non-host permissions are enforced by the server from the connection's identity.
- Killing the host console ends the child server, and joined clients return to the main menu with a message.
- No client code references Akka, and adding `steam://` needs only a new transport plus a join source.

---

## Phase 5 — Load game into the lobby — DONE

**As built:**

- **Server:** `LobbyProtocol.CreateGameFromSave` (from 4a) creates the lobby game. The session stays in `Lobby` at the saved tick until the host starts it.
- **Settings in saves:** `SessionSaveSettings` writes the current `GameSettings` into the game state metadata (`session.*` keys) on every save. `InitializeFromSave` restores them, and the speed starts at the saved `InitialSpeed`. Saves without these keys get the defaults. The parity tests write the same keys into the directly built engine.
- **Legacy saves:** a save whose content hash is the legacy `"dev"` value (`GameSaves.LegacyContentHash`) is rejected with an `InvalidDataException` that says it is from an older, incompatible version. The gateway forwards the message.
- **Main menu load:** pick a save, then **Single player** (local server, full paths allowed, debug commands enabled) or **Host multiplayer**. The multiplayer host reuses `HostGameFlow.Host`: it starts the child server, connects, and loads by plain file name. The child shares the console's saves directory, so only saves in that folder can be hosted. Both paths open the `LobbyScreen`.
- **In-game `load`:** host only. The save is loaded on the *current* server first, so a rejected save leaves the game running. Then the loop leaves the current game (ending it for other players) and keeps the connection and hosted server. It joins the loaded game and opens its lobby, where the other players can rejoin through Join multiplayer. This closes the 4c gap where MP `load` always used the local SP server.
- `save` works in every stage except `Ended` (host only on remote servers, as in 4a).

**Original plan:**

**Goal:** loading a save creates a lobby so MP players can rejoin before resuming.

### Work

1. Load flow creates the game from a save via the lobby (`CreateGame` variant with save path, or `InitializeFromSave` + lobby registration).
2. Session starts in `Lobby` (paused at the saved tick); host starts/resumes.
3. Saves store game settings so they are restored on load.
4. In-game `save` works in any stage; `load` from the in-game menu ends the current session and goes through the lobby.
5. Saves without a real content hash (legacy `"dev"`) show a clear incompatibility message.

### Files (expected)

- `src/SimEngine.Game.Ui.Console/Ui/LoadGameFlow.cs`, `Game/Commands/NavigationCommands.cs`
- `src/SimEngine.Server/Actors/LobbyActor.cs`, `GameSessionActor.cs`
- `src/SimEngine.Game/GameDefinition.cs` / save metadata for settings

### Done when

Save during play → quit → load → lobby → start → continues from the saved tick with the same settings; MP players can rejoin a loaded game before the host starts.

---

## Out of scope (later)

- Army movement (`MoveArmy` intent command; engine computes the authoritative path; client path is preview only).
- Reconnect into a running game, player ↔ AI handoff, country actors (Phase 5 of `multiplayer-implementation-plan.md`).
- Clustered hosting / sharding.
