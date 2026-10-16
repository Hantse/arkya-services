# Validation du prototype d'areas

Rapport du 15 octobre 2026, Unity `6000.6.6f1`. Il porte sur une preuve locale entre deux areas d'un même univers de test. Le [guide du prototype](AREA_PROTOTYPE.md) décrit la construction, le lancement et les limites ; ce rapport ne conclut pas à une disponibilité en production.

**Verdict actuel : PASS.** Le scénario final avec deux Dedicated Servers et deux clients graphiques a réussi ses 54 contrôles. Les captures ont été inspectées ; les tentatives précédentes restent documentées ci-dessous.

## Éléments vérifiés

| Vérification | Résultat constaté | Preuve |
| --- | --- | --- |
| Tests Unity Edit Mode actuels | **56/56 réussis**, 0 échec, 0 ignoré | [Résultats XML actuels](../artifacts/area-prototype/editmode-current-results.xml), exécution à `2026-10-15T20:04:44Z` |
| Build client et Dedicated Server | **Réussis**, 0 erreur et 0 avertissement pour chaque rôle | [Agrégat du build](../artifacts/area-prototype/build-response.json), fin à `2026-10-15T20:26:04Z` |
| Deux serveurs et deux clients sans rendu | **54 contrôles réussis**, 0 échec lors de la dernière exécution | [Vérification du run récent](../artifacts/area-prototype/runs/20261015-221857-4c51ec5b/verification.json) |
| Deux clients graphiques et captures | **54/54 contrôles réussis**, captures inspectées | [Vérification graphique finale](../artifacts/area-prototype/runs/20261015-222947-7a06f200/verification.json) |

Les 56 tests ont été exécutés dans le projet Unity isolé `artifacts/area-prototype/protocol-tests-project/`, sur l'assembly `Arkya.AreaPrototype.Protocol.Tests` : 22 tests de géométrie/protocole d'area, 4 d'autorité, 5 de jetons de transfert et 25 de règles de quête. Ils ne remplacent pas une vérification des transports, du handoff réel ou des dégâts entre processus. La première exécution de 31 tests à `2026-10-15T18:25:57Z` reste conservée dans [son XML historique](../artifacts/area-prototype/editmode-results.xml).

Le build vérifié utilise `packageScope = "prototype"`, avec les dépendances requises par cette scène dans deux copies isolées. Il ne constitue pas une validation du manifest complet du projet artistique. Les réponses [client](../artifacts/area-prototype/client-build-response.json) et [serveur](../artifacts/area-prototype/server-build-response.json) ne contiennent aucune erreur de compilation.

## Validation graphique finale

Le run `7a06f200-23b6-40e6-b93b-672574f17f46` utilise deux vrais binaires Dedicated Server et deux clients visibles Direct3D11, sur loopback : scénario automatique de 45 secondes, délai interserveur de 50 ms, messages d'impact dupliqués et avance de démarrage commune de 20 secondes. Le [manifeste des processus](../artifacts/area-prototype/runs/20261015-222947-7a06f200/run.json) enregistre les binaires et ces paramètres.

| Résultat final | Mesure |
| --- | --- |
| Contrôles réussis / échoués | **54 / 0** |
| Transferts terminés | **7** |
| Impacts appliqués / interzones | **23 / 2** |
| Doublons ignorés / impacts rejetés | **10 / 0** |
| Codes de sortie des quatre processus | **0, 0, 0, 0** |
| Erreurs runtime détectées dans les quatre journaux | **0** |

| Observation client | Client 1 | Client 2 |
| --- | --- | --- |
| Areas connectées | 2 | 2 |
| Frames d'observation | 3 595 | 3 596 |
| Scènes chargées | 1 | 1 |
| Transferts observés joueur / monstre / projectile | 2 / 2 / 1 | 2 / 2 / 1 |
| Doublons de présentation | 0 | 0 |
| Intervalle maximal entre snapshots de simulation | **0.4005 s** | **0.4667 s** |
| Attente initiale avant simulation | 14.7516 s | 14.7483 s |

Sources : [vérification](../artifacts/area-prototype/runs/20261015-222947-7a06f200/verification.json), [client 1](../artifacts/area-prototype/runs/20261015-222947-7a06f200/client-1.json), [client 2](../artifacts/area-prototype/runs/20261015-222947-7a06f200/client-2.json) et [codes de sortie](../artifacts/area-prototype/runs/20261015-222947-7a06f200/process-exits.json).

