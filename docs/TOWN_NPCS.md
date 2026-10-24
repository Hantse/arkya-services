# PNJ d’Aubefont

La ville reçoit vingt-trois PNJ fondés sur les modèles Paladin et Valkyrie : treize personnages
fixes, six qui se déplacent autour de leur poste et quatre qui suivent une patrouille.
Le serveur ECS possède leurs positions et valide les conversations ; le client
présente les modèles, les animations et le panneau de dialogue. L’exploration locale
et le dedicated utilisent ces mêmes systèmes.

Le build actif utilise **8 / `arkya-npc-vitals-v1`**. Le ghost PNJ `TownNpc.v2`
réplique désormais son niveau, sa santé courante et sa santé maximale. Client
et dedicated doivent être publiés ensemble ; les validations historiques
ci-dessous portent sur leurs builds respectifs.

Les trois nouveaux habitants fixes (IDs 21–23) sont les maîtres bûcheron,
mineur et forgeron. Leurs boutons de service changent l'école de craft active,
distribuent les outils de base une seule fois et enseignent les formules
disponibles sans passer par l'inférence ; voir [l'artisanat](CRAFTING.md).

## Parler en jeu

Un **clic gauche** sur le PNJ ou son nom le sélectionne. À moins de **4 mètres**,
un **clic droit**, **E** ou le bouton **Parler** ouvre la conversation. Le curseur
devient une bulle de dialogue au survol ; les obstacles masquent la sélection.
Son salut écrit s’affiche. Saisir ensuite un message et appuyer
sur **Entrée** ou **Parler** ; **Échap** ou **Fermer** ferme le panneau.

Un PNJ qui parle s’arrête et se tourne vers le personnage. Chaque ouverture,
demande et réponse lui accorde une pause de **45 secondes**, après laquelle il
reprend son comportement. Le panneau suspend les entrées de déplacement et de
caméra du joueur. Le serveur refuse les conversations à travers un obstacle,
hors de portée, pendant la mort ou un transfert d’autorité. Une demande en cours
doit terminer avant la suivante ; le cooldown minimum est d’une seconde.

La question est limitée à **200 caractères et 500 octets UTF-8**. La réponse du
modèle est du texte simple, limitée à **512 unités UTF-16 et 1 000 octets UTF-8**
dans le message réseau. Le serveur coupe seulement entre deux caractères Unicode
complets afin de conserver les accents et les emoji sans dépasser un paquet RPC.
Le client garde un petit
historique de présentation pendant la session ; cet historique n’est pas envoyé
au modèle ni conservé comme mémoire persistante. Si l’inférence est indisponible,
le salut écrit reste dans le panneau et un statut d’indisponibilité permet de réessayer.

## Modifier les PNJ dans Unity

Hors Play Mode, sélectionner cet asset dans la fenêtre Project :

```text
Assets/Arkya/Characters/Npcs/Resources/ArkyaTownNpcs.asset
```

Son Inspector expose `Entries`, une définition par PNJ. Enregistrer l’asset après
modification, puis relancer l’exploration pour repartir des définitions sauvegardées.
Le catalogue est inclus dans les builds du client et du serveur : publier les
deux ensemble après un changement de contenu. Il n’existe pas de rechargement
de catalogue distant ni d’éditeur de PNJ dans le backoffice pour cette tranche.

