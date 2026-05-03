# Multiplayer Implementation Plan

## Implementation Phases

### Phase 1 — Contracts & Server Foundation

1. Create `SimEngine.Contracts` project with grain interfaces and command records.
2. Create `SimEngine.Server` project with `GameSessionGrain` that wraps `SimulationEngine`.
3. Add Orleans package references to `Directory.Packages.props`.
4. Implement `GameSessionGrain.StepAsync` — applies queued commands, calls `engine.Step()`, returns tick result.
5. Write grain integration tests using Orleans `TestCluster`.

### Phase 2 — Client Library & ConsoleHost Migration

6. Create `SimEngine.Client` with a helper that connects to a local or remote silo.
7. Refactor `ConsoleHost` to use `SimEngine.Client` instead of creating `SimulationEngine` directly.
   - `GameSessionFactory` → creates a grain via the client instead of instantiating the engine.
   - `GameSession` → wraps grain calls instead of direct engine access.
   - `GameLoop` commands → call grain methods (e.g., `StepCommand` calls `session.StepAsync()`).
8. Verify `ConsoleHost` works identically in single-player (in-process silo) mode.

### Phase 3 — Multiplayer

9. Add `PlayerGrain` with command buffering and session join/leave.
10. Add `ILobbyGrain` for game listing and creation.
11. Add Orleans Streams for pushing state deltas to connected clients after each tick.
12. Add network silo hosting mode (listen on a port) alongside in-process mode.
13. Support multiple `ConsoleHost` instances connecting to the same session for testing.

### Phase 4 — Persistence & Production Hardening

14. Wire Orleans grain persistence to call `SimulationEngine.Save()`/`Load()`.
15. Add auto-save on configurable tick interval.
16. Add graceful disconnect handling (player grain detects timeout, pauses or AI-takes-over).
17. Add game speed synchronization (host controls speed, grain timer adjusts).

---

## What Does NOT Change

- **`SimEngine`** — no Orleans dependency, no actor framework. Stays a pure library.
- **`SimEngine.Game`** — game systems, components, codecs. Untouched.
- **`ISimulationSystem` / `SystemDependencyGraph`** — the tick loop, batching, and dependency analysis stay as-is. They already provide actor-like system isolation.
- **`DeferredEventBus`** — player commands become `ISimulationEvent` records published to the existing bus. No new event infrastructure needed.
- **Determinism** — `Xoshiro256StarStar`, per-system PRNG forking, tick scheduling all preserved. The grain just calls `Step()`.
- **Save/Load format** — grain persistence delegates to existing `SimulationSaveSerializer`.

---

## Packages to Add (to `Directory.Packages.props`)

```xml
<!-- Orleans -->
<PackageVersion Include="Microsoft.Orleans.Server" Version="..." />
<PackageVersion Include="Microsoft.Orleans.Client" Version="..." />
<PackageVersion Include="Microsoft.Orleans.Sdk" Version="..." />
<PackageVersion Include="Microsoft.Orleans.Streaming" Version="..." />
<PackageVersion Include="Microsoft.Orleans.Testing" Version="..." />
```

> Pin to the latest stable Orleans 9.x release compatible with .NET 10 at implementation time.
