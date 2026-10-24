# Rapports urgents du 24 octobre 2026

Périmètre : les **13 rapports**, bugs et suggestions, au statut `triaged` avec
le tag `urgent` au début de l'intervention. Leurs textes et captures 1440 × 810
ont été examinés. Les corrections ont été compilées et testées dans Unity,
avec inspection du jeu et des captures. Les sources sont prêtes pour un
nouveau build ; cette intervention ne déploie pas de release en ligne.

## Changements

| Rapport | Changement dans les sources |
| --- | --- |
| UI — `13dffc49-0e9d-432b-80ba-e257770577b5` | Jauges vie/mana plus hautes, valeurs centrées, cadre propre et espacement endurance/XP/actions ; suppression des sprites de boutons étirés sur les jauges. |
| Options — `2158d7a1-57ed-47f5-a20b-86eb58259d71` | Structure des listes et défilement restaurée dans le thème runtime ; boutons préservés ; Échap ferme d'abord la liste déroulante. |
| UI — `f3d1bb5f-dcb0-45aa-9fcf-c2871264f6e5` | Dimensions flex bornées, contenu défilant et retours à la ligne pour titres/actions. |
| Scroll — `534f7246-c00d-48a6-9bef-38c5fd87f2f7` | Le zoom utilise les pas uniformes d'Input System 1.20 ; conversion /120 uniquement pour le mode natif Windows. Un cran passe de 5,5 m à environ 4,59 m. |
| Click — `73f2bc72-a8e9-44b3-9168-b3675de16f74` | Un clic simple ne verrouille plus le pointeur. La rotation attend un glissement de 5 pixels ; le client natif replace le curseur au point de départ après la rotation. |
| Attack — `11a4b42d-a0f1-48df-a1c8-876c223511e3` | Une demande d'attaque valide côté présentation déclenche un mouvement local de 0,68 s du torse et des bras, superposé à la locomotion. Les dégâts restent serveur. |
| Nom PNJ — `7195b1d0-4c39-4c3e-987e-7a590f0f9849` | Nom/métier, niveau et vraie jauge de santé lus dans le ghost serveur, avec sélection et placement anti-chevauchement. Les PNJ restent non attaquables. |
| Personnage — `afb251da-ecc6-4d78-b44e-527caa8fce8d` | Clic gauche de sélection, clic droit de dialogue, curseur au survol, contrôle UI/obstacle/distance ; E reste disponible. |
| Quest — `64320618-5461-4235-af75-deaf93537b94` | Aélis indique les trois objectifs existants par ! en cours / ✓ accomplis et les détaille dans le dialogue. L'infobulle précise « Objectifs actifs » : ces quêtes sont autoactivées. |
| Interaction — `1f6b4255-d70f-4c8f-826f-d66010b977c4` | PNJ proches, immobiles et visibles entre eux : courtes conversations alternées, bulles et orientation cosmétique, sans mutation du déplacement serveur. |
| Herbe — `8606e55a-728c-4ad4-a93c-39fda3de7486` | Prairie des jardins raccourcie via le masque des brins, racines préservées, pigment olive moins contrasté. |
| Cohérence — `52a93b71-7425-4215-91c3-74b8de7b9d1f` | Émission magique des lanternes et particules conservées le jour, lumière chaude adaptée aux nuages sans augmenter le nombre de lampes. |
| Visuellement — `2e7ba268-e112-4378-84f0-d65dab5982f4` | Remplissage diffus sous l'orage relevé pour rendre façades et silhouettes plus lisibles ; rendu réel inspecté de jour, sous l'orage et de nuit. |

Les détails UI figurent dans [Options joueur](PLAYER_SETTINGS.md), ceux des
PNJ dans [PNJ du bourg](TOWN_NPCS.md), et les mesures de prairie/éclairage
dans le [rapport visuel](art-direction/urgent-visuals-2026-10-24/README.md).

## Vérification effectuée

- Versions installées confirmées : Unity **6000.6.6f1**, URP **17.6.0**,
  Input System **1.20.0**.
- Compilation C# hors Editor avec le Roslyn livré par cet Editor et ses
  assemblies/réponses de compilation : `Arkya.Characters.Runtime`,
  `Arkya.Characters.EditorTests`, `Arkya.World.Environment.Runtime`,
  `Arkya.World.Environment.Ambience`, `Arkya.World.Environment.Tests`.
  Aucun échec ; avertissements de dépréciation dans les assemblies Characters.
  Les sorties de cette vérification sont isolées sous `.local/urgent-2026-10-24/`.
- **5 tests de logique réussis hors moteur**, exécutés depuis l'assembly de tests
  compilée : pas de molette uniforme/natif et bornes du zoom ; quêtes inconnues
  sans marqueur ; conditions d'appariement PNJ ; alternance des répliques ;
  stabilité du sujet à la frontière des cycles décalés.
