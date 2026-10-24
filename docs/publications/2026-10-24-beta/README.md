# Publications bêta — 24 octobre 2026

[articles.json](articles.json) contient cinq sujets, chacun en français et en
anglais : une actualité d'inscription et quatre notes de version. Le tableau
`articles` utilise les arguments de `create_publication`. Les corps sont du
texte brut ; aucun HTML ni rendu Markdown n'est requis. Les identifiants,
révisions et dates de publication doivent venir du service Content.

La news et les liens d'inscription doivent être publiés après disponibilité de
`https://arkya.online/beta.html` et des API associées. Ce dossier ne publie rien
automatiquement et ne crée aucun candidat ou droit bêta.

## Publication vérifiée

Les dix articles ont été publiés par le MCP Admin et leurs snapshots anonymes
ont été comparés aux textes de `articles.json`. Le
[compte rendu MCP](publication-receipts.json) conserve les IDs, révisions et
dates retournés par Content. Les articles de développement portent
`dev.20261024` et restent explicitement distincts des versions distribuées.

La news d'inscription a été publiée après vérification de la livraison du
24 octobre : page `beta.html` en HTTP 200, API sans session en HTTP 401,
préflight CORS en HTTP 204 pour `https://arkya.online`, et page Admin redirigée
vers sa connexion. La [CI de cette livraison](https://github.com/Hantse/arkya-services/actions/runs/40087241218)
a construit les dix images et promu leurs digests. Aucun candidat ni accès
réel n'a été créé par les tests de cette mission.

## Périmètre et versions

| Sujet | `gameVersion` | Portée |
| --- | --- | --- |
| Inscription bêta | `null` | News, inscription distincte de l'attribution d'accès |
| Rétrospective web → Windows | `0.1.0-beta.20261023.3` | Client Windows proposé dans le manifeste public au moment de la rédaction |
| Launcher | `0.4.1` | Release GitHub publiée et manifeste canonique du site vérifié avant publication de la note |
| Développement jeu des 23–24 octobre | `dev.20261024` | Repère éditorial explicitement présenté comme développement, pas une version téléchargeable |
| Services et outils | `dev.20261024` | Bilan de réalisation ; activation/déploiement propres à chaque service et royaume |

Les manifestes HTTPS ont été relus publiquement le 24 octobre 2026 avec
`Invoke-WebRequest`, sans authentification :

- [Windows Bêta](https://arkya.online/downloads/windows/latest-win-x64.json) :
  `0.1.0-beta.20261023.3`, session launcher protocole 1,
  SHA-256 `64fa73c12d4bf9d1ec6907660336da65dee786ca3e04be38f8bc29403aa96366`.
- [Launcher Windows](https://arkya.online/downloads/launcher/latest-win-x64.json) :
  `0.4.1`, SHA-256
  `a0f724f03ea5eb600d87301c688e588f481339ddf1719b64208e64713d47e0c2`.

Ces valeurs correspondent aux fichiers de distribution versionnés sous
`sites/arkya/dist/downloads/` après la release launcher décrite ci-dessous.
La lecture vérifie les métadonnées disponibles,
pas une nouvelle installation ou admission au royaume public.

### Actualisation launcher 0.4.1

Le commit services
[`e3c5223`](https://github.com/Hantse/arkya-services/commit/e3c52230ef61e7e1e3ccbc86ec301502513a9caf)
met le manifeste versionné et la page Jouer à `0.4.1`. La
[release publique `launcher-v0.4.1`](https://github.com/Hantse/arkya-services/releases/tag/launcher-v0.4.1)
a été créée le 24 octobre à **12:37:28, heure de Bruxelles**, avant la
promotion du site. La lecture publique de l'API GitHub et des fichiers joints
confirme l'installateur, le ZIP portable, le manifeste et la provenance :

- Source `f56dc210abb166660cfdb977f400b39afe54a434`, qui comprend le correctif
  de fenêtre `131ac08`.
- Setup de **47 242 136 octets**, SHA-256
  `a0f724f03ea5eb600d87301c688e588f481339ddf1719b64208e64713d47e0c2`.
- Provenance : `ManifestMatchesExactSetup=true`, **184 tests Core et 14 WPF**.
  Ces résultats sont ceux de la release contrôlée, pas une nouvelle exécution
  pendant la rédaction de ces articles.

Le manifeste canonique
`arkya.online/downloads/launcher/latest-win-x64.json` a ensuite été vérifié à
`0.4.1`, avant publication des deux notes du launcher. La lecture de ce manifeste
confirme sa distribution publique ; elle ne remplace pas une nouvelle
installation du launcher pendant cette mission.

Seules les deux notes existantes du launcher sont actualisées, sans créer de
doublon : brouillon FR `763b187c…` → **Launcher Windows 0.4.1 : boutons visibles
et mises à jour** ; brouillon EN `15217ab3…` → **Windows launcher 0.4.1: visible
actions and updates**. Le compte rendu MCP conserve leurs identifiants complets.

## Première version publiée : preuves retenues

Les sources distinguent trois événements ; aucune date unique de déploiement
ne peut être déduite du premier commit ou de la première image construite.

1. [WEB_BETA.md](../../WEB_BETA.md) conserve une première publication manuelle
   du client web, version 6, sur
   `arkya-atlas.easykrill33.chatgpt.site/play.html`, contrôlée le 18 octobre.
   Elle reste distincte de la livraison sur le domaine `arkya.online`.
2. Le commit services
   [`842a977`](https://github.com/Hantse/arkya-services/commit/842a97766e53d6357b5b54fa17da94dceb0108be),
   daté du 18 octobre, vérifie les images et prépare le premier sync Argo ;
   son texte indique explicitement qu'aucun sync de cluster n'a été effectué
   dans cette session. Il ne prouve donc pas un déploiement joueur ce jour-là.
3. La première vérification publique du client sur `arkya.online` est consignée
   le 21 octobre à **12:22, heure de Bruxelles**, source Unity `3c4cc6e`.
   Les sources `fd77651` puis `ef90ae8` ont été observées le même jour à
   **15:19** puis **18:42**. Voir le
   [journal de livraison WebGL](../../../backend/deploy/gitops/web-client-control/README.md)
   et les commits services
   [`277db67`](https://github.com/Hantse/arkya-services/commit/277db67628220c6f537780cf64b3ebb97adf5765),
   [`f8af7e1`](https://github.com/Hantse/arkya-services/commit/f8af7e1a3c9204874fdc2cf0741fdfcd39462c5f),
   [`d359181`](https://github.com/Hantse/arkya-services/commit/d3591817d54eb60e735c924f67c2a6fa588e2a58).

La rétrospective couvre ces étapes, sans appeler un build CI « déploiement »
et sans confondre l'upgrade WSS 101 avec une admission authentifiée complète.

## Sources par sujet

### Client distribué et launcher

- [WINDOWS_LAUNCHER.md](../../WINDOWS_LAUNCHER.md) : installateur, canaux,
  session protégée, mise à jour du launcher et limites de validation.
- [CHARACTER_CREATION.md](../../CHARACTER_CREATION.md) et
  [CHARACTER_ENTRY.md](../../CHARACTER_ENTRY.md) : Valkyrie, personnalisation,
  choix du royaume, historique d'entrée et session du launcher.
- [Création dans la ville](../../art-direction/character-city-stage-2026-10-23/README.md) :
  décor 3D et revue visuelle, sans remplacer les preuves de build distribué.
- [Environnement](../../world/STARTER_ISLAND_ENVIRONMENT.md),
  [météo](../../world/STARTER_ISLAND_WEATHER.md) et
  [exploration](../../world/STARTER_ISLAND_PLAYABLE.md).
- [BUG_REPORTS.md](../../BUG_REPORTS.md) : F8/F9, capture contrôlée et suivi.
- Commit services
  [`d1697f8`](https://github.com/Hantse/arkya-services/commit/d1697f854905) :
  premier manifeste Windows `0.1.0-beta.20261023`.
- Commit services
  [`878cac8`](https://github.com/Hantse/arkya-services/commit/878cac843503) :
  manifeste `.3`, provenance CI source Unity
  `ffca47da7cb0335e8b61a4cbf17c1df0f4d2c4dd`.
- Commits services `131ac08`, `f56dc21` et `e3c5223` : correctif de fenêtre
  maximisée, version `0.4.1` et publication des artefacts/du manifeste. La note
  décrit désormais ce correctif dans `0.4.1`, avec la distinction de promotion
  du site documentée ci-dessus.

### Développement après le client Windows .3

La source Unity `.3`, `ffca47d`, précède les commits Unity suivants :

| Révision Unity | Réalisation | Documentation et limites |
| --- | --- | --- |
| `daf23e9` | Menu Échap et préférences locales | [PLAYER_SETTINGS.md](../../PLAYER_SETTINGS.md), 15 tests ciblés, rendu natif exporté à vérifier |
| `3790841`, `01768c1`, `12be7b7` | Statistiques/HUD et refonte des cadres | [CHARACTER_STATS_SKILLS.md](../../CHARACTER_STATS_SKILLS.md), HUD et fiche ; les limites d'XP historiques précèdent l'ajout des rencontres |
| `d832afe`, `cff0e92` | Rotation d'aperçu, course, regard libre | [URGENT_REPORTS_2026-10-23.md](../../URGENT_REPORTS_2026-10-23.md) |
| `5b81b87` | Possessions autoritaires sauvegardées | [CHARACTER_POSSESSIONS.md](../../CHARACTER_POSSESSIONS.md), vraies API/WebSocket et MongoDB isolé ; pas d'UI banque/équipement |
| `611faa4`, `c468e88` | Camps, combats et crédits de quête/XP | [STARTER_ISLAND_ENCOUNTERS.md](../../world/STARTER_ISLAND_ENCOUNTERS.md), 27 tests ciblés et revue du vrai parcours local ; pas de loot ni de réplication interpartition |
| `2d4e357`, `b6a2149` | Plongée, saut/nage, racines/foin/herbe | [Registre des dix signalements urgents](../../URGENT_REPORTS_2026-10-23.md), tests et captures ; publication coordonnée protocole 4 nécessaire |
| `5ad7d07`, `244520f`, `8b43904` | Vingt PNJ et dialogue d'inférence | [TOWN_NPCS.md](../../TOWN_NPCS.md), 39 tests MCP, vrais ghosts, mouvements et réponse du modèle local ; protocole 5 et activation distante à livrer |

Les modifications locales non committées du projet Unity au début de cette
mission ont été laissées intactes et ne sont pas annoncées comme terminées.
Les termes `dev.20261024` et « nécessite une nouvelle livraison » empêchent
d'attribuer les ajouts tardifs au binaire `.3`.

### Services et outils

- [AUTH_SESSIONS.md](../../AUTH_SESSIONS.md) : sessions site/admin/launcher.
- [ADMIN_CONSOLE.md](../../ADMIN_CONSOLE.md) : comptes, présence,
  administration, fiches de personnages en lecture seule.
- [REALM_OPERATIONS.md](../../REALM_OPERATIONS.md),
  [REALM_DEPLOYMENT.md](../../REALM_DEPLOYMENT.md) et
  [REALM_SITES.md](../../REALM_SITES.md) : plans, opérations et pages de royaume.
- [STARTER_ISLAND_HANDOFF.md](../../STARTER_ISLAND_HANDOFF.md) et
  [MULTI_AREA_RUNTIME.md](../../MULTI_AREA_RUNTIME.md) : transferts,
  validation multi-processus et limites client natif/public.
- [QUEST_CONTENT.md](../../QUEST_CONTENT.md) : prototype de catalogues distinct
  des nouvelles rencontres ; pas de récompenses implicites.
- [ITEM_CONTENT.md](../../ITEM_CONTENT.md) : bibliothèque, références Unity,
  catalogues, probabilités ; tirage/attribution du loot encore séparés.
- [Collection d'armes](../../art-direction/resources/armes/README.md) :
  21 modèles visuels, trois LOD, sans équipement activé par leur seul import.
- [INFERENCE_AGENTS.md](../../INFERENCE_AGENTS.md) et
  [TOWN_NPCS.md](../../TOWN_NPCS.md) : workers, raccordement et limites.
- [PUBLICATIONS.md](../../PUBLICATIONS.md) : schéma, révisions et opérations MCP.

Les règles détaillées d'accès bêta sont portées par l'implémentation de cette
mission : inscription par compte Discord, consultation du statut et attribution
opérateur manuelle ou aléatoire. La news n'annonce ni date d'invitation, ni
attribution automatique, ni notification par e-mail.

## Vérification éditoriale

Avant publication, contrôler les longueurs et valeurs selon
[PUBLICATIONS.md](../../PUBLICATIONS.md), vérifier la page d'inscription en
ligne, puis rechercher les titres/langues existants pour éviter les doublons.
Créer les brouillons, relire leur révision, puis publier chaque brouillon
enregistré avec `expectedRevision`. Conserver les IDs retournés dans le compte
rendu de publication ; ne jamais inventer de dates ou d'IDs de Content.
