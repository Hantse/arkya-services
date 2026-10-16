# Arkya Roadmap

## Phase 0 - Foundation

- Initialize source control and Git hygiene.
- Keep Unity Editor version pinned and confirm the package baseline.
- Add Entities and Netcode for Entities packages when the first networked prototype begins.
- Scaffold backend solution and local MongoDB development setup.
- Define initial shared contracts for account, character, and session flows.

## Phase 1 - Online Vertical Slice

- Boot a server-authoritative Unity world.
- Connect one client to a server.
- Spawn an owned player entity.
- Send predicted movement input.
- Interpolate a remote player.
- Persist a minimal character profile through the backend.

## Phase 2 - MMORPG Core Loop

- Character creation and selection.
- Basic combat interaction.
- Inventory and loot ownership.
- World zones or shards.
- Minimal admin/operator tools.
- Logging and telemetry for multiplayer sessions.

## Phase 3 - Production Hardening

- Load and soak testing for gameplay server sessions.
- Backend integration tests with MongoDB.
- Asset streaming and addressable content policy.
- CI for Unity compile checks and backend tests.
- Security pass for auth, economy, and persistence endpoints.
