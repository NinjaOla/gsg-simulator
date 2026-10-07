# Copilot Instructions

## Project Guidelines
- The simulation engine owns core pathfinding behavior and APIs; pathfinding changes belong inside the engine rather than in external orchestration.

## Runtime Architecture (Akka.NET)
- The runtime is Akka.NET end-to-end (see `docs/adr-001-mp-runtime-akka.md` and `docs/multiplayer-implementation-plan.md`). Orleans/grains are superseded and must not be introduced; treat remaining Orleans/grain references in code comments as stale.
- `SimEngine` and `SimEngine.Game` must stay free of Akka dependencies. Akka lives only in `SimEngine.Contracts` (server actor protocols), `SimEngine.Server`, and hosts. `SimEngine.Client` and `SimEngine.Networking` have no Akka references.
- `GameSessionActor` (owning `SimulationEngine`) is the sole simulation authority. Commands enter the engine via `DeferredEventBus` on tick boundaries; persistence uses `SimulationEngine.Save()`/`Load()`.
- Actor roles:
  - `LobbyActor` — create/list sessions and route players to them.
  - `GameSessionActor` — owns the engine, command queue, tick timer, and snapshot broadcast; validates content hash on join.
  - `PlayerActor` — per-player entry point; buffers commands and forwards them to the session.
  - `CountryActor` / `AICountryActor` / `PlayerCountryActor` (planned, Phase 5) — observe state and issue commands; never mutate state directly.
  - Observer/client bridge actors — forward state updates to clients (`SimEngine.Client`, `SessionStateCache`).

## Listen-Server Model
- Single-player is a client that spawns/owns a local server actor system and talks to it through the same gateway and wire protocol as multiplayer (`SimEngine.Contracts.Wire` over the in-process transport).
- Multiplayer is multiple clients connecting to either a console-owned "listen" server or a dedicated (`--server`) server.
- SP vs MP is purely wiring (who hosts + how many clients connect), never a separate code path.
- Clients never hold server `IActorRef`s or use Akka remoting; they talk to the server's `GatewayActor`/`ConnectionActor` through `SimEngine.Networking` transports (in-proc, TCP, later Steam). The player id is assigned by the server per connection, never trusted from the client.
- There is no in-process engine access from clients (the former `GameSession.Engine` / `ILocalEngineProvider` backdoor is removed). Clients read dynamic state from `SessionStateCache` (via `GameClient.SubscribeAsync`) and static data from a locally loaded `StaticWorld`; never reintroduce direct `SimulationEngine` reads in client code.

## MP Design Decisions
- Static/content data is NOT sent over the wire. Each client loads static data (map/geography) and mod content locally; only a content hash (built on `GameManifest` `ContentHash`/`ContentVersion`/`EnabledFeatures` via `ContentHasher`) is exchanged. The server rejects join on mismatch.
- Server lifecycle: single-player uses a host-owned server that dies with the client; multiplayer uses a detached/shared server that survives independently so multiple clients can attach.
