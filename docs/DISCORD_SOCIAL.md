# Social Discord dans Arkya

La première intégration sociale ajoute les amis, la présence et les invitations à un salon Discord dans la [création de personnage et son essai local](CHARACTER_CREATION.md). Elle utilise le Discord Social SDK côté client et Identity pour les échanges OAuth confidentiels. Le chat texte et la voix n'ont pas encore d'interface dans Arkya.

## Périmètre disponible

| Fonction | Comportement |
| --- | --- |
| Amis | Liste commune des amis Discord et des relations propres au jeu, avec leur état de présence |
| Demandes Arkya | Ajout par nom Discord, acceptation, refus, annulation et retrait d'une relation de jeu |
| Retrait | Le bouton retire uniquement l'amitié Arkya ; il ne supprime pas une amitié Discord |
| Blocage | Blocage et déblocage via Discord, avec une portée globale au compte Discord |
| Présence | Création de personnage ou essai local ; aucun identifiant de personnage, realm ou ticket d'admission publié |
| Invitations | Inviter un ami, ignorer une invitation ou rejoindre son salon social Discord |

Les relations sont possédées par Discord, sans copie de la liste d'amis dans MongoDB. La distinction entre amitié Discord et amitié propre au jeu suit les [opérations de relation du SDK](https://docs.discord.com/developers/discord-social-sdk/development-guides/managing-relationships).

Un salon social ne donne aucun droit d'entrée dans le MMORPG. Cette tranche ne réserve ni personnage, ni place dans un realm, ni serveur de simulation. Le serveur Arkya reste seul responsable du gameplay et de la future admission. Aucun canal de guilde, chat local, chat global, DM ou appel vocal n'est exposé par cette interface.

## Installation et plateformes

Le projet actif est `Unity3D/Arkya/`, sous Unity **6000.6.6f1**. Le package officiel est embarqué dans `Packages/com.discord.partnersdk/`, version **1.10.19337**. Il provient de l'archive Unity fournie par le développeur depuis son dossier Downloads, avec ce SHA-256 :

```text
BF120D00D2BFA71F92B6C3274EF28FC771D10BD1036324E249EF04BDA009A945
```

