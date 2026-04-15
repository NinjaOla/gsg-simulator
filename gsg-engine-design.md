# GSG Simulation Engine — Design Document

## Overview

A grand strategy game simulation engine in C#. The engine is a class library — no rendering, no UI. Consumers (a game, test harness, or headless benchmark) call `Step()`, the engine advances one tick, and state is consistent when it returns. Rendering is a third-party concern that reads engine state.

The engine knows it's a GSG — provinces, countries, armies, wars are first-class concepts — but the initial implementation of each is minimal and grows incrementally.

---

## Architectural Decisions

### Simulation as a Class Library

The engine exposes state and an event bus. A renderer reads state directly or subscribes to events. The engine does not define a renderer contract — that's the consumer's problem. Whether the game snapshots, locks, or double-buffers for its render thread is outside engine scope.

### Time Model: `TimeProvider` + `DateTimeOffset`

The engine uses `System.TimeProvider` (.NET 8+) as its time abstraction. A custom `SimulationTimeProvider` subclass controls simulation time. All dates are `DateTimeOffset` — no custom calendar. The framework already handles leap years, variable month lengths, and all calendar math.

Tick resolution is a configuration choice. An hourly game advances by `TimeSpan.FromHours(1)`, a daily game by `TimeSpan.FromDays(1)`. Monthly/yearly advances use `AddMonths()`/`AddYears()` which respect real calendar boundaries.

```csharp
public class SimulationTimeProvider : TimeProvider
{
    private DateTimeOffset _currentTime;

    public SimulationTimeProvider(DateTimeOffset startDate)
        => _currentTime = startDate;

    public override DateTimeOffset GetUtcNow() => _currentTime;

    public void Advance(TimeSpan delta) => _currentTime += delta;
    public void AdvanceMonths(int months) => _currentTime = _currentTime.AddMonths(months);
    public void AdvanceYears(int years) => _currentTime = _currentTime.AddYears(years);
}
```

`FakeTimeProvider` from `Microsoft.Extensions.TimeProvider.Testing` is available for unit tests out of the box.

### Tick Scheduling on Real Calendar

Systems declare a cadence (every tick, daily, weekly, monthly, quarterly, yearly). The engine compares the previous and current `DateTimeOffset` to decide whether a system runs — checking actual calendar boundaries, not modulo arithmetic.

```csharp
TickCadence.Daily     => curr.Day != prev.Day || curr.DayOfYear != prev.DayOfYear
TickCadence.Weekly    => ISOWeek changes
TickCadence.Monthly   => curr.Month != prev.Month || curr.Year != prev.Year
TickCadence.Quarterly => quarter changes
TickCadence.Yearly    => curr.Year != prev.Year
```

**Cross-rate dependencies:** When a daily system feeds into a monthly system, the state design must include explicit accumulator fields — the daily system adds to an accumulated value, the monthly system consumes and resets it. This is a data design concern, not hidden engine behavior.

### Determinism

Required for replay, multiplayer lockstep, and save/load verification.

- **No `float`/`double` in game state.** IEEE 754 results vary across CPUs, JIT versions, and build configurations. Use `decimal` for economy, `int`/`long` with fixed-point scaling for performance-critical paths.
- **No unordered iteration.** `Dictionary` does not guarantee enumeration order. If processing order has side effects, results become non-deterministic. Use ordered collections or iterate by sorted ID.
- **Seeded PRNG.** A deterministic random source passed through the simulation context. Same seed + same inputs = same outputs.

### Threading

The tick boundary is a hard synchronization barrier — all systems must complete before the clock advances. Parallelism is *within* a tick, not across ticks.

**System-level parallelism:** Systems declare what state they read and write. The engine builds a dependency graph at startup. Systems with no overlapping read/write sets run in parallel batches.

**Data-level parallelism:** Within a system, per-province calculations that only touch local state can be parallelized. The system author opts into this; the engine doesn't force it.

### Entity Model

The engine provides a unified component-based entity store. **Everything is an entity** — provinces, countries, armies, wars, trade routes. A province is just an entity with a `ProvinceComponent` attached; there is no separate "province collection." Entity relationships (e.g., `province --Owns--> country`) live in a labeled directed graph keyed by `EntityId`.

Component storage is typed-per-component: one `Dictionary<EntityId, T>` per component type, paired with a `SortedSet<EntityId>` mirror for deterministic ascending iteration. Components are value types (`struct`) so aliasing is impossible and snapshotting is trivial. No ECS framework — plain C#.

`ProvinceId` remains a strongly-typed marker, but by convention its `.Value` matches the underlying `EntityId.Value` of the province entity it refers to. This keeps pathfinding and adjacency APIs type-clear while letting them share a single id space.

### World Loading