| Champ | Utilisation |
| --- | --- |
| `Id` | Entier positif unique utilisé par les ghosts et les RPC. Conserver sa valeur pour un PNJ existant. |
| `CraftSchool` | `None` pour un résident, ou `Logger`, `Miner`, `Blacksmith` pour exposer les services de maître artisan. |
| `NpcId` | Identifiant stable en minuscules, chiffres et tirets simples, de 1 à 64 caractères. |
| `DisplayName`, `Role` | Nom et métier affichés au joueur et transmis au modèle. |
| `Level` | Niveau affiché dans le cartouche allié, de 1 à 100 ; les résidents actuels sont niveau 1. |
| `MaxHealth` | Santé maximale initialisée côté serveur, de 1 à 100 000 ; les résidents actuels commencent à 100/100 PV. |
| `Greeting` | Salut écrit immédiatement disponible sans requête d’inférence, jusqu’à 512 caractères. |
| `Personality` | Consigne de personnage rédigée côté contenu : caractère, connaissances et façon de parler, jusqu’à 800 caractères. |
| `InferenceProfileId` | Métadonnée stable de profil ; aucun choix de modèle distinct n’est encore associé à cet ID. |
| `QuestIds` | Liens vers des identifiants de quêtes, au maximum 16 slugs uniques. |
| `Home` | Point de départ en coordonnées monde ; le serveur recale la hauteur sur le Terrain. |
| `FacingDegrees` | Orientation initiale autour de l’axe vertical. |
| `Movement` | `Static`, `LocalWander` ou `Patrol`. |
| `WanderRadius` | Rayon de promenade autour de `Home`, entre 0,5 et 8 mètres. |
| `PatrolPoints` | Suite de 2 à 32 points monde pour `Patrol`, chacun à moins de 100 mètres de `Home`. |
| `WalkSpeed` | Vitesse serveur, entre 0,3 et 2,2 m/s. |
| `WaitSeconds` | Pause entre destinations, de 1 à 30 secondes. |
| `VisualResource` | `ArkyaNpcs/Paladin` ou `ArkyaNpcs/Valkyrie`. |
| `Tint`, `HeightScale`, `ModelYaw` | Teinte de tenue, échelle visuelle et correction d’orientation du modèle. |

`LocalWander` choisit des destinations déterministes dans son rayon. `Patrol`
parcourt les points dans l’ordre puis recommence. Les déplacements suivent le
Terrain, refusent l’eau et les obstacles statiques, puis attendent avant de choisir
la prochaine destination en cas de blocage. Ce comportement ne calcule pas un
chemin autour des bâtiments : poser les points sur des passages ouverts et
vérifier le trajet dans la Game View. Les PNJ ne disposent pas encore d’évitement
mutuel, d’horaires persistants ou de navigation entre étages.

Lors de l’apparition, le serveur cherche un emplacement libre proche du point
prévu si nécessaire. Chaque point appartient à une seule partition. Un PNJ reste
dans cette partition et n’en transfère pas l’autorité. Il n’y a pas encore de
réplication des PNJ d’une partition voisine : près d’une frontière, le joueur voit
les PNJ de son serveur courant, puis ceux du nouveau serveur après son handoff.

Les deux prefabs de présentation sont conservés sous :

```text
Assets/Arkya/Characters/Npcs/Resources/ArkyaNpcs/Paladin.prefab
Assets/Arkya/Characters/Npcs/Resources/ArkyaNpcs/Valkyrie.prefab
```

Les adaptations Arkya portent les matériaux URP et les animations `Idle`/`Walk`.
Les sources fournisseur sont conservées séparément. Le root motion est désactivé :
l’animation suit la position reçue et ne déplace jamais le PNJ. Les changements
de couleur utilisent des propriétés de matériau par instance.

## Quêtes et comportements futurs

Le cartouche affiche le nom, le métier, le niveau et la santé dans un format
compact avec un contour de sélection. Le serveur initialise `Health`,
`MaxHealth` et `Level` depuis son catalogue validé ; le client lit ces données
du ghost. La jauge verte et les valeurs courantes/maximales suivent les mises
à jour serveur, sans état de santé possédé par l'interface. Les résidents
restent non attaquables : aucun RPC de dégâts PNJ n'est ajouté.

Les cartouches à moins de 18 mètres sont disposés par priorité : sélection,
survol, puis proximité. Le placement réserve aussi l'espace des icônes et des
bulles ; il décale verticalement les noms qui se croisent, avec un trait discret
vers leur ancre. Si aucun emplacement lisible ne reste, le cartouche secondaire
est masqué. Le clic sur un nom utilise son rectangle réellement affiché.

Aélis référence les trois objectifs de combat de l'île dans `QuestIds` :
`secure-the-meadow`, `silence-the-revenants` et `repel-the-corrupted-pack`.
Un point d'exclamation doré **!** apparaît au-dessus d'elle tant qu'un de ces objectifs est en cours,
puis une coche **✓** une fois les trois accomplis. Son dialogue affiche les compteurs
du ghost privé du joueur et précise que ces objectifs sont déjà actifs. Le
marqueur identifie le PNJ auquel parler à leur sujet ; son infobulle indique
« Objectifs actifs ». Il ne demande pas d'accepter ces objectifs une seconde
fois. Un identifiant inconnu ne produit pas de marqueur.

