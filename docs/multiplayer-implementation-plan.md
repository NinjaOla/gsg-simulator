# Multiplayer Implementation Plan

This document tracks the phased rollout for an Akka.NET-based multiplayer runtime.

## Goals

- Use **Akka.NET end-to-end** for lobby, sessions, country actors, and client/server communication.
- Keep **single-player on the same client/server architecture** as multiplayer.
- Keep `SimEngine` and `SimEngine.Game` **framework-free** and authoritative for simulation behavior.
- Let actors be the running parts of the app while the engine remains the only simulation authority.

---

## Architectural Constraints

### What does change

- Runtime hosting moves to server/client actor systems.
- Lobby, session lifecycle, player connection flow, and AI/player country behavior move into Akka actors.
- `ConsoleHost` stops creating `SimulationEngine` directly and instead becomes a client of the server runtime.

### What does not change

- **`SimEngine`** stays free of Akka dependencies.
- **`SimEngine.Game`** stays free of Akka dependencies.
- **`SimulationEngine`** remains authoritative for state mutation and tick advancement.
- **`ISimulationSystem` / `SystemDependencyGraph`** remain the simulation scheduling model.
- **`DeferredEventBus`** remains the way commands enter the engine on tick boundaries.
- **Save/Load** continues to use `SimulationEngine.Save()` / `Load()`.

---

## Runtime Model

### Single-player

Single-player still runs as a **server + client split**:

- a local server actor system hosts the authoritative simulation
- a separate client actor system connects to it through the same contracts used in multiplayer
- both may be launched by the same executable, but they remain separate runtime roles

### Multiplayer

- one shared server process hosts sessions
- multiple remote clients connect through Akka remoting
- the architecture is the same as single-player, only the hosting topology changes

---

## Actor Roles

### `LobbyActor`

Responsibilities:
- create/list sessions
- route players to sessions
- expose joinable game metadata

### `GameSessionActor`

Responsibilities:
- own one authoritative `SimulationEngine`
- own the command queue
- own the tick timer / lockstep gate
- publish queued commands to the engine event bus before stepping
- broadcast snapshots/deltas after each tick
- supervise country and observer child actors

### `CountryActor`

Responsibilities:
- represent one country/nation at the app layer
- observe current synchronized state
- decide and issue commands
- never mutate authoritative simulation state directly

Variants:
- `AICountryActor`
- `PlayerCountryActor`

### `ObserverActor` / client bridge actor

Responsibilities:
- forward authoritative state updates to a connected client
- decouple UI code from direct engine access

### Client-side bridge actor

Responsibilities:
- connect to lobby/session endpoints
- send UI/player commands to the server
- receive snapshots/deltas and surface them to UI code

---

## Command and Tick Flow

1. Client UI submits a player command.
2. Client bridge actor sends that command to the target server session.
3. AI or player country actors may also issue commands for the same tick.
4. `GameSessionActor` queues all commands for tick `N`.
5. On the tick boundary, `GameSessionActor` publishes queued commands into `DeferredEventBus`.
6. `SimulationEngine.Step()` runs once and resolves the tick deterministically.
7. `GameSessionActor` collects the resulting state snapshot/delta.
8. Observer/client actors receive updates and feed them to the frontend.

This keeps actors at the orchestration layer and the simulation engine at the authority layer.

---

## Execution Modes

Use the same actor/message design in two modes:

### Mode 1: Local session hosting

- no cluster requirement initially
- one server actor system listening locally
- one client actor system connecting to localhost
- useful for single-player and early integration testing

### Mode 2: Clustered session hosting

- Akka.Cluster / Akka.Cluster.Sharding enabled
- sessions distributed across nodes
- lobby available across the cluster
- same session/country actor logic reused under clustered hosting

This should follow an **execution mode abstraction** so local hosting and clustered hosting share the same actor protocols and most of the same actor code.

---

## Implementation Phases

### Phase 1 — Shared Contracts

1. Create `SimEngine.Contracts`.
2. Add message contracts for:
   - lobby requests/responses
   - session join/leave
   - player command submission
   - state snapshot/delta delivery