L'attente initiale est volontaire avant le départ commun. Elle est mesurée séparément dans `InitialSimulationWaitSeconds` et exclue de `MaxPresentationGapSeconds`, qui commence après la première snapshot de simulation. Le seuil du contrôle reste d'une seconde. Cet intervalle de snapshots ne garantit pas l'absence de tout défaut visuel ou de toute variation de cadence GPU.

Les [rapports S1](../artifacts/area-prototype/runs/20261015-222947-7a06f200/server-0.json) et [S2](../artifacts/area-prototype/runs/20261015-222947-7a06f200/server-1.json) donnent les états autoritaires finaux suivants :

| Acteur | Propriétaire | Époque | Position x | Points de vie | Impacts uniques |
| --- | --- | --- | --- | --- | --- |
| Joueur 1 | S1 | 3 | 479.976 | 30 | 7 |
| Monstre 100 | S1 | 3 | 482.473 | 20 | 8 |
| Joueur 2 | S2 | 1 | 532.866 | 20 | 8 |

Les 23 impacts sont ainsi conservés dans les registres. L'époque `3` et le retour sous S1 du joueur 1 et du monstre attestent leur aller-retour.

La [capture du client 1 au passage](../artifacts/area-prototype/runs/20261015-222947-7a06f200/client-1-crossing.png) montre son personnage sous S2 à l'époque `2`. La [capture du client 2 pendant le retour](../artifacts/area-prototype/runs/20261015-222947-7a06f200/client-2-return.png) montre le rendu vers la seconde 28, avant le franchissement effectif vers la seconde 30 : elle ne prouve pas à elle seule le retour sous S1, établi par les rapports finaux.

## Première exécution réseau constatée

Run `07723cd7-8b57-4b01-b99a-8c468b42de18`, scénario automatique de 45 secondes sur loopback, avec 50 ms de délai interserveur et duplication des messages d'impact. Le [manifeste des processus](../artifacts/area-prototype/runs/20261015-220100-07723cd7/run.json) confirme deux processus utilisant `Arkya.AreaPrototype.Server.exe` et deux utilisant le binaire client, sans rendu. Il ne s'agit donc pas du mode de secours qui lance le player comme serveur.

| Mesure | Résultat |
| --- | --- |
| Transferts terminés | **7**, avec le même total en entrée et en sortie |
| Impacts appliqués | **21**, dont **1 interzone** |
| Doublons d'impact ignorés | **5** |
| Impacts rejetés | **0** |
| Transferts et impacts encore en attente | **0** sur chaque serveur |
| Codes de sortie | **0 pour les quatre processus** |
| Erreurs runtime détectées dans les journaux | **0** sur les quatre processus |

Les [rapports du serveur S1](../artifacts/area-prototype/runs/20261015-220100-07723cd7/server-0.json) et [du serveur S2](../artifacts/area-prototype/runs/20261015-220100-07723cd7/server-1.json) montrent le joueur 1 et le monstre revenus sous S1, à l'époque `3` après l'aller-retour. Les points de vie finaux correspondent aux registres uniques : joueur 1 à `40` pour 6 impacts, monstre à `20` pour 8 impacts, joueur 2 à `30` pour 7 impacts. Chaque acteur possède un seul propriétaire autoritaire final ; les copies observées sur l'autre serveur restent des répliques.

| Observation client | Client 1 | Client 2 |
| --- | --- | --- |
| Areas connectées | 2 | 2 |
| Frames d'observation | 2 575 | 2 575 |
| Scènes chargées | 1 | 1 |
| Transferts observés joueur / monstre / projectile | 2 / 2 / 1 | 2 / 2 / 1 |
| Doublons de présentation | 0 | 0 |
| Intervalle maximal entre mises à jour | 0.4013 s | 0.4671 s |

Sources : [client 1](../artifacts/area-prototype/runs/20261015-220100-07723cd7/client-1.json), [client 2](../artifacts/area-prototype/runs/20261015-220100-07723cd7/client-2.json), [codes de sortie](../artifacts/area-prototype/runs/20261015-220100-07723cd7/process-exits.json). Ces mesures portent sur les ghosts et les observations, pas sur une fluidité GPU ; ce run est sans rendu. Le scénario de quêtes n'y est pas activé (`QuestScenario = false`).

## Réexécution et rendu graphique

La réexécution sans rendu `4c51ec5b-d664-4969-a324-03f1f5318de4` a de nouveau passé **54/54 contrôles** après recompilation des shaders : **7 transferts**, **23 impacts**, **2 impacts interzones**, **10 doublons ignorés**, **0 impact rejeté**. Les deux clients gardent une scène et aucun doublon visuel ; leurs intervalles maximaux sont de **0.4016 s** et **0.4682 s**. Voir [son résultat complet](../artifacts/area-prototype/runs/20261015-221857-4c51ec5b/verification.json).

