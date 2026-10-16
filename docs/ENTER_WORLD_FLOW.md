# Flux minimal Enter World

Ce flux pose le chemin client pour entrer dans le monde sans encore implementer le gameplay, le spawn ou la simulation serveur.

Les nouveaux services [Identity et Characters](../backend/README.md) sont indépendants de ce parcours : l'interface Unity n'y est pas encore branchée. `POST /dev/auth/login` reste réservé à l'environnement `Development` de `Arkya.Backend.Api` et ses jetons ne sont pas acceptés par ces services. La prochaine intégration devra remplacer cette session locale par Discord, sélectionner un personnage et définir l'admission auprès du serveur dédié ; une résolution de personnage seule ne vaut pas admission.

## Chemin actuel

1. Le client Unity demarre sur `Assets/Arkya/Scenes/EnterWorld.unity`.
2. `EnterWorldBootstrap` charge trois configurations separees :
   - `ClientConnectionSettings`: nom de joueur dev, serveur prefere, auto-connexion.
   - `BackendApiSettings`: URL de l'API ASP.NET Core.
   - `GameServerAddressBook`: adresse Netcode locale de secours.
3. Le client appelle `POST /dev/auth/login` sur le backend et recoit une session dev.
4. Le client appelle `GET /servers` et selectionne par defaut le serveur mock `local-dev`.
5. Le bouton `Enter World` lance `NetcodeForEntitiesConnectionLauncher.EnterWorld`.
6. Tant que Netcode for Entities n'est pas installe, le launcher bloque proprement avec la liste des packages manquants.

La prochaine tranche `Spawn` doit se brancher au point 5 : creer ou recuperer le `ClientWorld`, envoyer la requete de connexion Netcode vers `local-dev`, puis declencher le spawn apres acceptation serveur.

## UI Unity

La scene `EnterWorld.unity` construit une UI uGUI au runtime avec `EnterWorldScreen`.

- Les sprites sont resolus depuis `Assets/Synty/InterfaceModernMenus/Sprites/ModernMenus/` via `SyntyModernMenuSkin`.
- Les assets Synty restent isoles dans leur dossier vendor.
- Si les sprites ne sont pas charges en build, la UI garde une apparence de secours par couleurs.

## Configuration locale

Client Unity :

- Backend API: `Assets/Arkya/Config/BackendApiSettings.asset`
  - `http://localhost:5062`
- Client connection: `Assets/Arkya/Config/ClientConnectionSettings.asset`
  - display name: `Local Player`
  - preferred server: `local-dev`
- Game server fallback: `Assets/Arkya/Config/GameServerAddressBook.asset`
  - `local-dev`, `127.0.0.1:7777`

Backend ASP.NET Core :

- `MockServerList:LocalDevHost`
- `MockServerList:LocalDevPort`
- `MockServerList:LocalDevMaxPlayers`

Ces valeurs sont exposees par `GET /servers`; le client garde aussi un address book local pour afficher `local-dev` si la liste echoue apres une session dev valide.

## Packages Unity manquants

Le manifest Unity actuel ne contient pas encore les packages DOTS/Netcode necessaires au vrai client/server bootstrap ECS.

A installer pour brancher la connexion Netcode for ECS :

- `com.unity.entities`
- `com.unity.netcode`
- `com.unity.transport`

Probable package gameplay a ajouter ensuite pour la simulation/spawn :

- `com.unity.physics`

Le code actuel ne reference pas directement ces assemblies pour garder le projet compilable avant installation. Quand les packages seront presents, la prochaine etape devra remplacer le blocage `BlockedMissingBootstrap` par la creation du monde client/serveur et la requete de connexion Netcode.
