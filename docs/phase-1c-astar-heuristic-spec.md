# Phase 1c — A* heuristic spec

## Goal

Add an engine-owned great-circle heuristic for `AStarPathfinder` so the engine remains the authority for pathfinding behavior while preserving determinism and optimal-path guarantees.

The current pathfinder already supports this shape:

- `ProvinceEdgeCost` supplies the actual edge cost.
- `ProvinceHeuristic` is optional.
- Passing `null` keeps the current Dijkstra behavior.

Phase 1c should build on that API instead of redesigning pathfinding, but the implementation and policy for when the heuristic is valid should live inside `SimEngine`.

## Current state

The repo already has the prerequisites for this phase:

- `AStarPathfinder.FindPath(...)` accepts an optional `ProvinceHeuristic`.
- `ProvinceComponent` stores centroid latitude and longitude as signed microdegrees.
- `WorldBuilder` and `GeoJsonWorldLoader` populate those centroid fields.
- The console host currently uses uniform hop cost and does not pass a heuristic.

This means Phase 1c is primarily a heuristic implementation and validation task, not a pathfinder rewrite.

## Non-goals

Phase 1c does not:

- change the `AStarPathfinder` search algorithm
- change the tie-breaking rules or determinism contract
- enable the heuristic by default for every caller
- define military movement costs
- add save/load changes

## Hard constraints

### Determinism

The heuristic must be deterministic across runs on the same input. It must not depend on unordered iteration or ambient process state.

### Admissibility

The heuristic must never overestimate the cheapest remaining path cost.

That means the heuristic can only be enabled for a cost model that exposes a proven global lower bound between geographic distance and path cost.

Examples:

- **Safe:** `cost >= distanceKm * minimumCostPerKm`
- **Not safe:** uniform hop cost `1` per edge, because one edge can span an arbitrary geographic distance

### Integer-only search state

No floating-point values may enter pathfinding state or ordering. The heuristic result must be a non-negative integer in the same unit as the edge-cost function.

### Opt-in behavior

Existing callers that pass `null` must keep the current Dijkstra behavior unchanged.

## Engine ownership

Pathfinding behavior is owned by the engine. Consumers may request a path, but they should not be responsible for assembling the heuristic policy themselves.

That means:

- the heuristic implementation lives in `SimEngine`
- admissibility rules are defined in `SimEngine`
- enablement stays under engine control
- external consumers do not construct pathfinding behavior from loose pieces unless the engine explicitly exposes that as part of its contract

## Proposed API

Keep `AStarPathfinder.FindPath(...)` unchanged as the low-level search primitive.

Add an engine-owned heuristic type that implements the engine's official geographic lower-bound policy:

- `HaversineHeuristic`

Add an engine-owned entry point for building or retrieving the official heuristic delegate:

- `HaversineHeuristic.Create(SimulationState state, int minimumCostPerKilometer)`
- returns `ProvinceHeuristic`

Behavior:

1. The engine reads province centroid data from `state.Entities`.
2. The engine returns a delegate that maps `(node, goal)` to a lower-bound integer cost.
3. The engine multiplies geographic lower-bound distance by `minimumCostPerKilometer`.
4. If `minimumCostPerKilometer <= 0`, the engine rejects the input.

Rationale:

- keeps `AStarPathfinder` unchanged
- keeps heuristic logic inside the engine
- makes the engine, not the host, responsible for admissibility policy
- preserves the existing delegate-based pathfinder contract without making consumers own the algorithm

## Proposed placement

- `src/SimEngine/State/Pathfinding/HaversineHeuristic.cs`
- optional supporting file if needed for fixed-point math helpers:
  - `src/SimEngine/State/Pathfinding/FixedPointGeo.cs`

Keep the public surface small. If helper math is only used internally, make it `internal`.

## Heuristic definition

The heuristic estimates the shortest possible surface distance between two province centroids on a sphere and converts that distance into a lower-bound path cost.

Conceptually:

`heuristic(node, goal) = floor(greatCircleDistanceKm(node, goal)) * minimumCostPerKilometer`

The flooring step is important. The heuristic must round down so approximation error cannot turn a lower bound into an overestimate.

## Fixed-point formulation

The implementation should avoid floating-point arithmetic during heuristic evaluation.

Recommended approach:

1. Convert centroid microdegrees to fixed-point radians.
2. Compute a great-circle lower bound using fixed-point math.
3. Round down to whole kilometers.
4. Multiply by `minimumCostPerKilometer` using checked or saturating integer math.
5. Clamp to `int.MaxValue` before returning.

### Acceptable implementation detail

Using `double` during one-time setup is acceptable only if it never enters the search state and only produces deterministic integer lookup tables or constants stored inside the helper.

Examples:

- precomputed fixed-point trig tables generated at startup and stored as integers
- fixed-point constants for radians conversion or Earth radius

