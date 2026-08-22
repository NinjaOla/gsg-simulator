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

## Content Roadmap

Planned world-data features and their sequenced implementation slices:

> **Ocean geometry:** See [`docs/ocean-geometry-plan.md`](docs/ocean-geometry-plan.md)
> for adding first-class sea provinces (real ocean polygons, land↔sea adjacency,
> water rendering) via the `terrain` GeoJSON property and the `SimEngine.WorldGen`
> pipeline.

---

## Multiplayer Architecture

### Network Model: Local Server + Local Client

Single-player and multiplayer use the **same client/server model**. The simulation always runs in a server-side Akka actor system, and every UI talks to it through a separate client actor system. For local single-player that still means **server process + client process** semantics, even when both are started by the same executable on the same machine.

```
Single-player (localhost)                  Multiplayer / dedicated server
┌──────────────┐  Akka.Remote  ┌──────────────┐  ┌──────────────┐  network  ┌──────────────┐
│ Console/UI   │◄────────────►│ Server       │  │ Client/UI    │◄────────►│ Server       │
│ ClientSystem │              │ ServerSystem │  │ ClientSystem │          │ ServerSystem │
└──────────────┘              └──────────────┘  └──────────────┘          └──────────────┘
                                                                     ┌──────────────┐
                                                                     │ Client/UI 2  │
                                                                     │ ClientSystem │
                                                                     └──────────────┘
```

**Why:** this prevents a separate single-player path from emerging. The local host must go through the same message contracts, serialization, and state sync flow as a remote client.

---

### New Projects

| Project | Purpose | Dependencies |
|---------|---------|-------------|
| **`SimEngine.Contracts`** | Shared message contracts, DTOs, session commands, state snapshots. Keep these free of engine-hosting details so both client and server can depend on them cleanly. | `SimEngine` (for IDs/events shared across boundaries) |
| **`SimEngine.Server`** | Akka.NET server host. Runs the server actor system, lobby/session actors, country actors, and owns authoritative `SimulationEngine` instances. | `SimEngine`, `SimEngine.Game`, `SimEngine.Contracts`, Akka server packages |
| **`SimEngine.Client`** | Akka.NET client helper library. Owns the client actor system, connects to a local or remote server, and exposes a typed API for UI frontends. | `SimEngine.Contracts`, Akka client/remoting packages |
| **`SimEngine.Server.Tests`** | Actor and integration tests around session flow, command routing, and client/server interaction. | `SimEngine.Server`, `SimEngine.Contracts`, test packages |

Updated dependency graph:

```
SimEngine (engine core, no Akka dependency)
  └── SimEngine.Game (game rules)
        └── SimEngine.Contracts (messages, commands, DTOs)
              ├── SimEngine.Server (Akka host, sessions, lobby, country actors)
              │     └── SimEngine.Server.Tests
              └── SimEngine.Client (Akka client connection helper)
                    └── SimEngine.ConsoleHost (uses Client to talk to server)
```

---

### Server-Authoritative Session Model

`SimulationEngine` remains the only simulation authority. Actors do **not** mutate simulation state directly.

Instead:

1. client/player actors and AI/country actors observe synced state,
2. they issue immutable commands,
3. the authoritative session actor queues those commands,
4. the session actor publishes them to the engine's existing event bus,
5. the engine advances one deterministic tick.

That preserves the current engine model while using actors as the running parts of the application.

---

### Actor Topology

```
ActorSystem (server)
│
├── /user/lobby
│     └── LobbyActor
│           - lists/creates sessions
│           - routes join requests
│
└── /user/sessions/{sessionId}
      └── GameSessionActor
            - owns SimulationEngine
            - owns tick timer / lockstep gate
            - owns command queue
            - broadcasts snapshots
            │
            ├── country-{id}
            │     ├── AICountryActor
            │     └── PlayerCountryActor
            │
            └── observer-{playerId}
                  └── pushes session updates to connected clients
```

On the client side, a separate actor system hosts a client/session bridge actor that connects to the lobby/session actors and feeds state into the UI.

---

