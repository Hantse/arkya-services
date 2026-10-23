# Utiliser Unity MCP avec Arkya

Codex privilégie le MCP Unity pour observer et modifier l'état réel de l'Editor
dans le périmètre de la tâche demandée. Il poursuit les opérations autorisées
sans demander une confirmation à chaque lecture, modification ou vérification.

## Projet et fournisseur

- Projet actif : `Unity3D/Arkya/`, Unity `6000.6.6f1`.
- Fournisseur installé : `com.coplaydev.unity-mcp` (CoplayDev).
- Outils Codex : `mcp__unityMCP__*` ; serveur de ressources : `unityMCP`.
- Les anciens skills de CLI ou d'autres fournisseurs ne décrivent pas
  nécessairement les outils de cette connexion. Vérifier les schémas exposés.
- Aucun ajout, remplacement ou changement de package n'est nécessaire pour
  utiliser ce fournisseur déjà présent.

## Début de chaque intervention Unity

1. Lire `AGENTS.md`, `README.md`, `docs/ARCHITECTURE.md` et les fichiers proches
   de la tâche. Confirmer le projet depuis son manifeste et `ProjectVersion.txt`.
2. Découvrir les capacités dans `mcpforunity://custom-tools`, puis les sessions
   dans `mcpforunity://instances`.
3. Si plusieurs instances sont disponibles, appeler `set_active_instance` avec
   le `Name@hash` exact retourné par la découverte pour fixer le routage.
   Ne pas conserver un hash, un port ou un identifiant de session dans ce guide.
4. Lire `mcpforunity://project/info` pour confirmer le chemin et la version,
   puis `mcpforunity://editor/state` pour l'état de l'Editor.
5. Vérifier `data.advice.ready_for_tools` et `data.compilation.is_compiling`
   avant les opérations qui nécessitent un Editor disponible.
6. Relever les scènes ouvertes, leur état modifié et le mode Play avant d'agir.

Les ressources se lisent par leur URI exacte, pas par leur nom affiché.
Utiliser le catalogue de ressources pour découvrir une URI inconnue, notamment
pour les objets et composants. Les réponses enveloppent leurs données sous
`data`. Ne pas déduire un format d'URI ou un paramètre absent du schéma courant.

## Choisir les outils

Les noms ci-dessous omettent seulement le préfixe `mcp__unityMCP__`.

| Besoin | Capacités à privilégier |
| --- | --- |
| Scènes et hiérarchie | `manage_scene` : scènes actives/chargées, hiérarchie, chargement et sauvegarde ciblés |
| Objets | `find_gameobjects`, ressources des objets, puis `manage_gameobject` pour les changements |
| Composants | Ressources des composants pour inspecter ; `manage_components` pour modifier les propriétés ou composants |
| Assets et références | `manage_asset` : recherche, informations, import et déplacements ; préserver les GUID et `.meta` |
| Prefabs | `manage_prefabs` pour inspecter, modifier et sauvegarder le contenu ou le stage concerné |
| Diagnostic | `read_console` ; conserver les preuves avant tout effacement de console |
| Import et compilation | `refresh_unity`, puis état Editor et console |
| Tests | `run_tests`, puis `get_test_job` avec l'identifiant retourné |
| Vérification visuelle | `manage_camera` et ses captures Game View/Scene View, selon les actions annoncées |
| API Unity incertaine | `unity_reflect` et `unity_docs` si disponibles dans la session |

Préférer des recherches ciblées et des réponses paginées. Inspecter les valeurs,
références et identifiants réels avant une mutation. Relire ensuite les objets
concernés pour vérifier les valeurs et le câblage obtenus.

## Code et validation

`rg`, la lecture des fichiers et les patches restent adaptés au code C#.
Après modification, déclencher si nécessaire l'import/compilation avec
`refresh_unity`, vérifier que l'Editor est prêt, puis lire `read_console`.
Une validation de texte C# seule ne prouve pas la compilation du projet Unity.

Exécuter les tests Edit Mode ou Play Mode pertinents pour le changement,
avec les filtres disponibles, puis consulter le résultat final du job.
Pour une scène ou un prefab, vérifier aussi les composants et références ;
pour un changement visuel, inspecter une capture si la capacité est disponible.
Rapporter les vérifications effectivement réalisées et leurs limites.

Préserver les scènes et prefabs déjà modifiés par l'utilisateur : ne pas les
fermer, recharger, écraser ou sauvegarder globalement comme effet secondaire.
Sauvegarder les changements de la tâche sur leur cible précise. Respecter
les `.meta`, les assets tiers isolés et les dossiers générés exclus du travail.

## Si la connexion est indisponible

Refaire une découverte et relever l'erreur concrète. Si l'Editor importe ou
recompile, effectuer une attente bornée puis relire son état ; éviter les
boucles d'attente et les répétitions sans nouveau diagnostic.
Continuer les lectures et changements de fichiers indépendants du MCP lorsque
la tâche le permet. Signaler explicitement toute compilation, inspection ou
validation Unity qui n'a pas pu être exécutée. Ne pas lancer un second Editor
sur un projet déjà ouvert et ne pas installer un autre fournisseur par défaut.

## Dernière preuve de connexion

Le 20 octobre 2026, une vérification en lecture seule a confirmé l'accès à
`CharacterCreation` : 10 objets racines, hors Play Mode, aucune compilation en
cours et aucune erreur dans les messages consultés. Des avertissements du SDK
Discord étaient présents. Cette observation ne garantit pas la disponibilité
du MCP lors d'une intervention ultérieure ; refaire les vérifications initiales.
