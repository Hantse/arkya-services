# Arkya

Arkya is a Unity3D MMORPG project using Unity DOTS (Data-Oriented Technology Stack) for gameplay simulation and Netcode for Entities for server-authoritative multiplayer. Its visual direction is stylized and painted. The approved tree V3 is the visual reference for new environment objects, preserved in the [art direction resources](docs/art-direction/resources/README.md).

## Current Shape

- Active Unity project for art comparison and the area prototype: `Unity3D/Arkya/`, Unity `6000.6.6f1`, URP `17.6.0`; see the [Unity art test guide](docs/art-direction/unity-test/README.md).
- Starter island: editable native terrain with a level 1–6 layout, reachable from character creation for [local playable exploration](docs/world/STARTER_ISLAND_PLAYABLE.md) and the [web beta dedicated flow](docs/WEB_BETA.md), with server-owned movement and an orbit camera. Quests and progression remain separate; [scene and editing guide](docs/world/STARTER_ISLAND.md).
- Island presentation: four-hour day/night cycle, sun, moon and stars, painted ground and local animated/audio ambience; [review controls and validation](docs/world/STARTER_ISLAND_ENVIRONMENT.md).
- Island weather: gradual cloud cover, rain, wind and decorative thunderstorms; [controls and validation](docs/world/STARTER_ISLAND_WEATHER.md).
- Boundary water: a boiling violet ocean ring marks the island limit, with a five-second server ECS death rule after water contact; [configuration and current integration scope](docs/world/STARTER_ISLAND_BOUNDARY_WATER.md).
- Existing gameplay foundation: `My project/`, Unity `6000.3.10f1`; unchanged by the art comparison integration.
- Rendering: URP package is present.
- Input: Unity Input System package is present.
- Character creation: an isolated in-game UI Toolkit scene supports rigged Humans and Elves, appearance choices, Discord login and Characters/MongoDB persistence, plus a local Netcode movement test; see the [character creation guide](docs/CHARACTER_CREATION.md).
- Discord social: the official Social SDK adds friends, presence and social-lobby invitations to native character creation and its local test; [setup, scope and validation](docs/DISCORD_SOCIAL.md). The web client uses the website's Discord-backed Arkya session and separate server admission. Chat/voice UI remains separate work.
- Gameplay simulation: Unity DOTS, using Entities (ECS), the C# Job System, and Burst.
- Gameplay networking target: Netcode for Entities, with server authority, local prediction, and remote entity interpolation. The current area prototype interpolates all received ghosts.
- Inventory and harvesting foundation: server ECS rules for up to 150 slots, carry weight, resource nodes, tools and socketed stones; [rules, Inspector setup and current integration limits](docs/GAMEPLAY_FOUNDATION.md).
- Area prototype: two server processes and two clients on one continuous map, with entity authority transfers and cross-area combat; see the [build and run guide](docs/AREA_PROTOTYPE.md).
- Backend: separate .NET 10 Identity (Discord accounts/sessions) and Characters services in `backend/`, alongside the existing local development API.
- Public API deployment: a YARP gateway routes `api.arkya.online` to the backend services; [k3s setup and Docker image workflows](backend/deploy/gateway/README.md).
- Realm deployment backoffice: `Arkya.Admin` manages persistent realm definitions through `Arkya.Realms.Api`, which creates and observes dedicated workloads in approved k3s namespaces; [architecture, scope and setup](docs/REALM_DEPLOYMENT.md). The web beta adds player admission, WebSocket destinations and Unity Linux dedicated build tooling; deploying its image and public WSS route still requires the real environment configuration.
- Backend persistence: MongoDB, with a separate database owned by each service and local Compose setup.
- Quest content: an ASP.NET admin edits kill objectives and publishes immutable catalogs; dedicated area servers load a pinned version at startup and save character progress through Characters. See the [quest content guide](docs/QUEST_CONTENT.md).
- News and release notes: the admin prepares drafts and publishes version-linked notes or news through Content; the launcher reads their anonymous public feed. See the [publication contracts and guide](docs/PUBLICATIONS.md).
- Server target: authoritative Unity dedicated server communicating with the ASP.NET Core services; initial contracts use HTTPS JSON.
- Realm model: the web beta implements an authenticated player catalogue, immutable character realm binding and one-use admission tickets backed by character leases. Its island uses one dedicated per destination; integration with multi-area simulation remains separate.

The repository is still in foundation mode. The web beta connects the website session, persistent character selection and a dedicated island through real services, WSS admission and server-owned position persistence. Build tooling and integration tests are included; this is not a claim that production is deployed. The separate area prototype exercises continuous simulation across two owners, and the historical gameplay bootstrap retains its local development flow.

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
- `sites/arkya/` is versioned in this services repository. Its CI publishes the frontend image to GHCR for the Argo CD beta deployment; manual Sites publication remains available separately.
- Local build outputs, validation checkouts, browser captures and machine configuration are ignored; approved art sources and documentation captures remain versioned.

- Keep Unity-generated folders out of source control.
- Keep Unity `.meta` files with their assets.
- Keep third-party Scinty assets separate from Arkya-authored content.
- Treat the server as authoritative for gameplay and persistence.
- Keep public contracts explicit between Unity, backend services, and persistence.

## Useful Documents

- [Sessions renouvelables du site et de l’admin](docs/AUTH_SESSIONS.md).
- [Launcher Windows : installation, mises à jour et distribution](docs/WINDOWS_LAUNCHER.md).

- [Site vitrine et atlas visuel des directions artistiques](sites/arkya/README.md).
- `AGENTS.md`: working rules for coding agents and collaborators.
- `docs/ARCHITECTURE.md`: target system architecture.
- `docs/ENTER_WORLD_FLOW.md`: current local development flow from dev session to mock game server entry.
- `docs/ROADMAP.md`: first implementation phases.
- `docs/decisions/0001-project-baseline.md`: initial baseline decision.
- [Backend setup and API contracts](backend/README.md).
- [CI coverage, Unity runners and workflow activation](docs/CI.md).
- [In-game character creation, rigs, server persistence and local movement test](docs/CHARACTER_CREATION.md).
- [Web beta: website session, Unity builds, dedicated configuration and validation](docs/WEB_BETA.md).
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
