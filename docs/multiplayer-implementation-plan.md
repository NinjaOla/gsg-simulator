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

9. ✅ Add `PlayerGrain` with command buffering and session join/leave.
   - `IPlayerGrain` (keyed by player id) buffers `PlayerCommand`s and joins/leaves a
     session; `FlushCommandsAsync` forwards the buffer to the session in one call via
     the new `IGameSessionGrain.EnqueueCommandsAsync` batch method.
   - `GameSessionGrain` tracks members in an ordinal `SortedSet<string>`
     (`JoinAsync`/`LeaveAsync`/`GetPlayersAsync`), cleared on shutdown. Membership is
     in-memory until step 14 (persistence) and independent of engine init.
10. ✅ Add `ILobbyGrain` for game listing and creation.
    - Singleton lobby (key `LobbyKeys.Default`): `CreateGameAsync` assigns a session id and
      initializes the session before registering the `GameListing`; `ListGamesAsync`/`GetGameAsync`
      query games; `RemoveGameAsync` unregisters and shuts the session down; `ListWorldsAsync`
      surfaces `WorldCatalog` so contracts-only clients never handle file paths.
    - `GameClient.GetLobby` mirrors `GetSession`. Listings are in-memory until step 14 (persistence).
11. ✅ Replace per-tick full-state push with **delta** sync over the existing
    session stream (the stream itself is wired; `SessionStreamUpdate` currently
    carries tick result + event text).
    - A client fetches a one-time `SessionSnapshot` via `IGameSessionGrain.GetSnapshotAsync`
      (world summary + per-country identity/treasury), then folds in per-tick
      `SessionStreamUpdate.CountryDeltas` — only countries whose `TreasuryComponent`
      changed, carrying **absolute** `FundsE2` so applying deltas is order-tolerant.
    - `GameSessionGrain` diffs against a `_lastPublishedFunds` baseline; `SimEngine.Client.SessionStateCache`
      is the client read model (snapshot + deltas, thread-safe). Provinces/adjacency/country identity
      are static and live only in the snapshot, keeping per-tick messages tiny at continent scale.
    - ConsoleHost still reads the in-process engine view; the backdoor is removed when the
      console cuts over to the cache in step 13.
12. ✅ Add network silo hosting mode (listen on a port) alongside in-process mode.
    - `SimEngineSiloOptions` (silo/gateway ports + `EnableStreams`) drives a new
      `UseSimEngineSilo(Action<SimEngineSiloOptions>)` overload: the parameterless call keeps
      in-process single-player defaults, while a configured call uses `UseLocalhostClustering(siloPort, gatewayPort)`.
      Streams are now enabled by default (`AddMemoryStreams` + `PubSubStore`), so step-11 deltas
      actually reach out-of-process clients.
    - ConsoleHost gains a `--server` mode (`ServerMode`, optional `--silo-port`/`--gateway-port`)
      that hosts the silo and waits for Ctrl+C; the default path is still in-process single-player.
    - `NetworkSiloHostingTests` proves a real gateway listens (a `TcpClient` connects) and a
      co-hosted session steps over the network silo. Loopback/same-machine scope; cross-machine
      membership is out of scope.
13. Support multiple `ConsoleHost` instances connecting to the same session for testing.
    - **Listen-server model (locked).** SP and MP are the *same* code path: a game always runs as
      a silo with one or more clients. SP = the console hosts a silo and is its only client; MP =
      additional clients connect to a console-owned "listen" server or a dedicated (`--server`) one.
      "SP vs MP" is purely wiring — who hosts and how many clients connect.
    - **Render cutover (the core work).** Remove the in-process `GameSession.Engine` backdoor (and the
      `GameSessionFactory.Create` throw) so the host's own client renders from the synced
      grain/stream/`SessionStateCache` path — identical to a remote client. After this there is no
      separate single-player rendering path.
    - **Static data via content hash (locked).** Static/content data (map/geography, mods) is **not**
      sent over the wire. Each client loads it locally and computes a content hash (building on the
      existing `GameManifest` `ContentHash`/`ContentVersion`/`EnabledFeatures`, today hardcoded `"dev"`).
      The client passes the hash on join; the server enforces it as a compatibility gate and rejects
      mismatches — protecting deterministic lockstep.
    - **Dynamic state read model.** The hot per-tick path stays on the small step-11 cache
      (tick/date + per-country treasury + events). Richer static-but-queryable detail
      (province detail, neighbors, A* paths) is fetched lazily via new on-demand grain queries
      (e.g. `GetProvinceDetailAsync`/`GetNeighborsAsync`/`GetPathAsync`) so snapshots stay tiny while
      console commands keep full SP/MP parity.
    - **Server lifecycle (locked).** SP uses a host-owned server (co-hosted silo that dies with the
      client). MP spawns/attaches a **detached/shared** `--server` process that survives independently
      so multiple clients can attach. `MainMenu` gains an SP/MP choice that selects the wiring.
    - **Suggested slicing** (separate change-sets, sequenced): (a) content-hash compatibility gate ✅ —
      foundational, low-risk, no UI; (b) render cutover + on-demand grain queries — the bulk;
      (c) process lifecycle (host-owned vs detached) + SP/MP menu.
    - **Slice (a) done.** `ContentHasher` (in `SimEngine.Game`) computes a deterministic SHA-256 over the
      raw world file bytes + content version + ordinal-sorted enabled features; `GameContentDefaults.ContentVersion`
      replaces the scattered hardcoded `"dev"`. `GameSessionGrain` computes the authoritative hash on
      `InitializeAsync` (and restores it from save metadata on `InitializeFromSaveAsync`), and `JoinAsync` now
      takes the client's hash and rejects mismatches with `ContentMismatchException` (gate is skipped only while
      the session is pre-init, honoring "membership independent of engine init"). `IPlayerGrain.JoinSessionAsync`
      forwards the hash. Covered by `ContentHasherTests`, `ContentHashGateTests`, and the updated `PlayerGrainTests`.
    - Scope stays loopback/same-machine (matches steps 11–12); cross-machine membership is out of scope.

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
