# gsg-simulator

My test game/engine/stuff?

I want to create my own gsg game, i dont have experience in game development. Im a huge fan of the PDX gsgs like HOI, EU, VIC and CK so this will be inspired by that.

All tutorials show game logic tightly coupled into the game engine(unity etc) Which seems odd to me as a systems developer.
The idea is to create a simulation engine/logic as a library / seperate process and the UI/gameengine just interactis with it.

The first interaction is a console to manually verify that things work.

---

## Adding Features

### Architecture Layers

| Layer | Project | Responsibility |
|-------|---------|---------------|
| **Engine** | `SimEngine` | Domain-agnostic infrastructure: ECS, tick scheduling, pathfinding, events, serialization, PRNG |
| **Game** | `SimEngine.Game` | Game-specific rules: economy, population, diplomacy, war — anything that defines *your* game |

---

### Engine Feature vs Game Feature

| Engine (`SimEngine`) | Game (`SimEngine.Game`) |
|---|---|
| Pathfinding (A*, adjacency) | War movement rules |
| Entity/Component store | `ArmyComponent`, `DiplomacyComponent` |
| Event bus infrastructure | `WarDeclaredEvent`, `TradeAcceptedEvent` |
| Tick scheduling & cadence | Which systems run monthly vs daily |
| Deterministic PRNG | Battle outcome rolls |
| Relationship graph primitives | "Owns", "AlliedWith" labels |

**Rule of thumb:** If it's reusable across different grand-strategy games, it's engine. If it encodes *your game's* rules, it's game.

---

### Adding a Game Feature (e.g., Diplomacy)

#### 1. Component (data)

```csharp
// SimEngine.Game/Components/DiplomacyComponent.cs
namespace SimEngine.Game.Components;

public readonly record struct DiplomacyComponent(int ReputationE2, bool AtWar);
```

#### 2. System (logic)

```csharp
// SimEngine.Game/Systems/DiplomacySystem.cs
namespace SimEngine.Game.Systems;

public sealed class DiplomacySystem : ISimulationSystem
{
    public string Name => "Diplomacy";
    public string Key => "game.diplomacy.v1";
    public TickCadence Cadence => TickCadence.Monthly;
    public int Order => 30;
    public IReadOnlyCollection<StateKey> Reads => [ComponentStateKeys.Of<DiplomacyComponent>()];
    public IReadOnlyCollection<StateKey> Writes => [ComponentStateKeys.Of<DiplomacyComponent>()];

    public void Execute(in SimulationContext ctx)
    {
        foreach (var (id, _) in ctx.State.Entities.Query<DiplomacyComponent>())
        {
            ref var dip = ref ctx.State.Entities.GetRef<DiplomacyComponent>(id);
            // Reputation decays toward 0 each month
            dip = dip with { ReputationE2 = dip.ReputationE2 - (dip.ReputationE2 / 100) };
        }
    }
}
```

#### 3. Register in `GameDefinition.CreateDefault`

```csharp
systems:
[
    new PopulationSystem(),
    new EconomySystem(),
    new DiplomacySystem(),   // <-- add here
],
```

---

### Shared Feature Functionality (cross-cutting helpers)

For logic reused across multiple systems, create static helpers in the game project:

```csharp
// SimEngine.Game/Shared/CountryQueries.cs
namespace SimEngine.Game.Shared;

internal static class CountryQueries
{
    /// <summary>Sums production across all owned provinces.</summary>
    public static long TotalIncome(SimulationState state, EntityId countryId)
    {
        var income = 0L;
        foreach (var pid in state.Relationships.GetOutbound(countryId, RelationshipLabel.Owns))
        {
            if (state.Entities.TryGet<EconomyComponent>(pid, out var eco))
                income += eco.ProductionE2;
        }
        return income;
    }
}
```

Both `EconomySystem` and a future `BudgetSystem` can call `CountryQueries.TotalIncome(...)`.

---

### Adding an Engine Feature (e.g., Weighted Random Selection)

Engine features are game-agnostic utilities exposed via `SimulationContext`:

```csharp
// SimEngine/Random/RandomExtensions.cs
namespace SimEngine.Random;

public static class RandomExtensions
{
    /// <summary>Pick an index from weights using the system's deterministic PRNG.</summary>
    public static int WeightedIndex(this IDeterministicRandom rng, ReadOnlySpan<int> weights)
    {
        var total = 0;
        foreach (var w in weights) total += w;
        var roll = rng.Next(0, total);
        for (var i = 0; i < weights.Length; i++)
        {
            roll -= weights[i];
            if (roll < 0) return i;
        }
        return weights.Length - 1;
    }
}
```

Any game system can use it: `ctx.Random.WeightedIndex(stackalloc int[] { 70, 20, 10 })`.

---

### Actor (Player/AI) Interactions

Actors don't directly mutate state. They issue **commands** via the event bus that systems process on the next tick — keeping player and AI on equal footing.

#### 1. Define a command

```csharp
// SimEngine.Game/Commands/DeclareWarCommand.cs
namespace SimEngine.Game.Commands;

public readonly record struct DeclareWarCommand(EntityId Attacker, EntityId Defender) : ISimulationEvent;
```

#### 2. Actor issues the command (player input or AI decision)

```csharp
eventBus.Publish(new DeclareWarCommand(playerId, targetId));
```

#### 3. System consumes and resolves it

```csharp
public void Execute(in SimulationContext ctx)
{
    foreach (var cmd in ctx.Events.Consume<DeclareWarCommand>())
    {
        ref var attDip = ref ctx.State.Entities.GetRef<DiplomacyComponent>(cmd.Attacker);
        attDip = attDip with { AtWar = true, ReputationE2 = attDip.ReputationE2 - 500 };

        ref var defDip = ref ctx.State.Entities.GetRef<DiplomacyComponent>(cmd.Defender);
        defDip = defDip with { AtWar = true };
    }
}
```

---

### Checklist for a New Feature

1. **Component** — data `readonly record struct` in `SimEngine.Game/Components/`
2. **System** — implements `ISimulationSystem`, registered in `GameDefinition`
3. **Codec** — serialization in `SimEngine.Game/Serialization/` (for save/load)
4. **Shared helpers** — `SimEngine.Game/Shared/` for cross-system logic
5. **Commands** — `readonly record struct` implementing `ISimulationEvent` for actor input
6. **Engine extension** — only if the feature is game-agnostic (pathfinding, PRNG, scheduling)
Second will be a globe with the stride game engine.
Most likely pure C# for everything as that is what I enjoy.

---

## Multiplayer Architecture Plan

### Network Model: Local Server

Single-player and multiplayer use the **same architecture**. A game always runs as a server (Orleans silo) with one or more clients connected to it. In single-player the silo runs in-process — no separate executable, no network overhead.

```
Single-player                          Multiplayer (host + play)
┌────────────┐  in-process  ┌───────┐  ┌────────────┐  network  ┌───────┐
│ Console /  │◄───────────►│ Silo  │  │ Host UI    │◄────────►│ Silo  │
│ Game UI    │              │       │  │ (local)    │           │       │
└────────────┘              └───────┘  └────────────┘           │       │
                                       ┌────────────┐  network  │       │
                                       │ Player 2   │◄────────►│       │
                                       └────────────┘           └───────┘

Dedicated server: silo-only process, no UI. All players connect remotely.
```

**Why:** One code path for SP and MP. The `ConsoleHost` becomes just another client — it renders from the synced grain/stream/`SessionStateCache` path, not from a private in-process engine view. No separate single-player logic to maintain.

**Status:** The transport (delta sync, network silo hosting) is in place through step 12. Step 13 removes the last in-process engine backdoor so the host's own client renders like a remote one, adds the content-hash compatibility gate, and wires the SP (host-owned) / MP (detached/shared `--server`) lifecycles. See the implementation plan for the sequenced slices.

---

### New Projects

| Project | Purpose | Dependencies |
|---------|---------|-------------|
| **`SimEngine.Contracts`** | Orleans grain interfaces (`IGameSessionGrain`, `IPlayerGrain`, `ILobbyGrain`) and shared DTOs/commands. Pure interfaces — no implementation, no framework dependency beyond Orleans abstractions. | `SimEngine` (for `EntityId`, `ISimulationEvent`) |
| **`SimEngine.Server`** | Orleans silo host. Contains grain implementations that own `SimulationEngine` instances. Runs the tick loop, applies player commands, pushes state deltas. | `SimEngine`, `SimEngine.Game`, `SimEngine.Contracts`, `Microsoft.Orleans.Server` |
| **`SimEngine.Client`** | Orleans client helper library. Provides a typed client that connects to a local or remote silo. Used by `ConsoleHost`, future Stride UI, or any other frontend. | `SimEngine.Contracts`, `Microsoft.Orleans.Client` |
| **`SimEngine.Server.Tests`** | Integration tests for grains using Orleans `TestCluster`. | `SimEngine.Server`, `SimEngine.Contracts`, `xunit.v3` |