Le SDK reste séparé du code Arkya. L'assemblage `Arkya.Social.Discord` référence `Discord.Sdk` et la couche `Arkya.Social.Runtime`. Il s'active pour l'éditeur et les players desktop Windows 64 bits, macOS et Linux lorsque cette version de package est présente. La contrainte `!UNITY_SERVER` exclut l'adaptateur des builds serveur dédié. Le support Linux du SDK est expérimental ; la présence de l'adaptateur ne constitue pas une validation de chaque plateforme. Voir l'[installation Unity officielle](https://docs.discord.com/developers/discord-social-sdk/getting-started/using-unity) et la [compatibilité des plateformes](https://docs.discord.com/developers/discord-social-sdk/core-concepts/platform-compatibility).

L'archive complète représente environ 1,2 Go une fois extraite. Les binaires du package utilisent Git LFS, avec des règles dédiées aux extensions `.aar`, `.dylib`, `.dbg`, `.kef` et aux exécutables des frameworks iOS en complément des règles existantes. Les postes de développement et la CI doivent récupérer les objets LFS avant d'ouvrir Unity ; aucun commit ni push n'a été effectué lors de cette intégration.

| Emplacement | Rôle |
| --- | --- |
| `Assets/Arkya/Social/Runtime/` | Contrats, connexion, renouvellement, présence et coordination des salons |
| `Assets/Arkya/Social/Discord/` | Adaptateur vers le SDK natif et enregistrement de la factory au démarrage |
| `Assets/Arkya/Social/Tests/Editor/` | Tests du client social avec dépendances simulées |
| `Assets/Arkya/Characters/Runtime/UI/DiscordSocialPanel.cs` | Panneau UI Toolkit dans la création et l'essai local |
| `backend/src/Arkya.Identity.Api/` | Autorisation sociale confidentielle et contrôle du compte Arkya |

## Configuration du portail Discord

Ces réglages externes restent manuels ; l'intégration n'a modifié aucune application ni aucun secret du portail.

1. Utiliser la même application Discord que celle configurée dans Identity et activer **Discord Social SDK → Getting Started** dans le [Developer Portal](https://discord.com/developers/applications).
2. Garder **Public Client désactivé** : Arkya échange les codes et renouvelle les jetons côté backend, selon le [flux confidentiel documenté](https://docs.discord.com/developers/discord-social-sdk/development-guides/account-linking-with-discord).
3. Conserver le callback OAuth de connexion Arkya, en local `https://localhost:7141/v1/auth/discord/callback`.
4. Ajouter le callback desktop du SDK : `http://127.0.0.1/callback`. Le SDK peut utiliser un port loopback dynamique ; Identity n'accepte que cet hôte et ce chemin, sans query, fragment ni identifiants.
5. Configurer les secrets existants `Discord:ClientId` et `Discord:ClientSecret` dans Identity, ainsi que MongoDB, HTTPS et les clés entre services, en suivant le [guide backend](../backend/README.md).

Le client reçoit l'identifiant public d'application et les scopes depuis Identity ; aucun secret n'est ajouté à un asset Unity. Les scopes demandés sont `openid sdk.social_layer identify`. Les lobbies nécessitent les permissions de communication même si cette tranche n'affiche aucun chat ; `identify` permet au backend de contrôler le compte social. Voir les [scopes du Social SDK](https://docs.discord.com/developers/discord-social-sdk/core-concepts/oauth2-scopes).

## Parcours et sécurité

1. Le joueur se connecte à son compte Arkya par le parcours Identity existant. Ce login utilise seulement `identify` et ne remet aucun jeton Discord au client.
2. Dans **Amis Discord**, il active le social. Le SDK génère un challenge PKCE et ouvre l'autorisation Discord.
3. Unity transmet le code, le vérificateur et le callback à Identity avec la session Arkya. Le backend échange le code avec son secret, vérifie les scopes, lit l'identité Discord et impose le même compte que celui de la session Arkya.
4. Le SDK reçoit le jeton d'accès social. Unity conserve ce jeton et le ticket de renouvellement uniquement en mémoire ; aucun enregistrement dans PlayerPrefs ou un asset.
5. Le client suit l'expiration de la session Arkya et renouvelle l'autorisation sociale lorsqu'elle approche de son échéance. Une déconnexion ou une expiration locale Arkya coupe le social. Identity revérifie expiration et révocation lors de chaque requête, y compris après l'appel au fournisseur.

Toutes les routes sociales exigent HTTPS et un bearer Arkya :

| Route | Entrée ou sortie |
| --- | --- |
| `GET /v1/auth/discord/social/config` | `applicationId`, `scopes`, `redirectUri` |
| `POST /v1/auth/discord/social/exchange` | Entrée : `code`, `codeVerifier`, `redirectUri` |
| `POST /v1/auth/discord/social/refresh` | Entrée : `refreshTicket` |
| Réponse des deux POST | `accessToken`, `refreshTicket`, `tokenType`, `expiresIn`, `scope`, `discordUserId` |

`refreshTicket` protège le refresh token Discord avec ASP.NET Core Data Protection, le lie à une session Arkya précise et expire avec elle. Le refresh token brut reste inaccessible au client ; chaque réponse remplace son ticket précédent. Les clés Data Protection doivent être protégées, conservées et partagées entre réplicas Identity. Les réponses portent `Cache-Control: no-store` et les jetons ne doivent pas être journalisés. Le [guide backend](../backend/README.md#autorisation-discord-social-sdk) décrit les erreurs et le déploiement.

La déconnexion sociale laisse la session Arkya active. Elle n'est pas une révocation globale des autorisations de l'application dans Discord. Les comptes provisoires Discord et une identité alternative au login Discord ne sont pas intégrés dans cette tranche.

La déconnexion explicite attend jusqu'à trois secondes pour quitter les salons avant de fermer le SDK. L'arrêt d'une scène ou du processus ne permet qu'une tentative de nettoyage : Discord conserve les appartenances lors d'une simple déconnexion réseau. Chaque nouvelle connexion sociale explicite quitte donc les anciens salons avant d'en créer un nouveau. Une reconnexion réseau automatique conserve le salon courant ; le compteur de présence compte seulement ses membres connectés.

## Présence et invitations

Le bouton **Amis Discord** est accessible depuis la création et l'essai local. Après connexion, un salon social est créé et la présence indique l'activité en cours. Une invitation acceptée rejoint le salon social de l'émetteur ; les commandes de déplacement et la connexion Netcode locale restent indépendantes.

Les secrets de jonction sont des entrées non fiables. Le client accepte exclusivement `arkya-social-v1:` suivi d'un GUID de 32 chiffres hexadécimaux, puis utilise cette valeur dans l'API de lobby Discord. Il n'interprète jamais ce secret comme une URL, une commande, une adresse de serveur ou un ticket Arkya. Une annulation ou un délai dépassé déconnecte le social pour éviter un état de salon partiellement appliqué. Une indisponibilité des lobbies peut laisser les amis disponibles, avec les invitations désactivées.

La future jonction au monde devra demander un droit d'admission au backend et au serveur autoritaire à partir de la session et du personnage. Elle ne pourra pas faire confiance au contenu d'une invitation Discord.

## Limites Discord

Au 16 octobre 2026, les limites de développement sont **par application**, partagées entre tous ses joueurs : 100 créations/jonctions de lobby par deux heures, 100 mises à jour de lobby par deux heures, 20 liaisons de canal par deux heures et 100 envois de messages par deux heures pour chacun des flux lobby et DM. L'accès de production nécessite une demande et une approbation **Comms Access** pour relever ces limites. Elles ne sont pas un budget adapté à un MMORPG public. Les conditions et la procédure sont dans les [fonctionnalités de communication Discord](https://docs.discord.com/developers/discord-social-sdk/core-concepts/communication-features).

## Vérification

Backend : **38 tests ciblés DiscordSocial réussis**, sans échec ni test ignoré, le 16 octobre 2026. Ils couvrent le happy path, les scopes manquants, le mauvais compte Discord, la rotation du ticket, une autre session, l'altération du ticket, l'expiration/révocation, les erreurs fournisseur et `no-store`. Les tests Mongo n'ont pas été relancés avec une base réelle dans cette tranche : moteur Docker indisponible et `ARKYA_TEST_MONGO` non configuré.

```powershell
dotnet test backend/tests/Arkya.Identity.Tests/Arkya.Identity.Tests.csproj --no-restore --filter FullyQualifiedName~DiscordSocial --verbosity minimal
```

La solution backend complète a également réussi : **263 tests passent, aucun échec, 10 scénarios Mongo ignorés** sans base de test configurée.

Validation Unity de cette intégration le 16 octobre 2026 : **29 tests réussis, aucun échec ni test ignoré** sous Unity 6000.6.6f1. Les 28 tests du client social couvrent notamment l'identité, les scopes, les invitations non fiables, la présence, les annulations et le nettoyage des salons ; ils utilisent un adaptateur simulé. Le test du panneau entre en Play Mode, vérifie le prérequis de compte Arkya, l'ouverture/fermeture et le focus, puis démarre un serveur Netcode autoritaire et son client local et vérifie leurs snapshots. La compilation des assemblages sociaux et la création/destruction d'un client SDK natif ont également réussi dans l'éditeur principal. Aucun player distribué ni plateforme autre que l'éditeur Windows n'a été validé.

Le script `scripts/discord-social/Test-DiscordSocial.ps1` copie les assets et le SDK dans un projet isolé sous `artifacts/discord-social-sdk/validation-project/`, puis lance les tests sociaux et le test du panneau avec serveur/client local. Il produit `unity-tests.xml` et `unity-tests.log` dans le dossier d'artefacts :

```powershell
./scripts/discord-social/Test-DiscordSocial.ps1 -UnityPath 'D:/Unity/6000.6.6f1/Editor/Unity.exe'
```

Le parcours réel avec deux comptes Discord n'a pas été validé. Après configuration du portail, le contrôle manuel doit couvrir : connexion Arkya puis sociale avec le même compte ; refus OAuth et mauvais compte ; demandes d'amitié de jeu ; blocage/déblocage ; présence création/essai ; invitation entre deux players ; annulation ; perte réseau ; déconnexion et expiration Arkya. Vérifier que rejoindre un salon social ne charge aucun realm et ne délivre aucun droit d'admission. Les tests simulés ne prouvent ni l'accès Comms Access ni le bon fonctionnement de l'application Discord réelle.
