# Signalements urgents — 23 octobre 2026

Lot récupéré via Arkya MCP : bugs et suggestions au statut `triaged` et tag
`urgent`. Sept entrées initiales et trois arrivées pendant l’intervention ont
été traitées. Les dix entrées sont résolues en révision **4**, avec leurs tags
conservés et les preuves de validation inscrites dans le MCP.

Les corrections sont committées et poussées. Elles demandent une nouvelle
distribution du jeu ; cette intervention n’a pas promu le serveur de production.
La plongée nécessite un client et un dédié compatibles en **protocole 4**.

## Résultats

| Signalement | Identifiant | Correction vérifiée | Commit Unity |
| --- | --- | --- | --- |
| Barres | `4d10fb39-baa6-422f-83f6-68e5c11eaf63` | PV, mana, endurance, niveau et XP alimentés par les statistiques autoritaires ; restauration du dernier état de la session. | `3790841` |
| Rotation | `7dc105b8-1931-403f-9d27-e0c45cddce78` | Glissement horizontal au clic gauche dans l’aperçu de sélection/création, avec libération du pointeur aux interruptions. | `d832afe` |
| Course par default | `d41c84ed-c11d-4b48-b9d5-e8ac5322dab2` | Course par défaut ; Maj gauche ou droite pour marcher, vitesses serveur conservées. | `cff0e92` |
| Camera | `030453da-e2f2-4f4a-b467-736ae60a62c3` | Inclinaison de −80° à +65°, ciel accessible dans l’île. | `cff0e92` |
| Clique gauche | `ccbfc51c-f1ce-48e0-99eb-684b2e34ac45` | Orbite gauche indépendante de la direction clavier ; clic droit réaligne déplacement et regard. | `cff0e92` |
| Saut | `06766697-74b9-4eb7-b546-ef1c4def5691` | Retour de pose au sol en 75 ms, sans long fondu laissant les pieds flotter ; saut serveur conservé. | `2d4e357` |
| Nage | `a89f1556-5a98-45d5-b5ff-4be0ab637d35` | Ctrl descend, Espace remonte ; profondeur maintenue au relâchement, vitesse 3D bornée ; bassin et cadence de nage corrigés. | `2d4e357` |
| Object | `f5268904-4890-42be-b938-a711ea9ed9c0` | Trois bottes hautes posées sur les corps des bottes inférieures ; scripts de génération corrigés. | `b6a2149` |
| Arbres | `8e0d0d88-4ba7-4e8c-a376-12d866ccfb3d` | Les 756 arbres des bosquets sont recalés selon le dessous réel des racines ; générateur aligné. | `b6a2149` |
| Effet | `3181f7b7-d965-4ad6-ae9d-1ec89c100616` | Herbe humide conservant sa rugosité mate : suppression du brillant ajouté par la pluie, assombrissement humide préservé. | `b6a2149` |

Les précisions utilisateur ont porté sur la pose et la vitesse du saut/de la
nage, puis sur les **touffes d’herbe au sol, par temps de pluie**. Le texte et
les captures des reports ont été utilisés comme indices de reproduction.

## Vérifications

Unity **6000.6.6f1**, URP **17.6.0**, projet `Unity3D/Arkya/`.
Compilation terminée, shader Meadow sans erreur ni variante encore en
compilation, console sans erreur en fin de contrôle. L’Editor a été rendu hors
Play sur `CharacterCreation`, scène propre. Les originaux fournisseur et les
GUID des assets ont été préservés.

| Contrôle | Résultat | Job MCP / preuve |
| --- | --- | --- |
| Traversée, simulation serveur, handoff, animations Valkyrie et HUD | **183/183** | `3a85ddeac22c42ce91873a1fce8d4ab9` |
| Saut/atterrissage/rivage, vrai clavier avec plongée, gameplay réseau | **3/3** | `1b9b906b214e409c9797125462079a30` |
| Caméra | **7/7** | `caaf62271bc54b3e801f49ed7f21d814` |
| Rotation de l’aperçu et course au clavier | **2/2** | `c44ae9cca595418f92590401ffcdb5a7` ; le cas HUD échoué de ce job a ensuite été corrigé |
| HUD réseau après correction de hauteur de l’endurance | **1/1** | `8235bfa6f0bf4f7a917e9261935a237b` |
| Backend compatibilité plongée | **62 Characters + 5 Realms** | Commit services `15cece8` |
| Contrat et promotion réseau Python | **16 réussis** | Paire client protocole 3 / serveur protocole 4 rejetée |
| Géométrie foin/racines et générateurs | Conforme | Audit MCP, relecture après sauvegarde, compilation des outils et revue indépendante |
| Herbe sous pluie | Reflets réduits, témoins inchangés | Captures A/B à caméra, éclairage, vent et humidité identiques |

Les parcours réseau utilisent de vrais mondes serveur et client locaux.
L’admission publique, le déploiement d’un dédié séparé et une nouvelle version
Windows distribuée ne sont pas revendiqués par cette validation.

## Constats mesurés