World data (provinces, adjacency) is loaded at runtime from GeoJSON to support modded worlds. The loader is abstracted behind an interface — the specific geometry library is not yet decided. Adjacency is derived from shared polygon edges. Coordinates are lat/lon projected to 3D sphere for renderer consumption.

Mods provide their own GeoJSON files — the engine doesn't care whether the world is Earth or fantasy.

### Pathfinding

The engine provides A* on the province adjacency graph. The cost function is pluggable — the game defines movement costs. Costs are integer (no floats in search state), and the open set is keyed by `(F, G, ProvinceId.Value)` so equal-cost ties resolve deterministically. The heuristic is optional; passing `null` degrades A* to Dijkstra, which Phase 1a ships with. A fixed-point great-circle heuristic is a Phase 1b option if profiling shows it's worth the arithmetic.

### Save/Load

Full simulation state serialized to JSON. All entities, components, relationships, and current date. JSON chosen for human readability, moddability, and debuggability.

### Modding

Data mods via JSON files (world overrides, starting state, events). Code mods via C# inheritance — game systems are non-sealed, mods can subclass and register replacements. The formal mod loading/discovery system comes later, but the architecture supports it from the start.

### What's Engine vs. Game Scope

| Engine scope | Game / mod scope |
|---|---|
| World loading (GeoJSON via interface) | Specific map files |
| Province graph + adjacency | What provinces "mean" in gameplay |
| Entity/component store | Component type definitions and logic |
| Entity relationships (labeled graph) | Which relationships exist |
| Tick scheduling on real calendar | System implementations |
| SimulationTimeProvider | Start date, tick resolution |
| Event bus (pub/sub) | Event types and game reactions |
| A* pathfinding on graph | Cost functions |
| Deterministic PRNG | What randomness is used for |
| Save/load (full state JSON) | Additional save metadata |
| System dependency resolution + parallel batching | Which systems exist |

| Outside engine scope | Why |
|---|---|
| Renderer contract | Consumer's problem — read state or subscribe to events |
| Modifier stacking | Game design concern — lives in game systems |
| UI / input | Third-party renderer handles this |

---

## Phase Plan

### Phase 0 — Foundation

The skeleton everything else builds on.

- Solution structure: `SimEngine.slnx` containing the `SimEngine` class library + `SimEngine.Tests`
- `SimulationTimeProvider` wrapping `TimeProvider` with advance methods
- `SimulationEngine` — the tick loop, system registration, cadence scheduling
- `ISimulationSystem` interface — cadence, execution order, read/write declarations
- System dependency graph — topological sort at startup, parallel batch execution
- `IEventBus` — publish/subscribe for cross-system communication
- Deterministic seeded PRNG
- Strongly-typed IDs (`ProvinceId`, `CountryId`, `EntityId` as `readonly record struct`)

### Phase 1 — World & State

The map and things on it. Split into two ships to keep the state model decoupled from the geometry library decision.

#### Phase 1a — State layer (done)

The in-memory model, with zero new dependencies. Tests use a programmatic `WorldBuilder` to construct synthetic worlds, so none of 1a depends on a real GeoJSON loader.

- **Unified entity store.** Provinces are entities — not a parallel collection. `EntityStore` holds typed per-component stores (`ComponentStore<T>` backed by `Dictionary<EntityId, T>` for ref access via `CollectionsMarshal`, plus a mirrored `SortedSet<EntityId>` for deterministic ascending iteration). Components are structs. `ProvinceId` is a strongly-typed marker whose `.Value` matches the underlying `EntityId.Value` for any province entity.
- **`ProvinceComponent`** — name, `Terrain` enum (Land/Sea modeled from the start), centroid as signed int microdegrees. No floats in game state. Neighbors live in `AdjacencyGraph`, not on the component — single source of truth.
- **`AdjacencyGraph`** — `FrozenDictionary<ProvinceId, ImmutableArray<ProvinceId>>`, immutable after build. Handles sparse `ProvinceId` values; neighbors within each entry are sorted ascending so iteration is deterministic.
- **`AStarPathfinder`** — integer-only Dijkstra with optional admissible heuristic. `SortedSet` open set keyed by `(F, G, ProvinceId.Value)` so equal-cost paths resolve deterministically across 100+ repeated runs.
- **`RelationshipGraph`** — labeled directed multigraph of entity relationships. `SortedDictionary<(EntityId, RelationshipLabel), SortedSet<EntityId>>` outbound + mirrored inbound index for O(log n) reverse lookups. `RelationshipLabel` is an enum (engine reserves 0–999; game code starts at 1000).
- **`SimulationState`** — root container holding `EntityStore`, `RelationshipGraph`, and `AdjacencyGraph`. Threaded into `SimulationContext` as an `init` property so systems access it via `ctx.State`. `SimulationEngineOptions.InitialState` lets callers hand a pre-built state to the engine.
- **`WorldBuilder`** — programmatic world construction used by tests, procedural games, and (in 1b) as the canonical "seeds → state" routine invoked by loaders.
- **`SphereProjection`** — lat/lon to unit-sphere helper. Uses `double` intentionally: renderer-only, never fed back into game state, never participates in determinism-sensitive math.
- **`IWorldLoader`** — interface + `ProvinceSeed` + `WorldLoadResult` shipped as a stub. Concrete implementation deferred to 1b.
- **State keys** — `CoreStateKeys` (`state/entities`, `state/relationships`, `state/adjacency`) and `ComponentStateKeys.Of<T>()` generic helper that caches per closed generic. Systems declare reads/writes via these so the Phase 0 dependency scheduler stays unchanged.

