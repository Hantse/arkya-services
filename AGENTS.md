# Arkya Agent Guide

## Project Identity

Arkya is an MMORPG project built with Unity3D using DOTS (Data-Oriented Technology Stack) and Netcode for Entities.

- Active client: Unity project in `Unity3D/Arkya/` (separate Git repository).
- Unity Editor: `6000.6.6f1`, URP `17.6.0`; verify against `ProjectSettings/ProjectVersion.txt` and `Packages/manifest.json` before Unity work.
- `My project/` refers to the historical `6000.3.10f1` foundation and may be absent from the current checkout.
- Gameplay architecture: Unity DOTS, using Entities (ECS), the C# Job System, and Burst.
- Gameplay networking target: Unity Netcode for Entities, also called Netcode for ECS.
- Art direction: stylized, painted surfaces and carefully shaped volumes. The approved tree V3 is the reference for new environment objects; see `docs/art-direction/resources/README.md`. Keep existing Scinty vendor packs isolated.
- Character asset base: on 22 October 2026, the user selected `valkyrie_2019`, imported under `Unity3D/Arkya/Assets/Valkyrie/`, as the base for future characters. Preserve the vendor originals and build Arkya adaptations under `Assets/Arkya/Characters/`. The active creation/world catalog uses this common base; historical Human/Elf appearance IDs remain compatible. New creation exposes only the supplied human/feminine/long-haired model; see `docs/CHARACTER_CREATION.md`.
- Character art direction: the six user-provided images selected on 20 October 2026 remain the visual target for proportions, sculpted forms, faces, hair, equipment and material finish; see `docs/art-direction/resources/references/personnages-stylises-2026-10-20/README.md`. Apply this direction to the Valkyrie base.
- Backend target: .NET / ASP.NET Core services.
- Storage target: MongoDB.

Treat Arkya as a long-lived multiplayer project. Prefer clear ownership, server authority, testable contracts, and conservative changes over quick one-off scripts.

## Repository Layout

- `Unity3D/Arkya/` contains the active Unity client and dedicated-server project, versioned separately from the root services repository.
- `My project/` is the historical Unity project location.
- `backend/` is reserved for ASP.NET Core services.
- `docs/` contains architecture notes, decisions, and planning documents.

Do not edit generated Unity folders unless explicitly asked. This applies to every Unity project, including `Unity3D/Arkya/` and the historical `My project/`:

- `Library/`
- `Temp/`
- `Logs/`
- `UserSettings/`
- `obj/`

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

## Unity MCP Workflow

For Unity tasks, actively use the running Editor through MCP for inspection, asset and scene work, and validation. Read the [Unity MCP guide](docs/UNITY_MCP.md) and `Unity3D/Arkya/AGENTS.md` before substantial Unity work. Pure backend or documentation tasks do not require an Editor connection.

- The verified bridge is **CoplayDev Unity MCP**, package `com.coplaydev.unity-mcp`, exposed as `mcp__unityMCP__*` with resource server key `unityMCP`. Discover the current tools and schemas; do not assume generated skills for another provider match this bridge. Reuse the installed provider.
- Start by reading `mcpforunity://custom-tools` and `mcpforunity://instances`. If several Editors are connected, select the intended exact `Name@hash` using `set_active_instance`; do not reuse a saved hash, port or session ID.
- Confirm `mcpforunity://project/info` points to `Unity3D/Arkya/`, then read `mcpforunity://editor/state`. Use the payload under `data`, including `data.advice.ready_for_tools`, compilation, play mode and active scene. Wait for compilation/imports to settle before dependent actions.
- Prefer MCP resources and tools for live hierarchy, components, serialized references, assets, materials, shaders, cameras, rendering and console inspection. Use specialized MCP tools for scene/prefab/component/material changes and asset creation/moves/import settings so Unity tracks serialization and `.meta` files.
- Use `rg` and normal file patches for source exploration and C# edits when useful. After external asset/script changes, refresh through MCP as needed, wait for compilation/domain reload, then read console errors before using changed types or reporting success. Use reflection/documentation tools when available to verify Unity APIs against installed assemblies.
- When specialized tools cannot express an authorized Editor operation, use the discovered code-execution/custom-tool capability with a narrow scope and inspect the result. Do not add permanent helper scripts for a one-off action when the existing MCP tools suffice.
- Run relevant Edit Mode/Play Mode tests through MCP when available, and inspect job results through completion. For visible changes, inspect an actual Editor/Game/camera capture using the available capture tools. A connection probe or empty console alone does not validate gameplay, networking or visual quality.
- Inspect dirty scenes and prefab stages before edits or scene switches. Preserve existing user changes, console evidence and `.meta` GUIDs; save only assets/scenes changed for the task. Perform reversible MCP work within the authorized task without repeatedly asking permission. Explain runtime tests before starting them; clarify only when they would endanger unsaved work or trigger effects outside the authorized scope.
- Keep Editor mutations and active-instance routing sequential. If using subagents, designate one agent to control the shared Editor; parallelize independent file reads/reviews instead. Use bounded, paged queries and batch independent reads.
- If MCP is unavailable, report the actual failed probe, continue useful repository work, and state which Editor checks remain unverified. Do not claim Editor validation, install another bridge, restart Unity, or launch another Editor on the same open project as an automatic workaround.

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

- Unity changes should compile in the Editor before being considered complete. Prefer MCP for compilation readiness, console checks and relevant test execution; report unavailable Editor verification explicitly.
- Add Edit Mode tests for deterministic utility/domain logic.
- Add Play Mode tests for scene, conversion, and gameplay flows when behavior crosses Unity runtime boundaries.
- Backend changes should include unit tests for domain/application logic and integration tests for persistence or API behavior when practical.
- For networking features, verify at least a host/server and one client path before declaring success.

## Agent Workflow

### Git workflow

- Work directly on `master` in this services repository and on `main` in the separate `Unity3D/Arkya/` repository. Commits and pushes go to the corresponding default branch.
- Do not create task branches or additional Git worktrees unless the user explicitly requests them. This applies to subagents too.
- Preserve existing uncommitted changes. Do not reset, delete or overwrite existing branches or worktrees as part of this workflow.

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