Deux résidents proches, immobiles et séparés par aucun obstacle peuvent échanger
de courtes répliques écrites, en alternance dans des bulles. Le calendrier de
présentation suit le tick réseau lorsqu'il est disponible. Cette ambiance ne
requiert pas de worker d'inférence, ne crée aucune mémoire et ne modifie aucun
ghost. Le déplacement ou une conversation serveur avec un joueur l'interrompt ;
seule l'orientation visuelle de résidents immobiles est adaptée pendant l'échange.
Les positions et les patrouilles restent celles du serveur.

`QuestIds` prépare le raccordement des PNJ au contenu : les identifiants sont
inclus dans le contexte serveur transmis au modèle.
Ce champ **ne crée pas d’acceptation, de remise ou de récompense de quête**.
Il ne modifie pas non plus la progression existante des objectifs de combat.
Les règles de [contenu des quêtes](QUEST_CONTENT.md) et les systèmes de combat
gardent leur autorité actuelle.

Une réponse telle que « voici votre récompense » reste une phrase sans effet.
Pour ajouter une action de quête ou un nouveau comportement, prévoir une
commande serveur explicite, ses préconditions et ses tests. Le texte du modèle
ne doit jamais être interprété comme une commande de gameplay.

## Raccorder l’inférence

Le parcours est `serveur Unity → Inference API → agent Windows 1.2 → worker local
→ serveur Unity → client`. Les workers disponibles utilisent leur configuration
de modèle actuelle dans Admin. Le contenu envoyé comprend `NpcId`, `DisplayName`,
`Role`, `Personality`, le message joueur et les liens de quêtes ;
`InferenceProfileId` n’opère pas de sélection de modèle. L’identité de compte et
l’historique du joueur ne sont pas envoyés.

Configurer l’environnement du **dedicated**, ou celui de l’**Editor** pour un
essai local utilisant réellement un worker :

```text
ARKYA_INFERENCE_API_URL=<origine du service Inference>
ARKYA_INFERENCE_SERVICE_KEY=<secret de service dédié>
```

Ces variables sont lues à la création du monde serveur. Ne placer aucune clé
dans le catalogue, un prefab ou le client distribué. L’origine doit utiliser
HTTPS ; HTTP est limité au loopback et à l’origine interne explicitement autorisée
`arkya-inference.arkya-beta.svc.cluster.local:8080`. Aucun chemin, paramètre de
requête ou fragment n’est accepté dans l’origine.

Côté Inference, activer `NpcDialogue__Enabled=true` et fournir le même secret
via `NpcDialogue__ServiceKey`. Dans le profil GitOps, Inference et Realms utilisent
plutôt `NpcDialogue__KeySeed`, référencé depuis le Secret existant de l'Admin :
une dérivation HMAC dédiée produit leur clé commune sans réutiliser une clé
opérateur ou d'admission. Seule la clé dérivée rejoint le Secret du dedicated ;
la graine reste dans les services. Les clés explicites restent acceptées pour
les autres installations.

Après activation du composant et promotion des images, un realm déjà déployé
exige un **Restart** explicite dans l'Admin pour ajouter les deux variables
d'inférence manquantes. Cette adaptation est refusée dès qu'une des variables
existe déjà ou qu'un autre champ du Deployment diffère. La réconciliation
automatique conserve son contrôle de dérive. Attendre le retour Ready, puis
vérifier la conversation dans le client.