- Après rétablissement de MCP, **45 tests ciblés sur 45 réussissent dans le
  runner Edit Mode Unity**, incluant `CharacterNpcPresentationTests` et
  `CharacterNpcPointerRuntimeTests`. Cette dernière fixture entre temporairement
  en Play Mode et vérifie sélection, demande de dialogue, portée et obstacle
  avec une caméra et un monde client de test ; elle ne valide pas un échange
  réseau avec un vrai serveur. Les détails de cette preuve PNJ et ses limites
  figurent dans [PNJ du bourg](TOWN_NPCS.md#vérification).
- Analyse des 42 meshes de prairie : 176 587 brins, aucune racine déplacée ni
  sommet sous sa racine après le raccourcissement calculé. Moyenne proche
  47,51 → 23,51 cm. Ce calcul n'est pas une capture du rendu GPU.
- Revue croisée des changements caméra/combat et UI ; contrôle des diffs et
  conservation des modifications présentes avant cette tâche.

## Validation complémentaire dans Unity

À la demande de l'utilisateur, l'Editor lent a été fermé normalement puis
relancé sur le même projet. Le fournisseur Coplay déjà installé a été
réutilisé en transport stdio pour rétablir MCP, sans installer d'autre pont.

- Compilation Editor réussie, console sans erreurs après recompilation.
- Les tests supplémentaires caméra, combat et PNJ couvrent 29 cas : 28 ont
  réussi dans le lot `acd48c6e737645e09a9e44e27b537165`, puis le dernier test
  réseau PNJ a réussi dans `1421a7fafce24984a03b4b3172f5d177`, avec Burst activé.
- La caméra est testée avec de vrais événements Input System : clic gauche et
  droit, seuil de 5 px, glissement, relâchement, Échap et blocage par UI/modal.
  La perte de focus est simulée par le callback Unity ; le déplacement physique
  du curseur Windows n'est pas mesuré.
- Combat : vrai serveur et client loopback, attaque via la présentation de
  production, mouvement du bras Valkyrie puis restitution, cooldown,
  déduplication, dégâts, mort et XP répliqués. En jeu, une attaque a aussi
  déclenché la pose et réduit les PV serveur de 50 à 25.
- PNJ : serveur niveau 6 / 240 PV, changement serveur à 137 PV reçu par le
  client et affiché par la jauge, patrouille interpolée et refus de dialogue
  hors portée. Le premier snapshot complet est attendu explicitement.
- **76 tests backend et 18 tests Python CI réussis** pour le protocole 8,
  l'admission, les incompatibilités, la reconnexion et les transferts.

Un cache JIT Burst obsolète a été diagnostiqué pendant cette validation : les
fonctions natives de sérialisation s'arrêtaient à l'ancien champ `Motion`,
alors que le code géré contenait les nouveaux PV. Le test réseau passait sans
Burst. L'invalidation des assemblies et la recompilation via le compilateur
Burst installé ont restauré le parcours avec Burst. Aucune suppression
manuelle de `Library` n'a été faite ; les préférences de compilation et
d'entrée en Play Mode ont été remises à leurs valeurs initiales.

Les captures sont référencées dans le rapport visuel. Les menus et
le HUD ont été inspectés à 1198 × 584 ; les tests UI couvrent aussi 1440 × 810,
980 × 600 et 640 × 480.

Les captures finales montrent les PV/niveaux, le marqueur de quête et une
conversation ambiante, ainsi que la place à caméra fixe de jour, sous l'orage
et de nuit. La console de cette session conserve des diagnostics Netcode
`Server Tick Batching` : les validations fonctionnelles ne constituent pas
une preuve de performance. L'Editor a retrouvé `CharacterCreation` propre,
hors Play Mode. Aucun changement temporaire de scène n'a été sauvegardé.

## Limites de portée

L'animation d'attaque est une anticipation cosmétique du joueur local ; elle
n'ajoute ni équipement ni réplication de l'animation aux autres joueurs.
Le ghost `TownNpc.v2` et le protocole **8 / `arkya-npc-vitals-v1`** transportent
maintenant les niveaux et PV des PNJ. Les PNJ restent non combattants ; les
quêtes existantes restent autoactivées, sans nouveau parcours d'acceptation
ou de récompense. Voir [Versions de build](BUILD_VERSIONS.md).

Les tests réseau sont locaux dans l'Editor. Les nouveaux exécutables natifs,
WebGL et dedicated distribués restent à reconstruire et à vérifier dans la CI.
La passe visuelle corrige prairie, éclairage et ambiance ; elle ne remplace
pas l'ensemble des modèles ni la direction artistique du projet.
