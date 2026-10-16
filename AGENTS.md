# Arkya Agent Guide

## Project Identity

Arkya is an MMORPG project built with Unity3D using DOTS (Data-Oriented Technology Stack) and Netcode for Entities.

- Client: Unity project in `My project/`.
- Unity Editor: `6000.3.10f1`.
- Gameplay architecture: Unity DOTS, using Entities (ECS), the C# Job System, and Burst.
- Gameplay networking target: Unity Netcode for Entities, also called Netcode for ECS.
- Art direction: stylized, painted surfaces and carefully shaped volumes. The approved tree V3 is the reference for new environment objects; see `docs/art-direction/resources/README.md`. Keep existing Scinty vendor packs isolated.
- Backend target: .NET / ASP.NET Core services.
- Storage target: MongoDB.

Treat Arkya as a long-lived multiplayer project. Prefer clear ownership, server authority, testable contracts, and conservative changes over quick one-off scripts.

## Repository Layout

- `My project/` contains the Unity client project.
- `backend/` is reserved for ASP.NET Core services.
- `docs/` contains architecture notes, decisions, and planning documents.

Do not edit generated Unity folders unless explicitly asked:

- `My project/Library/`
- `My project/Temp/`
- `My project/Logs/`
- `My project/UserSettings/`
- `My project/obj/`

Unity `.meta` files are part of the source of truth. When adding, moving, or deleting assets, keep the matching `.meta` changes.

## Working Language

- Project documentation can be written in French.
- Code identifiers, namespaces, API contracts, and commit messages should use English unless a domain term is intentionally French.
- Keep comments short and useful. Explain why, not what the next line already says.

## Unity Client Guidelines

- Keep simulation data-oriented. Components hold data; systems hold behavior.
- Prefer `ISystem`, `IJobEntity`, Burst-friendly code, and explicit system groups for hot gameplay paths.
- Use Entity Command Buffers for structural changes in ECS systems.
- Keep MonoBehaviours for presentation, authoring, bootstrapping, editor tooling, or non-hot UI code. Do not put authoritative gameplay state in MonoBehaviours.
- Use Bakers and authoring components to bridge GameObject assets into ECS data.
- Separate gameplay simulation from presentation: animation, VFX, cameras, and UI should observe state instead of owning it.
- Keep Scinty vendor assets isolated from Arkya-authored prefabs, materials, scripts, and overrides.
- Favor URP-friendly content budgets and deliberate LODs. Match the approved painted art reference without imposing coarse faceting. Be careful with material count, shadow casters, and dynamic lights.

## Netcode for ECS Guidelines

- The server is authoritative for movement, combat, inventory, economy, quests, loot, and persistence decisions.
- Clients may predict local input, but server reconciliation wins.
- Remote entities should use interpolation unless a feature explicitly needs prediction.
- Define networked data as small, stable ECS components. Avoid syncing presentation-only state.
- Be deliberate with ghost fields, quantization, send rates, and owner-predicted entities.
- Treat bandwidth as a gameplay budget. New replicated fields need a reason.
- Keep client-only, server-only, and shared systems separated by world and assembly boundaries.

## Backend Guidelines

- The backend is responsible for accounts, authentication/session flow, character data, persistence, entitlement, social systems, and operational APIs.
- Use ASP.NET Core with clear application boundaries: API layer, application services, domain model, persistence adapters.
- MongoDB documents should have stable IDs, schema versioning, and intentional indexes.
- Never trust client-submitted state for inventory, currency, progression, combat results, or economy changes.
- Use async APIs with cancellation tokens for IO paths.
- Keep DTOs/contracts explicit. Do not leak Mongo persistence documents directly as public API responses.
- Keep secrets out of the repo. Use local environment files, user secrets, or deployment secrets.

## Testing And Verification

- Unity changes should compile in the Editor before being considered complete.
- Add Edit Mode tests for deterministic utility/domain logic.
- Add Play Mode tests for scene, conversion, and gameplay flows when behavior crosses Unity runtime boundaries.
- Backend changes should include unit tests for domain/application logic and integration tests for persistence or API behavior when practical.
- For networking features, verify at least a host/server and one client path before declaring success.

## Agent Workflow

When starting any Arkya task:

1. Read this file, `README.md`, and `docs/ARCHITECTURE.md` first.
2. Respect the project constraints: authoritative server, untouched Unity-generated folders, preserved Unity `.meta` files, and no committed secrets.
3. Implement only the requested step unless a small adjacent change is required to make it work.
4. Update useful documentation when behavior, architecture, setup, or contracts change.
5. Finish by reporting changed files and the verification performed.

Before changing code:

1. Inspect the nearest relevant files and package manifests.
2. Check whether the change belongs in Unity, backend, docs, or shared contracts.
3. Keep generated folders and vendor assets untouched unless the task explicitly requires them.
4. Make small, scoped edits that match the existing structure.

After changing code:

1. Run the narrowest useful verification available.
2. Update docs when architecture, contracts, or workflow expectations changed.
3. Report any tests or checks that could not be run.

## Open Project Decisions

- Decide the final Unity package set for Entities, Netcode for Entities, Physics, and baking workflow.
- Define the build and deployment configuration for the authoritative Unity dedicated server communicating with ASP.NET services.
- Initialize Git and Git LFS before importing large third-party art/audio/source assets.