Un agent Windows 1.2 inscrit et un worker disponible
sont nécessaires. Le [guide des agents d’inférence](INFERENCE_AGENTS.md#dialogues-pnj-du-jeu)
décrit l’authentification, les routes, les limites et la configuration du
[composant de déploiement facultatif](../backend/deploy/gitops/npc-dialogue/README.md).

Unity borne à huit les demandes HTTP simultanées, attend au plus 40 secondes
le service et expire une conversation en attente après 45 secondes côté simulation.
Les réponses périmées, celles d’une autre connexion et celles reçues après sortie
de portée ne rouvrent pas la conversation. Cette intégration ne conserve aucune
mémoire persistante de PNJ et la file du service reste temporaire.

## Réseau et fichiers

La paire d’admission courante est **`8 / arkya-npc-vitals-v1`**. Les services
reconnaissent encore les anciennes paires exactes, mais n’admettent pas un client
7 sur un serveur 8 ou l’inverse. Les trois nouveaux champs de vitaux du ghost
justifient cette séparation ; ils ne sont pas envoyés par une intention client.
La publication du client et du dedicated doit être coordonnée. Le snapshot
persistant du personnage conserve son format actuel.

| Fichiers Unity, relatifs à `Unity3D/Arkya/` | Responsabilité |
| --- | --- |
| `Assets/Arkya/Characters/Runtime/Npcs/CharacterNpcCatalog.cs` | Définitions et validation du contenu. |
| `Assets/Arkya/Characters/Runtime/Npcs/CharacterNpcComponents.cs` | Ghosts, données de simulation et messages de conversation. |
| `Assets/Arkya/Characters/Runtime/Npcs/CharacterNpcSystems.cs` | Apparition, déplacement, autorisation des interactions et contrôle des réponses. |
| `Assets/Arkya/Characters/Runtime/Npcs/CharacterNpcClient.cs` | Intentions client et réception des réponses. |
| `Assets/Arkya/Characters/Runtime/Npcs/CharacterNpcInferenceSystem.cs` | Appels HTTP serveur vers Inference. |
| `Assets/Arkya/Characters/Runtime/Npcs/CharacterNpcPresentation.cs` | Modèles, animations, noms et sélection. |
| `Assets/Arkya/Characters/Runtime/UI/CharacterNpcDialoguePanel.cs` | Fenêtre de conversation. |

## Vérification

Les améliorations de présentation du **24 octobre 2026** ajoutent les fixtures
`CharacterNpcPresentationTests` (marqueurs, progrès, cartouche et répliques) et
`CharacterNpcPointerRuntimeTests` (sélection gauche, dialogue droit, obstacle,
blocage UI et portée). Après rétablissement de MCP, la suite ciblée Edit Mode
exécutée dans Unity **6000.6.6f1** réussit **45 tests sur 45**, dont ces deux
fixtures PNJ. Le test de pointeur passe temporairement en Play Mode et vérifie
la présentation avec une caméra, un obstacle et un monde client de test :
le clic gauche sélectionne sans RPC, le droit crée la demande de dialogue,
et la distance ou un obstacle empêchent l'ouverture. Il contrôle la demande
créée côté client, sans connexion à un vrai serveur ni transport réseau.

Cette première réussite précède l'ajout des champs de santé et du placement
anti-chevauchement. Les fixtures ont ensuite été complétées pour contrôler
l'initialisation serveur, la jauge depuis un ghost reçu, la réservation d'espace
des marqueurs et la réplication de santé avec un vrai serveur/client loopback.
La suite élargie a obtenu **28 réussites sur 29** : seul le test réseau PNJ
échouait, car le cache JIT Burst de l'Editor exécutait encore les anciennes
fonctions natives du sérialiseur, malgré une assembly et des métadonnées à jour.
Les snapshots reçus contenaient donc l'identifiant et la position, mais pas
les nouveaux champs de santé. L'invalidation des assemblies et la recompilation
via l'API Burst installée ont corrigé cet état local, sans changer le gameplay
ni désactiver les assertions. Burst a ensuite été réactivé.

La nouvelle exécution ciblée du test
`LoopbackReplicatesNpcVitalsMotionAndValidatesDialogueRangeBeforeReturningServerText`
réussit **1 test sur 1**, avec Burst actif, en **8,529 secondes**
(job MCP `1421a7fafce24984a03b4b3172f5d177`). Elle vérifie l'initialisation serveur
au niveau 6 avec 240/240 PV, leur réception côté client, puis la réplication
d'une santé ramenée à 137/240 côté serveur et son affichage par le cartouche.
Le parcours couvre aussi le mouvement, les RPC de dialogue et le refus hors
de portée. Ce résultat porte sur un vrai serveur et un client locaux en
loopback ; il ne valide pas un dedicated déployé. Les résultats locaux sont
conservés dans `.local/urgent-2026-10-24/expanded-job-result.json` et
`npc-network-burst-final.json`. Ces exécutions restent distinctes des 45 tests
initiaux et des preuves historiques ci-dessous.

Les objectifs restent autoactivés : le marqueur **!** les signale sans ajouter
d'acceptation ou de récompense.
Les preuves historiques suivantes décrivent la révision antérieure.

Le **24 octobre 2026**, après réouverture par l’utilisateur, Unity **6000.6.6f1**
a compilé les scripts et exécuté les six fixtures PNJ via MCP : **39 réussites,
aucun échec ni test ignoré**, en 15,824 secondes. `CharacterNpcTests` vérifie les
règles et l’autorité, `CharacterNpcNetworkTests` les ghosts et RPC entre un vrai
serveur et un client locaux, `CharacterNpcAssetTests` le catalogue et les prefabs,
`CharacterNpcDialoguePanelTests` et `CharacterNpcDialogueRuntimeTests` l’interface,
et `CharacterNpcInferenceTests` le contrat HTTP. L’assertion de saisie a été placée
dans la fixture avec `UIDocument` attaché : un champ détaché ne distribue pas ses
événements de changement. La saisie, l’envoi unique par Entrée, l’attente, le focus
et la nouvelle tentative après erreur sont couverts.

L’exploration de `StarterIsland` a ensuite été exécutée en Play Mode avec un
serveur et un client locaux : **20 ghosts de chaque côté et 20 présentations**.
Sur deux relevés séparés de **60,551 secondes**, les dix statiques sont restés
exactement fixes et les dix mobiles ont tous changé de position. Les six petits
déplacements sont restés dans leur rayon aux deux relevés ; les quatre patrouilles
ont avancé. L’écart serveur/client maximal observé est de **0,235 m** en mouvement
avec interpolation, contre moins de **0,001 m** pour les statiques. Tessa, PNJ 15,
est restée immobile en état de conversation pendant les deux relevés espacés de
17,85 secondes, puis a parcouru 3,17 m après expiration de sa pause.
Ces échantillons ne prouvent pas le franchissement de chaque segment à chaque tour.

Le dialogue d’Aélis a été validé dans cette même session avec l’API ASP.NET,
MongoDB temporaire réel, l’agent authentifié `RemoteDialogues` et le modèle
**LFM2.5-1.2B-Instruct Q4_K_M**, sur CPU avec quatre threads. La question envoyée
par Entrée dans le champ de l’interface a suivi le trajet **client RPC → serveur
Unity → API → worker → serveur → client RPC → interface**. La requête
`9d05ae879f6b4fa2ba733f25ff386126` a produit en **9,190 secondes de calcul modèle**
une réponse conforme au rôle de capitaine du guet, effectivement affichée dans
le panneau. Échap a ensuite fermé le dialogue. Les captures Game View ont été
inspectées dans `Unity3D/Arkya/Captures/TownNpcs/`, notamment
`Town-NPC-Aelis-model-dialogue.png` et `Town-NPC-patrol-resumed.png`.

Pour cet essai piloté par MCP, le personnage a été placé près des PNJ côté serveur
et l’ouverture a appelé la présentation, en neutralisant le contrôle de focus OS
durant cet appel seulement : Codex avait le focus. La saisie et Échap ont utilisé
les événements du panneau attaché. Cela ne remplace pas un essai manuel du raccourci
physique E. Les preuves locales non sensibles sont conservées sous
`artifacts/npc-dialogue-validation/live-125da01b/` : résultats des tests,
`movement-summary.json`, relevés de patrouille, `unity-dialogue.json` et événements
du worker. Après l’essai, l’Editor a retrouvé `CharacterCreation` propre, hors
Play Mode, et ses variables d’environnement initiales. Aucun service déployé ni
agent installé n’a été modifié ; l’activation distante reste à configurer.

La console conserve les diagnostics de l’essai : un refus de chargement lors
d’une première invocation directe du parcours et des retards Netcode
`Server Tick Batching`. L’entrée via création d’un nouveau personnage a ensuite
réussi. Aucune conclusion de performance n’est tirée de cette session Editor
instrumentée. Le précédent crash de rendu caméra `D3D12Fence::Wait` ne s’est pas
reproduit avec les captures normales de la Game View.

La première CI avait échoué avant Unity à cause d’un validateur de publication
resté sur le protocole 3. Le commit Unity `244520f` aligne les nouveaux artefacts
sur **5 / arkya-town-npcs-v1** et autorise uniquement la lecture de l’ancien
overlay lors de sa migration. Ses **78 tests Python** passent localement ; dans
le [run CI](https://github.com/Hantse/arkya-mmorpg/actions/runs/40075935239),
77 passent et un cas Windows est ignoré sous Linux. Le contrôle de confiance CI
réussit ; les résultats Unity distants restent distincts des 39 réussites locales.
Le correctif de fixture est publié dans le commit Unity `8b43904`.

Les vérifications déjà exécutées dans le dépôt lors de l’ajout du protocole
comptent **37 tests backend** de compatibilité/admission et **3 tests Python**
du fixture CI de topologie, tous réussis. Les résultats du service d’inférence
et de l’agent Windows sont suivis dans leur guide dédié.