Le run graphique `26be7030-60c0-490c-8582-b7726583176e`, avec deux clients Direct3D11, a produit des images lisibles. La capture ci-dessous montre le joueur 1 à `x = 534.5`, propriétaire S2, époque `2`, et le monstre sous S2. La carte, la frontière, les capsules et les barres de vie sont visibles.

![Client 1 après passage sous autorité S2](../artifacts/area-prototype/runs/20261015-221951-26be7030/client-1-crossing.png)

Cette première tentative a passé **52/54 contrôles** et reste historiquement **non validée**. Les deux échecs concernent l'intervalle maximal, enregistré à **2.6865 s** et **2.7594 s** : la mesure incluait l'attente des ghosts au tick initial `0` avant le départ synchronisé de la simulation. Le run final ci-dessus confirme la correction du départ de mesure, sans changer le seuil d'une seconde. Les [rapports de cette tentative](../artifacts/area-prototype/runs/20261015-221951-26be7030/verification.json) sont conservés.

## Reproduire

Depuis la racine du dépôt, PowerShell 7, après préparation du projet isolé de tests :

```powershell
& 'D:/Unity/6000.6.6f1/Editor/Unity.exe' -batchmode -nographics `
    -projectPath (Join-Path (Get-Location) 'artifacts/area-prototype/protocol-tests-project') `
    -runTests -testPlatform EditMode `
    -testResults (Join-Path (Get-Location) 'artifacts/area-prototype/editmode-current-results.xml') `
    -logFile (Join-Path (Get-Location) 'artifacts/area-prototype/unity-tests-current.log')
```

Construire puis lancer les vérifications réseau :

```powershell
& ./scripts/area-prototype/Build-AreaPrototype.ps1
& ./scripts/area-prototype/Test-AreaPrototype.ps1
& ./scripts/area-prototype/Test-AreaPrototype.ps1 -VisibleClients
```

`Build-AreaPrototype.ps1 -UnityPath <path>` permet d'utiliser un autre emplacement de l'éditeur. Le script construit client puis Dedicated Server dans deux processus Unity distincts ; leurs réponses sont conservées dans `client-build-response.json`, `server-build-response.json` et l'agrégat `build-response.json`.

Le vérificateur contrôle les propriétaires finaux, les transferts, les registres de dégâts, l'absence d'impacts rejetés, les observations des deux clients, les codes de sortie et les erreurs runtime. Les résultats ci-dessus distinguent les runs sans rendu et graphiques ; les captures du run final ont aussi été inspectées.

## Compatibilité du lanceur Windows PowerShell

Le lancement manuel a révélé une régression sous Windows PowerShell 5.1 : `Start-Process -Environment` n'y existe pas. Le lanceur crée maintenant les processus avec `ProcessStartInfo`, en filtrant l'environnement des seuls clients sans modifier celui du terminal.

Une nouvelle exécution réelle sous `powershell.exe` **5.1.26100.9444**, avec deux Dedicated Servers et deux clients graphiques, a passé **54/54 contrôles** : [run `95e36f38`](../artifacts/area-prototype/runs/20261015-224519-95e36f38/verification.json). Les quatre processus sont sortis avec le code 0 et aucune erreur runtime n'a été détectée. Cette vérification porte sur le lanceur corrigé et les binaires déjà construits ; aucun changement Unity n'était nécessaire.

Un [test ciblé du lanceur sous 5.1](../artifacts/area-prototype/launcher-compatibility/windows-powershell-51-result.json) a aussi passé 78 contrôles : arguments et chemins contenant des espaces, filtrage des quatre variables sensibles dans les clients, conservation de l'environnement parent et arrêt des processus déjà lancés après une erreur au troisième worker ou lors de l'écriture de `run.json`.

## Limites de la preuve

Les tests de protocole, les builds et le scénario de quatre processus locaux, avec et sans rendu, sont vérifiés. Les captures ont été inspectées et les intervalles de snapshots restent sous le seuil contrôlé. Cette preuve ne démontre pas la tenue en charge, la reprise après crash ou la tolérance à une partition réseau. Le rattrapage et l'extrapolation sont bornés ; les registres et états d'autorité restent en mémoire.

Le service Realm, les personnages persistants liés à un realm, l'admission, l'intégration Discord/Characters et les sauvegardes MongoDB sont hors de cette preuve locale.
