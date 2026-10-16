# 0001 - Project Baseline

## Status

Accepted.

## Context

Arkya is starting as a Unity3D MMORPG with a low-poly art direction based on Scinty assets. The target stack combines Unity ECS gameplay, Netcode for Entities multiplayer, ASP.NET Core backend services, and MongoDB persistence.

## Decision

- Use Unity as the client and real-time simulation technology.
- Use ECS/DOTS patterns for performance-sensitive gameplay systems.
- Use Netcode for Entities as the networking direction for replicated gameplay.
- Use ASP.NET Core for account, persistence, operational, and non-real-time services.
- Use MongoDB as the first persistence store.
- Keep generated Unity files, local user settings, and build outputs out of source control.

## Consequences

- Gameplay architecture must be server-authoritative from the beginning.
- Client code should distinguish presentation from simulation.
- Backend contracts need to be explicit and should not mirror Mongo documents directly.
- Large third-party art and audio assets will need a Git LFS policy before import.
- Early work should focus on a small online vertical slice instead of broad feature scaffolding.