Updated dependency graph:

```
SimEngine (engine core, no actor/Orleans dependency)
  └── SimEngine.Game (game rules)
        └── SimEngine.Contracts (grain interfaces, commands, DTOs)
              ├── SimEngine.Server (grain implementations, silo host)
              │     └── SimEngine.Server.Tests
              └── SimEngine.Client (connection helper)
                    └── SimEngine.ConsoleHost (uses Client to talk to grains)
```

---

### Key Design Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| **Simulation authority** | Server-authoritative. `SimulationEngine` runs only inside `GameSessionGrain`. | Determinism, anti-cheat, single source of truth. |
| **Tick model** | Lockstep (PDX-style). All player commands for tick N are collected, then `Step()` runs, then results are broadcast. | Preserves deterministic replay. Players at different speeds submit commands at different rates but the simulation processes them in order. |
| **Command flow** | Player → `PlayerGrain` → `GameSessionGrain` command queue → `DeferredEventBus` → systems consume during `Execute()`. | Reuses existing event bus. Player and AI commands are identical `ISimulationEvent` records. |
| **State sync** | After each tick (or batch of ticks), the silo pushes a state delta/snapshot to connected player observers via Orleans Streams. | Clients are thin — they render state, they don't simulate. |
| **Static content** | Static/content data (map/geography, mods) is **not** sent over the wire. Each client loads it locally and exchanges only a content hash (building on `GameManifest` `ContentHash`/`ContentVersion`/`EnabledFeatures`); the server enforces it as a compatibility gate on join and rejects mismatches. | Keeps snapshots tiny at continent scale and protects deterministic lockstep — a client with different content can't desync the sim. |
| **Server lifecycle** | Single-player uses a host-owned server (co-hosted silo that dies with the client). Multiplayer spawns/attaches a detached/shared `--server` process that survives independently so multiple clients can attach. | One mechanism, two lifetimes: zero-config SP, persistent MP host. |
| **Persistence** | `GameSessionGrain` calls `SimulationEngine.Save()` to a stream, stored via Orleans grain persistence (e.g., file system, Azure Blob, ADO.NET). | Reuses existing save/load infrastructure. Auto-save every N ticks configurable. |
| **In-process mode** | For single-player and `ConsoleHost`, the silo is hosted in-process using `UseLocalhostClustering()`. No network serialization overhead. | Zero-config SP experience. Same grain code path. |

---

### Grain Design

#### `IGameSessionGrain` (one per active game)

- **State:** owns a `SimulationEngine` instance, player list, game speed, command queue.
- **Methods:** `JoinAsync`, `LeaveAsync`, `SubmitCommandAsync<T>(T command)`, `StepAsync`, `SetSpeedAsync`, `SaveAsync`, `GetStateSnapshotAsync`.
- **Tick loop:** a grain timer calls `StepAsync` at the current game speed. Commands are drained from the queue and published to the event bus before each `Step()`.

#### `IPlayerGrain` (one per player, virtual — activated on demand)

- **State:** player identity, current session ID, pending commands.
- **Methods:** `JoinSessionAsync`, `LeaveSessionAsync`, `SendCommandAsync`.
- Acts as a buffer and authentication boundary before forwarding to the session grain.

#### `ILobbyGrain` (singleton)

- **State:** list of active/joinable sessions with metadata (world name, player count, speed).
- **Methods:** `ListGamesAsync`, `CreateGameAsync`, `RemoveGameAsync`.

> **Implementation plan:** See [`docs/multiplayer-implementation-plan.md`](docs/multiplayer-implementation-plan.md) for phased roadmap, package requirements, and migration steps.


## Solution

Open the repository with `SimEngine.slnx`.

## Build

```powershell
dotnet build .\SimEngine.slnx
```

## Test

```powershell
dotnet test .\SimEngine.slnx
```
