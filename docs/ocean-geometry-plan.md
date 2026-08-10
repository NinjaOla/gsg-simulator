# Ocean Geometry Plan

Add real water to loaded worlds as first-class **sea provinces** with actual
ocean polygons — so the world model contains `Terrain.Sea` provinces, they
participate in adjacency (land↔sea coastal borders and sea↔sea borders), and the
renderer draws them as water rather than relying on the empty background.

Today `Terrain.Sea` exists in the enum but is never produced: the WorldGen tool
emits only land admin-1 features and `GeoJsonWorldLoader` hard-codes every
province to `Terrain.Land`.

## Default Decisions

These shape the whole feature. They are the assumed defaults; revisit before
executing if requirements differ.

1. **Coastal adjacency → negative-space ocean generation.** Generate ocean as
   the inverse of land within a bounding frame so land and sea polygons share
   exact vertices and the existing deterministic snapped-edge builder connects
   coast to sea unchanged. Lowest risk to the determinism contract. (Alternatives
   considered: proximity/tolerance matching; sea↔sea only for now.)
2. **New world id `world_admin1_ocean`.** Keeps all shipped worlds and existing
   saves byte-stable — regenerating an existing asset would change its
   `ContentHash` and break old saves.
3. **Subdivided named seas.** A handful of sea provinces rather than one global
   blob, so naval adjacency is meaningful without exploding file size.

## Content Contract

Land/sea is carried on each GeoJSON feature via a new optional `terrain`
property (`"land"` / `"sea"`), defaulting to `land` when absent — preserving
backward compatibility for existing assets (grid4, germany_admin1,
europe_west_admin1, world_admin1) that have no `terrain` key. Sea provinces are
**unowned** (no country in `*.countries.json` references them); the seeder only
assigns ownership to listed provinces, so unowned = neutral water.

## Implementation Phases

Work bottom-up so each layer compiles and is testable before the next depends on
it.

### Phase 1 — Content Contract & Loader

1. Add `terrain` property reading to `GeoJsonWorldLoader` with `Terrain.Land`
   fallback, replacing the hard-coded `Terrain.Land` (currently at the
   `ProvinceSeed` construction site).
2. Add a loader unit test asserting features without `terrain` default to Land
   and `terrain:"sea"` yields `Terrain.Sea`.

### Phase 2 — Adjacency

3. Decide and document the coastal adjacency strategy (default: negative-space so
   land and sea share vertices). Capture the decision if it changes.
4. Implement land↔sea and sea↔sea adjacency in `SharedEdgeAdjacencyBuilder`,
   preserving existing land↔land output exactly (determinism-critical).
5. Add adjacency tests covering land↔sea and sea↔sea against a small synthetic
   fixture.

### Phase 3 — Content Generation

6. Extend `SimEngine.WorldGen` with an `--ocean <file>` flag that ingests ocean
   geometry (public-domain Natural Earth `ne_10m_ocean` / `ne_50m_ocean`).
7. Emit ocean features tagged `terrain:"sea"` with province ids appended after
   land (append-only so land ProvinceIds stay stable), excluded from
   `*.countries.json`.

### Phase 4 — Rendering

8. Add an ocean fill color for `terrain:"sea"` features in `GeoJsonMapRenderer`
   (instead of the hue-rotation group color), keeping the background for true
   no-data gaps.
9. Add a renderer test/snapshot asserting sea features use the ocean fill.

### Phase 5 — Assets & Integration

10. Regenerate the `world_admin1_ocean` asset, register it in `WorldCatalog`, and
    wire content copies.
11. Update `GameSessionGrain` content-hash inputs if new content files are
    introduced.
12. Build the solution and run loader, adjacency, and renderer test suites to
    confirm determinism and no regressions.
13. Update `tests/SimEngine.Tests/TestAssets/README.md` and this doc to describe
    the `terrain` property and ocean generation flow.

## Risks & Open Questions

- **Coastal adjacency is the hard part.** Natural Earth land and ocean layers do
  not share exact vertices, so the shared-snapped-edge approach won't connect
  coast to sea without help. The negative-space default sidesteps this by
  construction; the fallbacks (proximity matching, sea-only) trade determinism
  risk or completeness.
- **Determinism + existing ProvinceIds.** Appending sea ids after land keeps land
  ids stable only if land ordering is untouched. Shipping ocean as a new world id
  avoids mutating a shipped asset's `ContentHash` and breaking old saves.
- **Ocean data volume.** A single global ocean MultiPolygon is huge and
  low-detail; subdividing into named seas balances gameplay and file size.
- **Backward compatibility.** Assets without a `terrain` property must still load
  as all-land (default fallback) — covered by a Phase 1 test.
