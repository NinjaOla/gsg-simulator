# Copilot Instructions

## Project Guidelines
- The simulation engine should own core pathfinding behavior and APIs; pathfinding changes belong inside the engine rather than external orchestration.
- The gsg-simulator architecture follows a listen-server model: Single-player acts as a client that spawns/owns a local server (co-hosted silo) and communicates over the same Orleans grain/stream/SessionStateCache transport as multiplayer. There is no separate in-process engine rendering path; the GameSession.Engine backdoor is to be removed in step 13. Multiplayer involves multiple clients connecting to either a console-owned "listen" server or a dedicated (--server) server. The distinction between SP and MP is purely wiring (who hosts + how many clients connect); the grain remains the sole simulation authority.
- **gsg-simulator MP design decisions**:
  - Static/content data is NOT sent over the wire — each client loads the game's static data (map/geography) and mod content locally, and only a content hash (hash of loaded game + mod versions, building on the existing GameManifest ContentHash/ContentVersion/EnabledFeatures) is exchanged; the server enforces it as a compatibility gate on connect/join (reject on mismatch).
  - Server lifecycle: single-player uses a host-owned server (dies with the client); multiplayer uses a detached/shared server that survives independently so multiple clients can attach.