What is not acceptable:

- using `double` directly inside the returned `ProvinceHeuristic`
- storing floating-point values in pathfinding state
- ordering the open set using floating-point values

## Distance model choice

Implement the engine heuristic as a lower-bound great-circle estimate, not as an exact travel-time model.

The heuristic should assume:

- Earth radius is a fixed integer constant
- centroid-to-centroid distance is the lower bound
- the actual path may be longer because it follows graph edges rather than a direct arc

This is sufficient for admissibility as long as the caller also supplies a valid `minimumCostPerKilometer` lower bound.

## Missing centroid behavior

If either endpoint lacks a `ProvinceComponent`, the engine heuristic construction should throw during setup or the returned delegate should throw when invoked.

Preferred behavior:

- validate all province entities once in `Create(...)`
- fail fast with `InvalidOperationException` when a referenced province is missing centroid data

The helper should not silently return zero for invalid data, because that would hide state corruption.

## Overflow behavior

The helper must not wrap on large distances or scales.

Rules:

- intermediate math uses `long`
- final value is clamped to `int.MaxValue`
- negative results are impossible; treat any internal negative as a bug and throw

## Console host wiring

Do not enable the heuristic in `PathCommand` yet.

The console host currently uses uniform hop cost:

- `(_, _) => 1`

That cost model does not provide a valid geographic lower bound, so a great-circle heuristic would not be admissible. The host should continue to pass `null` until the engine defines a movement-cost model with a proven `minimumCostPerKilometer`.

## Tests

Add tests in `tests/SimEngine.Tests/State`.

### Unit tests for the helper

- returns `0` when `node == goal`
- returns a non-negative value for valid province pairs
- is symmetric for symmetric inputs
- increases with geographic separation on simple synthetic data
- throws when `minimumCostPerKilometer <= 0`
- throws when centroid data is missing
- clamps instead of overflowing for very large scale factors

### Pathfinding integration tests

Use synthetic worlds built with `WorldBuilder`.

- A* with the heuristic returns the same total cost as Dijkstra on a graph whose edge costs satisfy the admissibility bound
- repeated runs return identical paths and total cost
- the heuristic reduces explored work compared with zero heuristic only if exploration counting can be added without distorting the public API; otherwise leave this to benchmarks

## Benchmark plan

Add `SimEngine.Benchmarks` only if the implementation lands.

Suggested benchmark cases:

- Dijkstra on a full loaded world graph
- A* with zero heuristic
- A* with the great-circle heuristic

Measurements:

- mean execution time
- allocation count
- relative speedup

Acceptance target:

- demonstrate a meaningful reduction in search time on long-distance queries before enabling the heuristic in production callers

## Implementation plan

### Step 1 — Add the engine heuristic type

Create `HaversineHeuristic` in `State/Pathfinding` with a `Create(SimulationState state, int minimumCostPerKilometer)` factory that returns `ProvinceHeuristic`.

### Step 2 — Add fixed-point geo math support

Implement the minimum internal math needed to convert centroid microdegrees into a deterministic lower-bound distance estimate. Keep helpers internal unless another production caller needs them.

### Step 3 — Add engine heuristic tests

Add unit tests that verify zero distance, symmetry, monotonicity on simple inputs, invalid input handling, and overflow protection.

### Step 4 — Add integration tests against `AStarPathfinder`

Construct synthetic worlds with edge costs that are explicitly compatible with the heuristic and assert that A* still returns the same optimal path cost as Dijkstra.

### Step 5 — Keep non-engine callers unchanged

Do not change `PathCommand` or any other host-level caller to use the heuristic yet.

### Step 6 — Add benchmarks

Create a benchmark project only after the helper and tests pass. Use it to validate that the extra complexity pays off on large graphs.

### Step 7 — Revisit engine-level enablement in Phase 4 or 5

Once movement costs exist, define and document the engine's global `minimumCostPerKilometer`, then expose heuristic-backed path queries through engine-owned call paths.

## Open decisions

1. **Exact formula choice**
   - Prefer the simplest fixed-point implementation that preserves a lower bound.
   - If exact fixed-point haversine is too complex, an explicitly lower-bounding approximation is acceptable.

2. **Factory validation scope**
   - Validate only provinces actually queried lazily, or validate every province with a `ProvinceComponent` up front.
   - Prefer up-front validation if the startup cost is small.

3. **Benchmark timing**
   - If the heuristic implementation remains dormant until military movement exists, benchmarking may move with that enablement step instead of Phase 1c itself.

## Recommended acceptance criteria

Phase 1c is complete when:

- the repo has a documented heuristic design
- `HaversineHeuristic.Create(...)` exists
- engine heuristic and integration tests pass
- current callers keep Dijkstra behavior unless they explicitly opt in
- the heuristic is documented as disabled for hop-based costs
