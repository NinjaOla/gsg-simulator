# ADR-001 — Multiplayer runtime: Akka.NET

- **Status:** Accepted (go)
- **Date:** 2026-08-18
- **Deciders:** Ola
- **Supersedes:** `adr-001-mp-runtime-orleans.md`

## Context

`SimEngine` is a deterministic, server-authoritative simulation engine. The
runtime around it needs to support:

- single-player and multiplayer through the same client/server boundary
- lobby and session management
- player and AI orchestration through actors
- future clustered hosting without pushing actor concerns into `SimEngine`

The architecture direction is now:

- **Akka.NET end-to-end** for lobby, sessions, server/client messaging, and
  country actors
- **single-player still uses server + client roles**, even locally
- **`SimulationEngine` remains the only authoritative state mutator**
- actors observe synchronized state and **issue commands**; they do not mutate
  simulation state directly

This replaces the earlier Orleans-based runtime decision.

## Decision

**Use Akka.NET as the multiplayer/runtime architecture.**

## Why

### 1. One actor model across the whole app

Akka can cover:

- lobby runtime
- session runtime
- server-to-client messaging
- per-country AI/player actors
- future clustered hosting

That keeps the mental model consistent instead of mixing frameworks for session
hosting and gameplay orchestration.

### 2. Better fit for explicit game runtime structure

The intended runtime is not just "RPC to an authoritative server." It is an
explicit actor hierarchy:

- `LobbyActor`
- `GameSessionActor`
- `CountryActor` / `AICountryActor` / `PlayerCountryActor`
- observer/client bridge actors

Akka maps directly onto that structure with explicit lifecycles, supervision,
parent/child ownership, and message flow.

### 3. Local SP and MP can share the same boundary

Even single-player should behave like client + server. Akka remoting and
separate actor systems make that boundary concrete from the start, which helps:

- prevent in-process engine backdoors
- exercise serialization and contracts early
- reduce SP/MP drift
- keep host-and-play and dedicated server modes as hosting variations, not
  architecture changes

### 4. Engine purity is preserved

`SimEngine` and `SimEngine.Game` remain framework-free. Akka stays in the
runtime layer:

- `GameSessionActor` owns the engine
- commands are queued and published into `DeferredEventBus`
- `SimulationEngine.Step()` remains the deterministic authority point

This keeps simulation code testable and portable.

## Runtime Shape

### Server side

- `LobbyActor` lists and creates sessions
- `GameSessionActor` owns one `SimulationEngine`
- `GameSessionActor` owns the tick gate and command queue
- `CountryActor` variants observe state and submit commands
- observer actors push snapshots/deltas to clients

### Client side

- a separate client actor system connects to the server
- client bridge actors send player commands and receive synchronized state
- frontends (`ConsoleHost`, future Stride UI) talk to the client layer, not the
  engine directly

## Consequences

### Positive

- one framework for runtime, lobby, sessions, and country actors
- strong alignment with actor-oriented AI/player orchestration
- clean client/server split for both SP and MP
- future clustered hosting remains available without changing engine code

### Tradeoffs

- Akka remoting/cluster infrastructure must be hosted and configured explicitly
- serialization/contracts become first-class concerns earlier
- session persistence and cluster rollout are additional runtime work

## Non-Goals

- This is **not** a decision to move simulation logic into actors.
- This is **not** a decision to allow country actors to own authoritative game
  state.
- This is **not** a commitment to clustered deployment from day one; local
  server/client mode comes first.

## Follow-up

- Use `docs/multiplayer-implementation-plan.md` as the phased rollout plan.
- Keep README as the current architecture overview.
- Build local server + local client mode before cluster sharding.
