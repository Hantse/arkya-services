# 0003 — Realms, personnages et autorité entre areas

## Statut

Décision de conception acceptée. Le prototype d'areas existe dans Unity ; le service Realm et le rattachement persistant des personnages restent à implémenter.

## Contexte

Un MMORPG doit distinguer l'univers auquel un joueur appartient des processus qui exécutent sa simulation. Une carte continue peut demander plusieurs serveurs Unity, sans demander au joueur de changer de personnage ou interrompre le combat à chaque frontière technique.

## Décision

- Un compte Arkya est global et peut posséder des personnages associés à des realms.
- Un personnage appartient à un realm persistant. Sa connexion et son admission doivent vérifier le compte et ce rattachement.
- Un realm peut être simulé par plusieurs serveurs d'areas. Une area décrit un domaine d'autorité technique au sein du même univers.
- Un passage entre areas conserve l'identité du personnage, son état de jeu et son realm. Le changement de propriétaire s'effectue avec une époque d'autorité incrémentée.
- Le combat reste possible de part et d'autre d'une frontière d'area dans le même realm. Le propriétaire de la cible valide les impacts et leurs effets uniques ; la présentation observe le résultat.
- Un transfert entre realms doit être une opération métier explicite, avec ses propres règles. Il ne découle pas du passage entre deux processus Unity.
- Prévoir un service ASP.NET Core Realm pour le catalogue des realms et leurs points d'entrée. Définir ses contrats et son lien avec l'admission avant de modifier l'API Characters.

## État de l'implémentation

`Unity3D/Arkya/Assets/Arkya/AreaPrototype/` contient une expérience locale avec deux areas et deux clients. Elle utilise des entités, jobs Burst et ghosts Netcode, avec transferts d'autorité, impacts identifiés et registre de déduplication transféré avec la cible. Aucun `realmId` persistant n'est actuellement transmis par ce protocole.

Identity possède les comptes et sessions ; Characters possède les personnages du compte. Le modèle Characters actuel ne comporte pas encore de realm et son index de nom reste global. Aucun service Realm, catalogue public, sélecteur Unity ni admission liée au realm n'est présent dans cette tranche.

## Conséquences

Le futur parcours de connexion sera `Discord → realm → personnage de ce realm → admission → connexion au monde`. Le serveur devra valider le realm du personnage, les droits du compte et la destination de simulation. Les identifiants d'area et les processus pourront évoluer sans changer le realm persistant.

L'ajout du rattachement demandera un contrat `realmId` explicite, une migration des documents existants et des index cohérents avec les règles retenues. La portée des noms, les transferts administratifs entre realms et les politiques de fermeture d'un realm restent à préciser.

Le prototype valide des mécanismes locaux et observables ; sa carte et ses ports fixes, ses identités de test et ses données en mémoire ne définissent pas encore une orchestration distribuée exploitable.

## Références

- [Architecture Arkya](../ARCHITECTURE.md).
- [Prototype Unity : construire, lancer et vérifier](../AREA_PROTOTYPE.md).
- [Services backend existants](../../backend/README.md).
