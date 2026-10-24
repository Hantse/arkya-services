# Arkya

Arkya is a Unity3D MMORPG project using Unity DOTS (Data-Oriented Technology Stack) for gameplay simulation and Netcode for Entities for server-authoritative multiplayer. Its visual direction is stylized and painted. The approved tree V3 is the visual reference for new environment objects, preserved in the [art direction resources](docs/art-direction/resources/README.md).

The [multi-Area runtime](docs/MULTI_AREA_RUNTIME.md) maps each Area to registered Unity content, distributes its partitions across dedicateds and transfers the authoritative player state through configured automatic portals. The admin interface uses English. Runtime verification and compatibility details are recorded in the guide.

The character base selected on 22 October 2026 is `valkyrie_2019`, imported under `Unity3D/Arkya/Assets/Valkyrie/`; see the [character integration status](docs/CHARACTER_CREATION.md#base-retenue--valkyrie_2019). Adaptations follow the [six visual references selected on 20 October 2026](docs/art-direction/resources/references/personnages-stylises-2026-10-20/README.md) for proportions, sculpted forms, expressive faces, hair, equipment and material finish. Creation and world presentation use the adapted Valkyrie base, with URP materials and six animation states. Historical Human/Elf appearance data remains compatible; new creation offers the supplied feminine human model and long hair.

The targeted Valkyrie integration check on 22 October 2026 reports **67 tests: 66 passed, 0 failed, 1 ignored**. The real-services admission case requires `ARKYA_WEB_BETA_TICKETS_FILE`; see [verification and limits](docs/CHARACTER_CREATION.md#intégration-valkyrie--22-octobre-2026).

## Current Shape

- Active Unity project for art comparison and the area prototype: `Unity3D/Arkya/`, Unity `6000.6.6f1`, URP `17.6.0`; see the [Unity art test guide](docs/art-direction/unity-test/README.md).
- Starter island: editable native terrain with a level 1–6 layout, reachable from character creation for [local playable exploration](docs/world/STARTER_ISLAND_PLAYABLE.md) and the [web beta dedicated flow](docs/WEB_BETA.md), with server-owned movement and an orbit camera. Quests and progression remain separate; [scene and editing guide](docs/world/STARTER_ISLAND.md).
- Island presentation: four-hour day/night cycle, sun, moon and stars, painted ground and local animated/audio ambience; [review controls and validation](docs/world/STARTER_ISLAND_ENVIRONMENT.md).
- Visual quality review: character and familiar animation checks, contact lighting, groundcover, rain and water refinements; [19 October 2026 comparison images, changes and validation](docs/art-direction/quality-2026-10-19/README.md).
- Island weather: gradual cloud cover, rain, wind and decorative thunderstorms; [controls and validation](docs/world/STARTER_ISLAND_WEATHER.md).
- Boundary water: a boiling violet ocean ring marks the island limit, with a five-second server ECS death rule after water contact; [configuration and current integration scope](docs/world/STARTER_ISLAND_BOUNDARY_WATER.md).
- Existing gameplay foundation: `My project/`, Unity `6000.3.10f1`; unchanged by the art comparison integration.
- Rendering: URP package is present.
- Input: Unity Input System package is present.
- Character selection and creation: a native 3D Aubefont courtyard surrounds the Valkyrie preview, with a right-hand character list and a dedicated creation view. UI Toolkit panels edit height and colors while a full-screen camera keeps the character framed between them. Painted buildings, lanterns, moving clouds, dust and quiet ambient audio form the backdrop; see the [city scene review and validation](docs/art-direction/character-city-stage-2026-10-23/README.md). Discord login, Characters/MongoDB persistence and local Netcode exploration remain described in the [character creation guide](docs/CHARACTER_CREATION.md).
- Discord social: the official Social SDK adds friends, presence and social-lobby invitations to native character creation and its local test; [setup, scope and validation](docs/DISCORD_SOCIAL.md). The web client uses the website's Discord-backed Arkya session and separate server admission. Chat/voice UI remains separate work.
- Gameplay simulation: Unity DOTS, using Entities (ECS), the C# Job System, and Burst.
- Gameplay networking target: Netcode for Entities, with server authority, local prediction, and remote entity interpolation. The current area prototype interpolates all received ghosts.
- Inventory and harvesting foundation: server ECS rules for up to 150 slots, carry weight, resource nodes, tools and socketed stones; [rules, Inspector setup and current integration limits](docs/GAMEPLAY_FOUNDATION.md).
- Partition prototype (historically `AreaPrototype`): two server processes and two clients on one continuous map, with entity authority transfers and combat across partition boundaries; see the [build and run guide](docs/AREA_PROTOTYPE.md).
- Backend: separate .NET 10 Identity (Discord accounts/sessions) and Characters services in `backend/`, alongside the existing local development API.
- Public API deployment: a YARP gateway routes `api.arkya.online` to the backend services; [k3s setup and Docker image workflows](backend/deploy/gateway/README.md).
- Realm deployment backoffice: `Arkya.Admin` manages persistent realm definitions through `Arkya.Realms.Api`, which creates and observes dedicated workloads in approved k3s namespaces. A realm contains several Areas, each representing a complete map such as StarterIsland. Creation configures each Area's dimensions and dedicated partitions, plus separate instance and raid capacities; [configuration and runtime limits](docs/REALM_DEPLOYMENT.md). Each partition is one authority with one Kubernetes replica. The protocol 2 runtime adds player handoff between StarterIsland partitions with an atomic persistent lease transfer; [implementation and current validation](docs/STARTER_ISLAND_HANDOFF.md). A real run passed with four dedicated executables and one rendered Unity Editor client: six handoffs, with a measured boundary stall of up to 0.43 seconds. The exported native client remains unvalidated. Instance and raid plans remain undeployable until their runtime and allocator exist.
- Admin console: grouped navigation, live service/realm/player dashboard, account search, bans and beta entitlements, and character presence; [contracts, configuration and k3s integration](docs/ADMIN_CONSOLE.md).
- Backend persistence: MongoDB, with a separate database owned by each service and local Compose setup.
- Quest content: an ASP.NET admin edits kill objectives and publishes immutable catalogs; dedicated partition servers load a pinned version at startup and save character progress through Characters. See the [quest content guide](docs/QUEST_CONTENT.md).
- Town NPCs: twenty Paladin/Valkyrie residents with server-owned movement, proximity dialogue and optional inference workers; edit identities, behavior, personality and quest links in the [town NPC catalog](docs/TOWN_NPCS.md). Quest acceptance and rewards remain separate.
- Item content: the backoffice edits weapons, armor and other items using a gallery of models exported from Unity; immutable publications synchronize back into Unity before builds. See the [item content pipeline](docs/ITEM_CONTENT.md) for setup and validation limits.
- News and release notes: the admin prepares drafts and publishes version-linked notes or news through Content; its existing MCP also exposes publication management with revision checks. The launcher reads their anonymous public feed. See the [publication contracts and guide](docs/PUBLICATIONS.md).
- Windows launcher: installs and updates the native client, signs the player in through Discord PKCE, and verifies the account before launching. An in-memory session reaches the launched game through a Windows pipe restricted to the current user and checked against the game's PID; Unity validates it before opening character selection. The launcher check reports 76 passing tests and a clean build; real Discord OAuth and an exported Unity client have not yet been tested together. See the [launcher flow, transport contract and validation limits](docs/WINDOWS_LAUNCHER.md).
- Server target: authoritative Unity dedicated server communicating with the ASP.NET Core services; initial contracts use HTTPS JSON.
- Realm model: `Realm → Areas → partitions → dedicateds`. StarterIsland is one Area; another Area has its own identifier, content, map bounds and partition count. The web beta implements an authenticated player catalogue, immutable character realm binding and one-use admission tickets backed by character leases. Protocol 2 retains its partition handoff; protocol 3 adds registered map content and automatic portals between Areas while preserving authoritative actor state. Historical JSON fields `islands`/`islandId` retain their names for compatibility. Further map content, cross-border NPC/combat replication and private session allocation remain separate; see the [multi-Area runtime guide](docs/MULTI_AREA_RUNTIME.md).

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

For Unity work, Codex should use the installed **CoplayDev Unity MCP** bridge for live Editor inspection, scene/asset changes and validation. The [Unity MCP workflow](docs/UNITY_MCP.md) describes connection checks, tool selection, tests and fallback behavior; the root and Unity-project `AGENTS.md` files carry these instructions for future chats.

## Useful Documents

- [Sites publics et administration dédiés aux realms](docs/REALM_SITES.md).

- [Sessions renouvelables du site et de l’admin](docs/AUTH_SESSIONS.md).
- [Launcher Windows : connexion Discord, installation, transfert de session et distribution](docs/WINDOWS_LAUNCHER.md).

- [Site vitrine et atlas visuel des directions artistiques](sites/arkya/README.md).
- `AGENTS.md`: working rules for coding agents and collaborators.
- `docs/ARCHITECTURE.md`: target system architecture.
- `docs/ENTER_WORLD_FLOW.md`: current local development flow from dev session to mock game server entry.
- `docs/ROADMAP.md`: first implementation phases.
- `docs/decisions/0001-project-baseline.md`: initial baseline decision.
- [Backend setup and API contracts](backend/README.md).
- [CI coverage, Unity runners and workflow activation](docs/CI.md).
- [Unity MCP: Editor connection, automation and validation](docs/UNITY_MCP.md).
- [In-game character creation, rigs, server persistence and local movement test](docs/CHARACTER_CREATION.md).
- [Menu Échap, options graphiques et son du joueur](docs/PLAYER_SETTINGS.md).
- [Web beta: website session, Unity builds, dedicated configuration and validation](docs/WEB_BETA.md).
- [StarterIsland player handoff: protocol, durable authority and four-process validation](docs/STARTER_ISLAND_HANDOFF.md).
- [Discord Social SDK: friends, presence, social invitations and configuration](docs/DISCORD_SOCIAL.md).
- [Combat, classes, multiclass progression and inherited spells — design](docs/GAMEPLAY_CLASSES.md).
- [Character stats, class progression, skill inheritance and live HUD — first implementation](docs/CHARACTER_STATS_SKILLS.md).
- [Animal soul bonds and wolf/fox totem companions — design and art assets](docs/GAMEPLAY_TOTEMS.md).
- [Backend service boundaries and transport decision](docs/decisions/0002-backend-services.md).
- [Realms, Areas, and partition authority](docs/decisions/0003-realms-and-area-authority.md).
- [Starter island, levels 1–6, and planned copies per realm](docs/world/STARTER_ISLAND.md).
- [Starter island sky, painted ground and ambience](docs/world/STARTER_ISLAND_ENVIRONMENT.md).
- [Starter island boundary water and server exposure rule](docs/world/STARTER_ISLAND_BOUNDARY_WATER.md).
- [Unity area prototype: build, launch, and verification](docs/AREA_PROTOTYPE.md).
- [Quest administration, versioned publication, dedicated loading and verification](docs/QUEST_CONTENT.md).
