# Multiplayer Implementation Plan

## Implementation Phases

### Phase 1 — Contracts & Server Foundation ✅

1. ✅ Create `SimEngine.Contracts` project with grain interfaces and command records.
2. ✅ Create `SimEngine.Server` project with `GameSessionGrain` that **owns** the `SimulationEngine` — the grain creates the engine (world load + game seeding happen server-side via `WorldCatalog`), it is not handed one by the client.
3. ✅ Add Orleans package references to `Directory.Packages.props`.
4. ✅ Implement `GameSessionGrain.StepAsync(int ticks)` — applies queued commands, steps server-side (batched), returns tick result. Pause/resume state persists across steps. `SaveAsync` / `InitializeFromSaveAsync` round-trip saves through the grain.
5. ✅ Write grain integration tests using Orleans `TestCluster`, including a byte-for-byte determinism parity test (N ticks via grain == N ticks via direct engine).

### Phase 2 — Client Library & ConsoleHost Migration ✅ (in-process mode)

6. ✅ Create `SimEngine.Client` with a helper that connects to a local or remote silo. *(Note: in-process single-player uses the co-hosted `IClusterClient` from the silo host directly; `GameClient.ConnectLocalAsync` is for out-of-process clients and gets exercised by the Phase 3 two-client spike.)*
7. ✅ Refactor `ConsoleHost` to drive the grain instead of creating `SimulationEngine` directly.
   - `GameSessionFactory` → calls `grain.InitializeAsync`/`InitializeFromSaveAsync`, then fetches a **read-only** engine view from `ILocalEngineProvider` for rendering/queries.
   - `GameSession` → holds the grain (mutations) + read view (display). Disposal shuts the grain down and unregisters the engine.
   - `StepCommand`/`SaveCommand`/`LoadCommand` → grain methods.
   - The in-process read view is a documented backdoor; it disappears when state snapshots/streams land (step 11).
8. ✅ Verified single-player (in-process silo) via an end-to-end smoke test (`GameSessionFactorySmokeTests`): new game → step → save → load → step, all through the grain.

### Milestone 2 — Orleans go/no-go spike ✅

Validate the runtime choice before building lobby/player grains. See
[adr-001-mp-runtime-orleans.md](adr-001-mp-runtime-orleans.md) — **decision: go**.

- ✅ Events-out path prototyped: `GameSessionGrain` publishes `SessionStreamUpdate`
  (tick result + rendered events) to a per-session Orleans stream after each step;
  covered by an external-client streams integration test.
- ✅ `tools/SimEngine.MpSpike`: standalone silo + two external clients over the
  loopback gateway, measuring grain overhead, round-trip latency, stream delivery,
  and snapshot size against budgets. All budgets passed.
- Open follow-ups recorded in the ADR: tune stream poll period if real-time push
  latency matters (memory streams poll ~100 ms); switch to delta sync before
  continent-scale worlds (full snapshot ≈ 4 MB raw at ~4,500 provinces).

### Phase 3 — Multiplayer

9. Add `PlayerGrain` with command buffering and session join/leave.
10. Add `ILobbyGrain` for game listing and creation.
11. Replace per-tick full-state push with **delta** sync over the existing
    session stream (the stream itself is wired; `SessionStreamUpdate` currently
    carries tick result + event text).
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
