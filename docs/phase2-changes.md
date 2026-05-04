# Multiplayer Architecture — Phase 2 Overview

## Summary

Phase 2 migrates ConsoleHost to run on top of an **in-process Orleans silo**, routing tick advancement through a `GameSessionGrain` while preserving direct engine access for local queries. This lays the groundwork for Phase 3 (true multiplayer with remote clients).

## New Projects

| Project | Purpose |
|---------|---------|
| `SimEngine.Client` | Helper library for connecting to a local or remote Orleans silo and obtaining grain references. |

## Key Changes

### `SimEngine.Server`

- **`ILocalEngineProvider`** — singleton service that maps session IDs to in-process `SimulationEngine` instances. Allows ConsoleHost (and future local tools) to query engine state without serializing through grain methods.
- **`SimEngineServerExtensions.UseSimEngineSilo()`** — `IHostBuilder` extension that configures localhost clustering and registers the local engine provider.
- **`GameSessionGrain`** — now accepts an optional `ILocalEngineProvider` via constructor injection and registers its engine on initialization.

### `SimEngine.ConsoleHost`

| File | Change |
|------|--------|
| `Program.cs` | Boots an `IHost` with `UseSimEngineSilo()`, passes `IServiceProvider` into the app. |
| `App.cs` | `Run(IServiceProvider)` — threads services through to game flows. |
| `GameSessionFactory.cs` | `CreateNew` and `Load` accept an optional `IServiceProvider`, register the engine with `ILocalEngineProvider`, and resolve an `IGameSessionGrain` reference. |
| `GameSession.cs` | Gains `Grain` (nullable) and `SessionId` properties alongside the existing `Engine`. |
| `StepCommand.cs` | Routes ticks through `grain.StepAsync()` when a grain is available; falls back to direct `engine.Step()` otherwise. |
| `NewGameFlow.cs` / `LoadGameFlow.cs` | Accept and forward `IServiceProvider`. |

### `SimEngine.Client`

- **`GameClient`** — static helper with `ConnectLocalAsync()` (creates a client to a localhost silo) and `GetSession()` (returns a grain reference by session ID). Intended for Phase 3 remote-client scenarios.

## Architecture Diagram

```
┌─────────────────────────────────────────────────┐
│  ConsoleHost  (in-process silo)                 │
│                                                 │
│  Program.cs ──► IHost (Orleans Silo)            │
│       │                                         │
│       ▼                                         │
│  App.Run(services)                              │
│       │                                         │
│       ├─► NewGameFlow / LoadGameFlow            │
│       │       │                                 │
│       │       ▼                                 │
│       │   GameSessionFactory                    │
│       │       ├─ creates SimulationEngine       │
│       │       ├─ registers in ILocalEngineProvider│
│       │       └─ resolves IGameSessionGrain     │
│       │                                         │
│       ▼                                         │
│   GameLoop                                      │
│       │                                         │
│       ├─ StepCommand ──► grain.StepAsync()      │
│       │                   (routes through grain)│
│       │                                         │
│       └─ Query commands ──► session.Engine.*    │
│                             (direct local access)│
└─────────────────────────────────────────────────┘
```

## What Stays the Same

- **`SimEngine`** core library — no Orleans dependency.
- **All query commands** (provinces, countries, path, adjacency, events) — still use direct engine access. These will migrate to grain query methods in Phase 3.
- **Save/Load format** — unchanged.
- **Determinism guarantees** — preserved; the grain simply calls `engine.Step()`.

## Next: Phase 3

- `PlayerGrain` with command buffering and session join/leave.
- `ILobbyGrain` for game listing.
- Orleans Streams for state delta push.
- Network silo hosting (listen on port) alongside in-process mode.
- Multiple ConsoleHost instances connecting to the same session.
