# Arkya

Arkya is a Unity3D MMORPG project using Unity DOTS (Data-Oriented Technology Stack) for gameplay simulation and Netcode for Entities for server-authoritative multiplayer. Its visual direction is stylized and painted. The approved tree V3 is the visual reference for new environment objects, preserved in the [art direction resources](docs/art-direction/resources/README.md).

## Current Shape

- Active Unity project for art comparison and the area prototype: `Unity3D/Arkya/`, Unity `6000.6.6f1`, URP `17.6.0`; see the [Unity art test guide](docs/art-direction/unity-test/README.md).
- Starter island: editable native terrain with a level 1–6 layout in the active Unity project; [scene, editing guide and planned copies per realm](docs/world/STARTER_ISLAND.md). Gameplay content and instance admission remain to implement.
- Island presentation: four-hour day/night cycle, sun, moon and stars, painted ground and local animated/audio ambience; [review controls and validation](docs/world/STARTER_ISLAND_ENVIRONMENT.md).
- Island weather: gradual cloud cover, rain, wind and decorative thunderstorms; [controls and validation](docs/world/STARTER_ISLAND_WEATHER.md).
- Boundary water: a boiling violet ocean ring marks the island limit, with a five-second server ECS death rule after water contact; [configuration and current integration scope](docs/world/STARTER_ISLAND_BOUNDARY_WATER.md).
- Existing gameplay foundation: `My project/`, Unity `6000.3.10f1`; unchanged by the art comparison integration.
- Rendering: URP package is present.
- Input: Unity Input System package is present.
- Character creation: an isolated in-game UI Toolkit scene supports rigged Humans and Elves, appearance choices, Discord login and Characters/MongoDB persistence, plus a local Netcode movement test; see the [character creation guide](docs/CHARACTER_CREATION.md).
- Discord social: the official Social SDK adds friends, presence and social-lobby invitations to character creation and its local test; [setup, scope and validation](docs/DISCORD_SOCIAL.md). Chat/voice UI and authoritative game admission remain separate work.
- Gameplay simulation: Unity DOTS, using Entities (ECS), the C# Job System, and Burst.
- Gameplay networking target: Netcode for Entities, with server authority, local prediction, and remote entity interpolation. The current area prototype interpolates all received ghosts.
- Inventory and harvesting foundation: server ECS rules for up to 150 slots, carry weight, resource nodes, tools and socketed stones; [rules, Inspector setup and current integration limits](docs/GAMEPLAY_FOUNDATION.md).
- Area prototype: two server processes and two clients on one continuous map, with entity authority transfers and cross-area combat; see the [build and run guide](docs/AREA_PROTOTYPE.md).
- Backend: separate .NET 10 Identity (Discord accounts/sessions) and Characters services in `backend/`, alongside the existing local development API.
- Public API deployment: a YARP gateway routes `api.arkya.online` to the backend services; [k3s setup and Docker image workflows](backend/deploy/gateway/README.md).
- Backend persistence: MongoDB, with a separate database owned by each service and local Compose setup.
- Quest content: an ASP.NET admin edits kill objectives and publishes immutable catalogs; dedicated area servers load a pinned version at startup and save character progress through Characters. See the [quest content guide](docs/QUEST_CONTENT.md).
- Server target: authoritative Unity dedicated server communicating with the ASP.NET Core services; initial contracts use HTTPS JSON.
- Realm model: a character belongs to one persistent realm, whose simulation can span several area servers. Realm APIs and character realm binding remain planned.

The repository is still in foundation mode. Identity and Characters establish the backend boundaries; the isolated Unity creation scene integrates Discord login and persistent character appearance, while the area prototype exercises continuous simulation across two owners. Realm selection, persistent realm-bound characters, and dedicated-server admission still need integration. The existing gameplay bootstrap continues to use its local development flow.

## Target Structure

```text
arkya/
  AGENTS.md
  README.md
  docs/
  backend/
  My project/
  Unity3D/Arkya/
```

## Development Notes

- The root repository is [arkya-services](https://github.com/Hantse/arkya-services) (`master`), containing backend services, documentation and art sources.
- `Unity3D/` is ignored by the root repository. The Unity project has its own repository, [arkya-mmorpg](https://github.com/Hantse/arkya-mmorpg) (`main`), at `Unity3D/Arkya/`; commit and push it separately.
- Install Git LFS before cloning either repository, then run `git lfs pull` inside each checkout to download binary assets. Text sources and Unity `.meta` files remain in regular Git.
- `sites/arkya/` is a separate local repository managed by the Sites publishing workflow and is also excluded from the root repository.
- Local build outputs, validation checkouts, browser captures and machine configuration are ignored; approved art sources and documentation captures remain versioned.

- Keep Unity-generated folders out of source control.
- Keep Unity `.meta` files with their assets.
- Keep third-party Scinty assets separate from Arkya-authored content.
- Treat the server as authoritative for gameplay and persistence.
- Keep public contracts explicit between Unity, backend services, and persistence.

## Useful Documents

- [Site vitrine et atlas visuel des directions artistiques](sites/arkya/README.md).
- `AGENTS.md`: working rules for coding agents and collaborators.
- `docs/ARCHITECTURE.md`: target system architecture.
- `docs/ENTER_WORLD_FLOW.md`: current local development flow from dev session to mock game server entry.
- `docs/ROADMAP.md`: first implementation phases.
- `docs/decisions/0001-project-baseline.md`: initial baseline decision.
- [Backend setup and API contracts](backend/README.md).
- [In-game character creation, rigs, server persistence and local movement test](docs/CHARACTER_CREATION.md).
- [Discord Social SDK: friends, presence, social invitations and configuration](docs/DISCORD_SOCIAL.md).
- [Combat, classes, multiclass progression and inherited spells — design](docs/GAMEPLAY_CLASSES.md).
- [Animal soul bonds and wolf/fox totem companions — design and art assets](docs/GAMEPLAY_TOTEMS.md).
- [Backend service boundaries and transport decision](docs/decisions/0002-backend-services.md).
- [Realms, characters, and continuous area authority](docs/decisions/0003-realms-and-area-authority.md).
- [Starter island, levels 1–6, and planned copies per realm](docs/world/STARTER_ISLAND.md).
- [Starter island sky, painted ground and ambience](docs/world/STARTER_ISLAND_ENVIRONMENT.md).
- [Starter island boundary water and server exposure rule](docs/world/STARTER_ISLAND_BOUNDARY_WATER.md).
- [Unity area prototype: build, launch, and verification](docs/AREA_PROTOTYPE.md).
- [Quest administration, versioned publication, dedicated loading and verification](docs/QUEST_CONTENT.md).