#### Phase 1b — Real world loader

The geometry library decision and the GeoJSON pipeline. Gated on the state layer so we can commit to a library without churning the state model.

- **Geometry library decision** — likely NetTopologySuite (BSD-3, STRtree for fast adjacency, `NetTopologySuite.IO.GeoJSON` for loading). Alternative: lightweight hand-rolled `System.Text.Json` parser + segment-hash adjacency. Decision gated on dependency-surface tolerance.
- **`GeoJsonWorldLoader`** — `IWorldLoader` implementation. Parses a `FeatureCollection`, extracts polygon geometries, emits `ProvinceSeed` records with centroids computed in microdegrees.
- **Adjacency-from-shared-edges** — spatial index (STRtree) candidate pairs + boundary intersection test, or normalized edge hashmap. Natural Earth topology quirks (non-watertight polygons, sliver triangles) handled with tolerance + validation pass.
- **Natural Earth integration test** — load `ne_10m_admin_1_states_provinces` end-to-end, assert reasonable province count and adjacency degree distribution. Regression gate on the loader.
- **Determinism boundary** — NTS uses `double` internally. Derived values (adjacency lists, integer-scaled centroids) baked into game state at load time; NTS types never cross the state boundary.
- **(Optional)** Haversine / fixed-point great-circle heuristic for `AStarPathfinder`, enabling real A* over continent-scale maps. Deferred if Dijkstra remains fast enough in practice.

### Phase 2 — Save/Load

Moved early because every subsequent phase needs it for testing and iteration.

- Full state to JSON — entities, components, relationships, current simulation date
- JSON to full state restore
- Round-trip tests: save → load → verify identical state
- Determinism tests: run N ticks → save → reload from initial → run N ticks → compare results

### Phase 3 — Core GSG Domain (minimal)

First game-specific layer. Deliberately small — just enough for a vertical slice.

- Country entity — tag, name, owns provinces via relationships
- Province economy — single "production" value, one number
- Population — one number per province, flat growth rate
- `EconomySystem` (monthly) — provinces produce based on population, countries collect
- `PopulationSystem` (monthly) — growth tick

This is enough to load a real-world map, assign countries, and watch economies tick forward.

### Phase 4 — Military (minimal)

- Army entity — owner, province location, unit count (one unit type)
- Movement — A* on adjacency graph, daily tick to advance along path
- Combat — two armies in same province, strength comparison with PRNG
- `MilitarySystem` (daily)

### Phase 5 — Diplomacy & Wars (minimal)

- War entity — attacker, defender, participants
- War declaration / peace — event-driven state changes
- Province occupation as separate from ownership
- `DiplomacySystem` (daily)

### Phase 6 — Modding & Scripting

- Mod manifest (`mod.json`) + load order + dependency resolution
- Data overrides via JSON (world, starting state, events)
- Code mods via inheritance — subclass systems, register replacements
- Scripted events — JSON-defined conditions and effects
- Relationship traversal for scripted conditions (scope navigation)

---

## Geodata Sources

For real-world maps, the primary dataset is **Natural Earth** (public domain):

- `ne_10m_admin_1_states_provinces` — ~4,500 first-order administrative subdivisions worldwide. Includes ISO codes, country references, names. Lands in the target range for GSG provinces.
- `ne_10m_admin_0_countries` — 258 country polygons with attributes (ISO/FIPS codes, population estimates, region classifications)
- `ne_10m_land` / `ne_10m_coastline` — landmass and coastlines for globe rendering
- `ne_10m_rivers_lake_centerlines` — rivers for terrain/supply considerations
- `ne_10m_populated_places` — ~7,000+ cities with population data

All Natural Earth data is public domain — no licensing concerns for distribution.

Pre-converted GeoJSON is available at `github.com/nvkelso/natural-earth-vector` in the `/geojson/` folder.

**GADM** (Global Administrative Areas) offers higher resolution with 400,000+ administrative areas across multiple subdivision levels, but its license restricts commercial use.