### Key Design Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| **Runtime model** | Akka.NET end-to-end for lobby, sessions, country actors, and client/server messaging. | One framework and one actor model across the whole app. |
| **Single-player topology** | Still client/server. Local SP uses a local server system and a separate client system communicating over the same contracts. | Minimizes SP/MP divergence and catches boundary issues early. |
| **Simulation authority** | Server-authoritative. `SimulationEngine` lives inside `GameSessionActor` only. | Determinism, anti-cheat, single source of truth. |
| **Tick model** | Lockstep. Commands for tick N are collected, then the session actor advances the engine and broadcasts results. | Preserves deterministic replay. |
| **Command flow** | Client/player actor or country actor → `GameSessionActor` command queue → `DeferredEventBus` → systems consume during `Execute()`. | Reuses existing engine/event design. |
| **Country execution model** | Countries can be actors (AI or player-controlled), but they only issue commands. | Good fit for autonomous decision making without letting actors own authoritative state. |
| **State sync** | The server pushes snapshots or deltas to connected clients/observer actors after each tick. | Clients stay thin and render synchronized state only. |
| **Static content** | Static/content data is not sent over the wire. Each client loads content locally and exchanges only a content hash/version/feature signature. | Keeps traffic low and blocks mismatched-content desyncs. |
| **Server lifecycle** | Single-player starts a host-owned local server; multiplayer can use a detached shared server process. | Same architecture, different hosting lifecycle. |
| **Persistence** | Session persistence wraps `SimulationEngine.Save()` / `Load()` instead of inventing a second format. | Reuses the deterministic snapshot infrastructure already in the engine. |

---

### Session and Country Actor Roles

#### `LobbyActor`

- Lists joinable games
- Creates new sessions
- Resolves a player/client to a target session actor

#### `GameSessionActor`

- Owns one authoritative `SimulationEngine`
- Owns the session tick cadence and command queue
- Applies player/AI commands to the event bus before stepping the engine
- Broadcasts state snapshots/deltas after each tick
- Spawns and supervises per-country and per-observer child actors

#### `CountryActor`

- Represents one country/nation at the application layer
- Has AI or player-control implementations
- Observes state and issues commands
- Never directly mutates the engine state

#### `ObserverActor` / client bridge actor

- Delivers state updates from the authoritative session to the connected UI
- Keeps the frontend decoupled from direct engine access

---

> **Implementation plan:** See [`docs/multiplayer-implementation-plan.md`](docs/multiplayer-implementation-plan.md) for phased rollout, package requirements, and migration steps.

---

### Current State (as built)

The Akka.NET re-platform described above is implemented. The runtime is now Akka.NET end-to-end (Orleans has been fully removed).

**Projects as they exist today:**

| Project | Role |
|---------|------|
| `SimEngine` | Engine core (no Akka dependency) |
| `SimEngine.Game` | Game rules (no Akka dependency) |
| `SimEngine.Contracts` | Shared message protocols (`SessionProtocol`, `PlayerProtocol`, `LobbyProtocol`), DTOs, routing markers, and `AkkaExecutionMode` |
| `SimEngine.Server` | Akka host: session/player/lobby actors, message extractors, `GenericChildPerEntityParent`, `WithSimEngineActors`, and `ILocalEngineProvider` |
| `SimEngine.Client` | Client facade: `GameClient` (local registry or remote path) and per-session `SessionClient` (`Ask`-based) |
| `SimEngine.Game.Ui.Console` | Console UI; hosts a local server and connects via `GameClient`. `--server`/`--host`/`--port` run a detached remoting server |
| `SimEngine.Game.Ui.Stride` | Stride globe UI frontend |
| `SimEngine.Server.Tests` | Actor tests using a raw `ActorSystem` + `Ask` (`ServerTestHarness`) |
| `tools/SimEngine.MpSpike` | Latency spike tool measuring direct/in-proc/remote advance round-trips |

**What works now:**

- **Session actor** (`GameSessionActor`) owns the authoritative `SimulationEngine`; clients only send messages.
- **Two execution modes** via `WithSimEngineActors(AkkaExecutionMode)`: `LocalTest` uses `GenericChildPerEntityParent` (child-per-entity); `Clustered` uses a sharded region.
- **Command flow:** commands are queued on the session actor and applied before each `Advance`.
- **Content-hash gate:** static content is not sent over the wire — only a content hash is exchanged and enforced on join.
- **Events-out:** subscribers receive per-tick `SessionStreamUpdate` broadcasts (no polling).
- **Persistence:** save/load wraps `SimulationEngine.Save()`/`Load()`.
- **Determinism:** stepping through the session actor produces byte-identical saves to a directly-constructed engine.

---

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