3. Keep contracts serialization-friendly and independent of Akka hosting details.
4. Keep IDs and state DTOs explicit across the boundary.

Deliverable:
- shared contracts usable by both server and client projects

### Phase 2 — Local Server Runtime

1. Create `SimEngine.Server`.
2. Add a server actor system host.
3. Implement `LobbyActor`.
4. Implement `GameSessionActor` with:
   - `SimulationEngine`
   - command queue
   - tick scheduling
   - snapshot publication
5. Start with **local server hosting** before clustering.
6. Add tests for session creation, join flow, and stepping.

Deliverable:
- a standalone local server runtime with authoritative sessions

### Phase 3 — Client Runtime

1. Create `SimEngine.Client`.
2. Add a client actor system host/helper.
3. Implement a client bridge actor for session/lobby communication.
4. Expose a typed API for frontends:
   - list sessions
   - create/join session
   - submit command
   - subscribe to state updates
5. Add integration tests for client-to-server round trips.

Deliverable:
- a client library that can connect to the local server runtime

### Phase 4 — ConsoleHost Migration

1. Refactor `SimEngine.ConsoleHost` to use `SimEngine.Client`.
2. Remove direct `SimulationEngine` construction from the interactive host path.
3. Add local single-player startup that launches:
   - local server runtime
   - client runtime connected to localhost
4. Add remote connect mode for multiplayer.
5. Keep save/load and command UX consistent from the user's perspective.

Deliverable:
- `ConsoleHost` runs through the client/server boundary in both SP and MP modes

### Phase 5 — Country Actors

1. Add `CountryActor` base behavior on the server side.
2. Add `AICountryActor`.
3. Add `PlayerCountryActor`.
4. Have `GameSessionActor` spawn country actors for session countries.
5. Feed synchronized state into country actors.
6. Have country actors emit commands back to the session actor.
7. Ensure the session actor remains the only writer to the engine.

Deliverable:
- countries run as actors while still issuing commands into the authoritative engine

### Phase 6 — Multiplayer Hosting

1. Add Akka remoting/network configuration for remote clients.
2. Add clustered hosting for sessions and lobby if needed.
3. Introduce cluster sharding for sessions when single-node hosting becomes a limitation.
4. Keep local mode available for tests and single-player.
5. Verify multiple clients can attach to the same session cleanly.

Deliverable:
- true multiplayer on the same protocols as single-player

### Phase 7 — Persistence and Hardening

1. Add session persistence around `SimulationEngine.Save()` / `Load()`.
2. Add autosave policy.
3. Add reconnect handling.
4. Add player-to-AI takeover / AI-to-player handoff.
5. Add content compatibility checks using content hash/version/features.
6. Add observability/logging around actor and session lifecycle.

Deliverable:
- production-oriented session hosting with persistence and reconnection behavior

---

## Suggested Package Areas

Add package versions when implementation starts; pin exact versions then.

```xml
<!-- Akka.NET -->
<PackageVersion Include="Akka" Version="..." />
<PackageVersion Include="Akka.Hosting" Version="..." />
<PackageVersion Include="Akka.Remote" Version="..." />
<PackageVersion Include="Akka.Cluster" Version="..." />
<PackageVersion Include="Akka.Cluster.Sharding" Version="..." />
<PackageVersion Include="Akka.Cluster.Tools" Version="..." />
<PackageVersion Include="Akka.Persistence" Version="..." />
<PackageVersion Include="Akka.Streams" Version="..." />
<PackageVersion Include="Akka.TestKit.Xunit2" Version="..." />
```

Optional, depending on the exact hosting/test strategy:

```xml
<PackageVersion Include="Akka.Persistence.Sqlite" Version="..." />
<PackageVersion Include="Akka.Persistence.Query" Version="..." />
```

---

## Notes

- Start **without cluster sharding** unless distribution is immediately needed; keep the actor/message model stable first.
- Prefer a design where local hosting and clustered hosting differ mostly in configuration, not in actor behavior.
- If country actors are added early, keep their input/output protocol narrow so replay and determinism remain easy to reason about.
