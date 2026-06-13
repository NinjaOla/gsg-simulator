# ADR-001 — Multiplayer runtime: Orleans

- **Status:** Accepted (go)
- **Date:** 2026-06-13
- **Deciders:** Ola
- **Context milestone:** MP Milestone 2 (Orleans go/no-go spike)

## Context

`SimEngine` is a deterministic, single-threaded-per-session simulation. The
multiplayer design (see [README](../README.md) and
[multiplayer-implementation-plan.md](multiplayer-implementation-plan.md)) is
server-authoritative lockstep: one engine per session, players submit commands,
the server steps and broadcasts results. Single-player and multiplayer share
one code path — in SP the server runs in-process.

Milestone 1 put the engine inside an Orleans `GameSessionGrain` and proved the
console host works as a thin client through it. The first MP commit message
("mp with orleans. dont know if thats good but lets go") flagged that the
runtime choice itself was never actually validated. This ADR records that
validation before we build `PlayerGrain` / `LobbyGrain` and commit deeper.

The realistic alternative considered is a **plain ASP.NET Core host + SignalR**:
a singleton service holding a dictionary of `SimulationEngine` instances, a
`Channel`/lock per session for thread-safety, and SignalR hubs for the
events-out path.

## Spike

`tools/SimEngine.MpSpike` stands up a standalone silo (localhost clustering,
gateway :30000, in-memory streams), connects **two genuinely external Orleans
clients over the loopback TCP gateway** sharing one session, and measures the
cost the actor runtime adds over a bare engine. Source:
[tools/SimEngine.MpSpike](../tools/SimEngine.MpSpike).

### Results (germany_admin1, 16 provinces, .NET 10, Debug, single dev box)

| Path | mean | p50 | p99 |
|------|-----:|----:|----:|
| Direct `engine.Step()` (no Orleans) | 0.001 ms | 0.000 ms | 0.012 ms |
| Co-hosted grain `StepAsync(1)` (in-proc) | 0.039 ms | 0.032 ms | 0.093 ms |
| External client `StepAsync(1)` (loopback TCP) | 0.212 ms | 0.201 ms | 0.369 ms |
| Stream delivery (A steps → B observes) | 109.7 ms | 109.2 ms | 111.9 ms |

Save snapshot (`germany_admin1` after 120 ticks): **14.6 KB raw, 1.8 KB gzip**
(12% ratio).

These are single-day ("empty") ticks, so per-step engine work is negligible and
the numbers isolate the **call/transport overhead** of each layer — exactly the
go/no-go concern.

### Budget verdicts

| Budget | Actual | Verdict |
|--------|-------:|:-------:|
| In-proc grain overhead < 1 ms | 0.038 ms | **PASS** |
| External round-trip < 10 ms | 0.212 ms | **PASS** |
| Raw snapshot < 1 MB | 0.014 MB | **PASS** |

### Findings worth carrying forward

1. **Grain overhead is in the noise.** ~0.04 ms in-process and ~0.2 ms over a
   real TCP gateway. For a lockstep GSG stepping at most a few times per second,
   the runtime cost is irrelevant next to the simulation itself.

2. **Stream delivery latency (~110 ms) is a configuration artifact, not a
   ceiling.** Orleans in-memory streams use a pulling agent whose default poll
   period is 100 ms; the measured ~110 ms is that cadence, not serialization or
   transport cost. For a turn/day-paced GSG this is already fine; if we want
   snappier push we tune `StreamPullingAgentOptions` or move to a push-based
   provider. **Do not read this as "Orleans streams are slow."**

3. **Snapshot size is fine now, but won't scale linearly forever.** ~913
   bytes/province raw. A full Natural Earth world (~4,500 provinces) extrapolates
   to ~4 MB raw / ~0.5 MB gzip. That blows the 1 MB raw budget at full scale,
   which confirms the plan's existing intent to push **deltas, not full
   snapshots**, to clients per tick (MP plan step 11). Full-snapshot sync is
   acceptable for current dev-scale worlds only.

## Decision

**Proceed with Orleans** as the multiplayer runtime.

### Why Orleans over ASP.NET Core + SignalR

- **Per-session single-threading for free.** A grain processes one request at a
  time. The engine's hard invariant — no concurrent `Step()` / read on a
  session — is enforced by the runtime. The SignalR alternative requires us to
  hand-roll a lock or channel per session and get it right ourselves.
- **One code path for SP and MP, already proven.** `UseLocalhostClustering()`
  in-process for single-player; the same grain code accepts external clients
  over the gateway. Milestone 1 + this spike demonstrate both with no divergent
  logic. The SignalR path would still need a hosted service and lifecycle we'd
  build from scratch.
- **Virtual-actor lifecycle fits sessions/players/lobbies.** `GameSessionGrain`,
  a future `PlayerGrain`, and `LobbyGrain` are textbook grain shapes:
  activate-on-demand, key-addressable, independently persistable. SignalR gives
  us none of this; we'd reimplement activation and addressing.
- **Persistence and streaming are first-party.** Grain persistence can wrap the
  existing `SimulationSaveSerializer`; streams give the events-out path without
  a separate hub contract.

### What this decision is *not*

- Not a commitment to in-memory streams for production — provider/poll tuning is
  a later, low-risk knob (finding 2).
- Not a commitment to full-snapshot client sync — delta sync is required before
  full-scale worlds (finding 3).
- Low blast radius if reversed: Orleans touches only `SimEngine.Contracts`,
  `SimEngine.Server`, and `SimEngine.Client`. `SimEngine` and `SimEngine.Game`
  have no actor/Orleans dependency, and the session API shape from Milestone 1
  transfers to any transport.

## Consequences

- Unblocks MP plan Phase 3: `PlayerGrain` (command buffering, join/leave),
  `LobbyGrain` (game listing/creation), and network silo hosting.
- Streams are the chosen events-out mechanism; revisit the provider and poll
  period when real-time push latency matters.
- Add a delta-sync mechanism before shipping continent-scale worlds; until then,
  full snapshots are the client-sync stopgap.
- `tools/SimEngine.MpSpike` stays in-tree as a re-runnable benchmark; re-run it
  if grain/stream wiring changes materially.
