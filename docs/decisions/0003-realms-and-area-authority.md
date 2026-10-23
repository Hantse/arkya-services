# 0003 — Realms, Areas et partitions d'autorité

## Statut

Décision acceptée, vocabulaire précisé par l'utilisateur le 23 octobre 2026 : une **Area est une carte entière**, comme l'île de départ. Un realm peut en contenir plusieurs. Le mot « area » employé historiquement pour un domaine d'autorité technique désigne désormais une **partition**.

## Contexte

Un MMORPG doit distinguer l'univers auquel un joueur appartient des processus qui exécutent sa simulation. Une carte continue peut demander plusieurs serveurs Unity, sans demander au joueur de changer de personnage ou interrompre le combat à chaque frontière technique.

## Décision

- Un compte Arkya est global et peut posséder des personnages associés à des realms.
- Un personnage appartient à un realm persistant. Sa connexion et son admission doivent vérifier le compte et ce rattachement.
- La hiérarchie est `Realm → Areas → partitions → dedicateds`. Chaque Area possède un identifiant, des dimensions, une origine et un nombre de dedicateds indépendants des autres Areas du realm.
- L'île de départ `starter-island` est une Area. Si elle utilise quatre dedicateds, chaque processus simule une partition de cette même carte. Une autre Area peut avoir son propre découpage et les mêmes coordonnées locales.
- Un passage entre partitions d'une même Area conserve l'identité du personnage, son état de jeu, son Area et son realm. Le changement de propriétaire s'effectue avec une époque d'autorité incrémentée.
- Le combat doit rester possible de part et d'autre d'une frontière de partition dans la même Area. Le propriétaire de la cible valide les impacts et leurs effets uniques ; la présentation observe le résultat.
- Le handoff de partitions ne définit pas un passage entre deux Areas. Leurs entrées, sorties et contenus sont des règles distinctes à fournir.
- Plusieurs copies indépendantes d'une Area ne sont pas plusieurs cartes : la destination logique cible est `(realmId, areaId, instanceId)`, puis sa partition d'autorité. Les pools privés d'instances et de raids restent distincts du découpage des Areas du monde ouvert.
- Un transfert entre realms doit être une opération métier explicite, avec ses propres règles. Il ne découle pas du passage entre deux processus Unity.
- Le service Realms possède le catalogue et le plan de déploiement. Characters valide le compte, le rattachement du personnage, la destination et la lease d'autorité avant l'admission.

## État de l'implémentation

`Unity3D/Arkya/Assets/Arkya/AreaPrototype/` contient une expérience locale avec deux partitions et deux clients sur une même carte. Le nom historique `AreaId` de son protocole désigne ces partitions. Elle utilise des entités, jobs Burst et ghosts Netcode, avec transferts d'autorité, impacts identifiés et registre de déduplication transféré avec la cible. Aucun `realmId` persistant n'est actuellement transmis par ce protocole.

Identity possède les comptes et sessions ; Characters possède les personnages rattachés à leur realm, les admissions et les leases persistantes. `Arkya.Realms.Api` possède les définitions opérateur et les demandes de déploiement k3s dans `arkya_realms`, avec profils figés et suivi des ressources ; voir le [périmètre de déploiement](../REALM_DEPLOYMENT.md).

La liste de cartes est déjà configurable par realm. Les champs historiques `topology.islands`, `islandId`, le type `Island` et les IDs de processus `island-*` sont conservés dans les contrats et snapshots : ils représentent les Areas et leurs partitions. Les renommer ou ajouter des alias sérialisés changerait les empreintes des plans existants ; cette clarification ne constitue pas une migration de protocole.

Le [handoff StarterIsland](../STARTER_ISLAND_HANDOFF.md) a été vérifié avec quatre dedicateds et un client Unity Editor. Il conserve le joueur entre partitions de l'Area de départ. Le runtime charge uniquement cette carte ; configurer d'autres Areas ne fournit pas automatiquement leurs scènes ou leurs règles de transition. Le combat frontalier et les pools privés restent hors de cette tranche.

## Conséquences

Le parcours de connexion reste `Discord → realm → personnage de ce realm → admission → connexion au monde`. Le serveur valide le realm du personnage, les droits du compte et la destination de simulation. Redécouper une Area ou changer son processus propriétaire ne change pas le realm persistant.

Le backoffice et la documentation emploient Area pour la carte et partition pour sa subdivision. Les noms techniques hérités du prototype ou du premier contrat sont documentés explicitement afin d'éviter de confondre ces deux niveaux.

Le prototype valide des mécanismes locaux et observables ; sa carte et ses ports fixes, ses identités de test et ses données en mémoire ne définissent pas encore une orchestration distribuée exploitable.

## Références

- [Architecture Arkya](../ARCHITECTURE.md).
- [Prototype Unity : construire, lancer et vérifier](../AREA_PROTOTYPE.md).
- [Services backend existants](../../backend/README.md).
