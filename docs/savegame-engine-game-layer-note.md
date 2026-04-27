# Savegame boundary note

## Goal

Clarify what belongs in the engine save layer versus the game layer so save files stay small, stable, and easy to validate.

## Core idea

Treat the current save system as two different concerns:

- a deterministic engine runtime snapshot
- a game-defined scenario reference plus mutable gameplay state

The engine should resume simulation.
The game layer should define authored content.

## Engine layer

The engine should own generic runtime state needed to resume deterministically:

- current simulation time
- previous tick time
- tick number
- root RNG state
- per-system RNG state
- scheduler/runtime options that affect deterministic resume
- generic persistence infrastructure for mutable state sections
- save format validation
- content identity validation hooks

The engine should not hard-code assumptions that authored world data must be embedded in every save.

## Game layer

The game layer should own scenario and world content:

- game id
- scenario id
- authored start date
- provinces
- province names
- centroids
- terrain
- neighbors / adjacency
- any other immutable world setup

The game layer should also own mutable gameplay state that changes after game start:

- ownership
- diplomacy
- economy
- population
- armies
- laws
- other game-specific simulation data

## Save rule

Persist a field only when at least one is true:

- it changes after game start
- it is required for deterministic resume
- it cannot be reconstructed from selected game content

Do not persist a field when all are true:

- it is scenario-authored
- it is immutable after start
- it can be reconstructed from the selected game content

## Applied to the current model

These items are likely scenario-owned and should not be stored in the save by default:

- `StartDate`
- province static component data
- adjacency / neighbor provinces
- entity lists, if entities are scenario-defined and stable

These items should remain in the save:

- current date/time
- previous tick
- tick number
- RNG state
- mutable gameplay state
- mutable relationships/components

## Recommended save shape

### 1. Save manifest

Keep a small manifest at the top level:

- save format version
- game id
- scenario id
- content version
- content hash

### 2. Engine runtime snapshot

Store only data required to resume the deterministic engine runtime:

- current time
- previous tick
- tick number
- root RNG snapshot
- system RNG snapshots
- any engine-owned runtime settings that affect replay/resume

### 3. Mutable state sections

Store only mutable game state sections.
These sections should be applied on top of freshly loaded scenario content.

## Recommended load flow

1. Read the save manifest.
2. Resolve the target game/scenario.
3. Validate `contentVersion` and `contentHash`.
4. Load the base scenario/world into a fresh `SimulationState`.
5. Apply mutable saved sections.
6. Restore engine runtime state.
7. Resume simulation.

## Why this split helps

Benefits:

- smaller save files
- clear ownership between engine and game layer
- simpler versioning
- easier compatibility checks
- less duplicated static data

Trade-off:

- saves depend on the referenced scenario content still existing and matching the expected hash/version

That trade-off is acceptable if the project wants scenario content to remain authoritative.

## Full snapshot alternative

A full snapshot is still valid if the project wants save files to be completely standalone and load even after authored content changes.

That approach gives:

- better isolation from content changes
- larger save files
- more duplication
- harder schema evolution
- weaker separation between engine and game layer

## Practical direction for this repo

Preferred direction:

- engine = deterministic runtime and persistence host
- game layer = scenario authoring and mutable gameplay state definition
- save file = runtime snapshot plus scenario identity/hash plus mutable delta

## Refactor outline

1. Classify every currently persisted field as one of:
   - engine runtime
   - static scenario content
   - mutable game state
2. Introduce a save manifest with `GameId`, `ScenarioId`, `ContentVersion`, and `ContentHash`.
3. Split the save payload into:
   - `EngineRuntimeSnapshot`
   - `GameContentReference`
   - `MutableStateSections`
4. Add a game-layer contract that builds base state from a scenario reference.
5. Remove static world data from the engine save payload.
6. Keep only mutable deltas/sections in saves.
7. Validate scenario/hash compatibility during load.
8. Add tests that cover compatibility failures and deterministic resume.

## Decision examples

- `startDate`: game layer, usually do not save
- province neighbors: game layer, do not save
- province names/terrain/centroids: game layer, do not save
- current simulation date: engine runtime, save
- RNG state: engine runtime, save
- changing ownership/relations: mutable game state, save