- **HUD** : contrôlé à 100/100 puis après mutation serveur PV 67, mana 41,
  endurance 29, niveau 3, XP 50/300. La capture tardive affiche 91 PV à cause de
  la régénération. La refonte UI parallèle `01768c1` garde une hauteur
  d’endurance de 7 px ; les cadres suivants appartiennent au chat UI.
- **Caméra** : capture runtime à yaw 129°, direction clavier conservée à 38°,
  pitch −76,8°. Les tests couvrent le calcul d’orbite et les limites.
- **Nage** : retrait de la surélévation du bassin de 16 cm dans le clip,
  `SwimRate` lié à la vitesse 3D observée et à la taille visuelle, compensation
  de hauteur limitée à la présentation. Vitesse totale bornée à 2,2 m/s,
  même en diagonale ou sur un fond montant. Le serveur garde la hauteur entre
  fond et surface moins 1,15 m. Les commandes simultanées s’annulent ; une
  commande périmée est neutralisée après 0,35 s. Le handoff écrit v3 et relit
  v1/v2 avec un axe vertical neutre. Les lecteurs backend v3 figurent dans le
  commit parallèle `8027e29`.
- **Foin** : abaissements de 60,33 mm, 60,10 mm et 55,75 mm dans
  `Quarter0_3.asset` et `Quarter3_27.asset`. Contact final à environ −8 mm ;
  la quatrième botte haute était déjà en contact à −8,6 mm. Les autres
  attributs de sommets et indices sont identiques.
- **Arbres** : seules 756 coordonnées Y de la scène changent. Abaissement moyen
  0,720 m, maximum 2,467 m ; 1 447 contacts inférieurs examinés par arbre.
  Écart résiduel maximal 0,000004 m. X/Z, rotations, échelles, états actifs
  et prefabs conservés. La capture de la pente la plus défavorable montre
  l’appui obtenu ; le générateur utilise le même calcul.
- **Herbe** : les essais de nuit à sec ne reproduisaient pas le défaut.
  L’humidité ajoutait une brillance de 0,312 au matériau pourtant mat à sec.
  Sous pluie et lanterne, la correction réduit la luminance moyenne de la zone
  d’herbe de **17,1 %**, son 95e percentile de **12,3 %** ; les témoins pavés et
  façade sont strictement identiques. La preuve concerne les reflets/liserés
  des brins, sans prétendre mesurer un large halo extérieur. Aucun changement
  de normales, bloom global, particules ou géométrie n’a été conservé.

Les [mesures structurées](world/urgent-reports-2026-10-23/validation.json) et les
captures retenues sont archivées dans `docs/world/urgent-reports-2026-10-23/`.

## Captures de contrôle

- [Barres pleines](world/urgent-reports-2026-10-23/hud-full-bars.png) et
  [valeurs mises à jour](world/urgent-reports-2026-10-23/hud-updated-bars.png).
- [Regard vers le ciel](world/urgent-reports-2026-10-23/camera-sky-free-look.png).
- [Saut en l’air](world/urgent-reports-2026-10-23/jump-airborne.png) et
  [retour au sol](world/urgent-reports-2026-10-23/jump-landed.png).
- [Nage de surface](world/urgent-reports-2026-10-23/swim-surface.png) et
  [plongée](world/urgent-reports-2026-10-23/swim-underwater.png).
- [Foin avant](world/urgent-reports-2026-10-23/hay-before.png) /
  [après](world/urgent-reports-2026-10-23/hay-after.png).
- [Racines avant](world/urgent-reports-2026-10-23/tree-slope-before.png) /
  [après](world/urgent-reports-2026-10-23/tree-slope-after.png).
- [Herbe humide avant](world/urgent-reports-2026-10-23/grass-rain-lantern-before.png) /
  [après compilation](world/urgent-reports-2026-10-23/grass-rain-lantern-after-compiled.png).

## Fichiers principaux

- `Assets/Arkya/Characters/Runtime/Playtest/` : contrôles, orbite et
  simulation autoritaire ; `Runtime/Multiplayer/` : protocole et handoff.
- `CharacterPreviewPresenter.cs`, `ValkyrieAnimationBuilder.cs`,
  `Swim.anim`, `Valkyrie.controller` : présentation des déplacements.
- UI et tests sous `Assets/Arkya/Characters/` : aperçu, statistiques,
  commandes et régressions ciblées.
- `StarterIsland.unity`, les deux meshes de foin et `PaintedMeadow.hlsl` :
  corrections de décor ; cinq fichiers sous `AgentScripts/World/` pour
  conserver ces corrections à la génération.
- Services Characters/Realms et tests : contrat d’admission correspondant au
  protocole 4. Aucun digest de déploiement ni garde de promotion contourné.
- [Création de personnage](CHARACTER_CREATION.md) et
  [île jouable](world/STARTER_ISLAND_PLAYABLE.md) : commandes à jour.

Les commits sont séparés de ceux des chats UI, rencontres, possessions et
launcher. Les modifications étrangères au lot présentes dans les deux
checkouts ont été laissées intactes.
