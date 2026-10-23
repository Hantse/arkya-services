# Launcher Windows Arkya

`launcher/` fournit un launcher WPF .NET 10, en français, pour Windows x64.
Il vérifie les versions disponibles, télécharge et installe un build Unity,
puis lance son exécutable. La publication autonome inclut .NET et ne demande
pas de droits administrateur. Au démarrage, sa page de connexion Discord
précède l’accès au jeu, aux actualités et aux paramètres. Le launcher ne reçoit
aucun secret de service.

Le launcher distribué depuis la page [Jouer](https://arkya.online/play.html)
propose les canaux **Bêta** et **Stable**, avec Bêta sélectionné par défaut.
Leurs adresses sont fixées dans le code :

| Canal | Manifeste officiel |
| --- | --- |
| Bêta | `https://arkya.online/downloads/windows/latest-win-x64.json` |
| Stable | `https://arkya.online/downloads/windows/latest-stable-win-x64.json` |

Aucun champ d’adresse, fichier de configuration ou variable d’environnement
ne permet de remplacer ces adresses dans le launcher distribué. Les anciennes
préférences `manifestUrl` et l’ancien `launcher-settings.json` ne sont plus
utilisés. Choisir Stable ne réutilise jamais une publication Bêta.

Les archives versionnées sont hébergées dans les publications GitHub du dépôt
`Hantse/arkya-services`. Le manifeste public contient les tailles et l’empreinte
SHA-256 du ZIP réellement construit. Le launcher peut ouvrir une version déjà
installée si le serveur de distribution est indisponible, après connexion du
compte. Cela ne rend pas les services de jeu accessibles hors ligne.

Le launcher `0.4.0` se distribue avec un installateur Windows autonome,
`Arkya-Launcher-Setup.exe`. Il installe le launcher pour l’utilisateur courant,
par défaut dans `%LOCALAPPDATA%\Programs\Arkya Launcher`, avec choix du dossier
dans l’assistant, raccourci Démarrer et raccourci bureau facultatif. Le runtime
.NET est inclus : aucune étape de téléchargement supplémentaire n’est requise.
Le désinstalleur Windows retire uniquement les fichiers du launcher ; il conserve
le jeu et les préférences. L’archive ZIP portable reste une distribution
secondaire pour les usages manuels.

L’accueil rassemble une capture du jeu, les actualités et les notes de version,
avec une barre d’installation et de lancement toujours accessible. Les onglets
**Actualités** et **Notes de version** chargent les dernières publications
françaises depuis `https://api.arkya.online/v1/content/posts`. Leur contenu est
administré dans **Admin → Publications** ; voir [le guide](PUBLICATIONS.md).
Les articles sont affichés en texte brut. Une panne de ce flux ne bloque ni
l’installation ni le lancement ; les notes du manifeste restent disponibles
dans le panneau de version. Le canal se choisit dans le launcher ; la roue
dentée donne accès au dossier du jeu et aux informations d’installation.

## Connexion au launcher

Sans session, seule la page **Continuer avec Discord** est accessible.
Elle ouvre le navigateur pour l’autorisation Discord, puis le launcher attend
le résultat et vérifie `GET /v1/auth/me` avant d’afficher l’accueil. Le parcours
réutilise les challenges Identity avec une preuve S256 ; le secret Discord et
les jetons Discord restent côté serveur. Le joueur peut annuler puis recommencer.

Le bearer Arkya reste en mémoire : il n’est écrit ni dans les préférences ni
dans les arguments du jeu. Le refresh rotatif est mémorisé avec la protection
Windows DPAPI de l’utilisateur dans
`%LOCALAPPDATA%\Arkya\Launcher\launcher-session.dat`, hors du dossier installé.
Le launcher retrouve la connexion au démarrage et après sa mise à jour,
renouvelle l’accès avant expiration et vérifie le compte avant chaque lancement.
La connexion dure au maximum 30 jours depuis l’autorisation Discord ; une
révocation, un ban ou cette échéance demandent une nouvelle connexion.
**Déconnexion** ferme immédiatement l’accès à l’accueil et demande la révocation de la session. La connexion
du site reste indépendante. Voir les
[contrats natifs et leur validation](AUTH_SESSIONS.md#launcher-windows).

## Utilisation

1. Télécharger `Arkya-Launcher-Setup.exe` sur la page **Installer le launcher**,
   l’ouvrir, puis suivre l’assistant. Le dossier du launcher se choisit pendant
   cette installation ; aucun droit administrateur n’est demandé.
2. Ouvrir Arkya Launcher depuis le menu Démarrer et choisir **Continuer avec
   Discord**. Autoriser la connexion dans le navigateur, puis revenir au launcher.
3. Choisir **Bêta** ou **Stable**. La version du canal est vérifiée.
   Choisir **Installer le jeu**, sélectionner
   le dossier du jeu, puis attendre le téléchargement. Le jeu peut être installé
   sur un autre disque que le launcher. Choisir ensuite **Jouer**.
4. Une future version du même canal propose **Mettre à jour**. Une opération peut
   être annulée. Aucune version Stable n’est publiée à cette révision ; ce canal
   affiche son indisponibilité et ne propose aucun téléchargement ni lancement.

La page publique ne charge plus de lecteur Unity dans le navigateur. Son bouton
**Installer le launcher** télécharge l’installateur `.exe` Windows ; le bouton **Jouer** du
launcher ouvre ensuite le client natif. Le compte reste accessible sur le site.

## Entrée dans les royaumes

Après la connexion Discord dans le client Windows, le jeu consulte les
personnages du compte, les royaumes disponibles et
`GET /v1/characters/entry-context`. Un compte sans personnage choisit d’abord
son royaume. Pour un compte déjà joué, le client retrouve le dernier royaume
dont le serveur a accepté une admission, puis affiche les personnages de ce
royaume. Le choix d’un autre royaume reste accessible.

Le build actuellement publié `0.1.0-beta.20261023.1` conserve sa propre
connexion Discord dans le client Unity après celle du launcher. Le verrou
d’accès du launcher ne modifie pas ce binaire. Un futur client pourra recevoir
la session du launcher par un canal Windows privé ; le manifeste devra alors
déclarer explicitement `launcherSessionProtocol: 1`. Sans cette capacité
(`0` par défaut), aucun transfert de session n’est tenté. Le bearer ne transite
jamais dans une URL, un argument de processus ou un fichier.

L’historique ne retient que les personnages encore détenus par le compte.
S’il n’en reste aucun, ou si le dernier royaume n’est plus disponible ou
compatible, le client revient au choix du royaume. Un clic sur un royaume ou
une tentative de connexion échouée ne
remplace pas cet historique : seul le backend l’enregistre après consommation
réussie du ticket par un serveur dédié. Il est associé au compte, indépendamment
du PC et du launcher.

Le client `0.1.0-beta.20261023.1` provient de la
[CI Unity du 23 octobre](https://github.com/Hantse/arkya-mmorpg/actions/runs/39888123368),
source `2b34ab6ff45c0faf56c230136e9d115c6c7aa177`. Il inclut ce parcours natif,
la base Valkyrie et la sélection/création dans la cour d’Aubefont. Installer
cette version avec le launcher pour recevoir ces changements. La publication
initiale `0.1.0-beta.20261023` reste une exploration locale ; une mise à jour du
site ou du backend ne change pas ce précédent binaire. Voir
[le contrat d’entrée](CHARACTER_ENTRY.md) pour l’historique et
[le contrat d’admission](WEB_BETA_CONTRACT.md) pour le transport et les contrôles
côté serveur.

Le client natif conserve le domaine de l’URL d’admission et son éventuel port
non standard dans l’en-tête HTTP `Host` du WebSocket. Le nom TLS et la
validation des certificats restent inchangés. Le
[correctif du Transport embarqué](https://github.com/Hantse/arkya-mmorpg/blob/2b34ab6ff45c0faf56c230136e9d115c6c7aa177/Packages/com.unity.transport/ARKYA_FORK.md)
ne modifie ni les routes publiques ni les autorisations par adresse IP client.
Les contrôles natifs couvrent TLS, WebSocket et la connexion UTP ; l’admission
authentifiée dans le royaume public reste à vérifier.

Avant le premier téléchargement, le launcher demande le dossier d’installation
du jeu. Le dossier proposé est `%LOCALAPPDATA%\Arkya\Game` ; un dossier sur un
autre disque peut être choisi. Annuler le sélecteur ne démarre pas l’installation
et conserve le choix précédent. Le chemin choisi reste visible dans la barre
inférieure et dans les paramètres, avec **Modifier…**. Ce bouton est désactivé
pendant l’installation et lorsque le jeu est lancé.

Changer de dossier permet de retrouver une installation existante ; si le
nouveau dossier ne contient pas de version installée, le launcher propose
**Installer le jeu**. Il ne déplace et ne supprime pas automatiquement les
fichiers de l’ancien dossier. Les versions et leurs téléchargements restent
regroupés dans le dossier de jeu choisi.

Les préférences de canal et de dossier sont conservées atomiquement dans
`%LOCALAPPDATA%\Arkya\Launcher\launcher-preferences.json`, indépendamment du dossier
du launcher et du jeu. Le premier démarrage reprend l’ancien fichier
`%LOCALAPPDATA%\Arkya\Game\launcher-preferences.json` et reconnaît une installation
historique désignée par `current.json`, comme installation Bêta. Une ancienne
préférence contenant une adresse de manifeste est ignorée ; le canal Bêta est
retenu à sa place sans perdre le dossier choisi.

La Bêta conserve le dossier de jeu choisi, ses sous-dossiers `releases/` et son
`current.json` historique. Stable possède une installation séparée dans
`channels/stable/` sous ce même dossier de base, avec ses propres releases,
pointeur actif et verrou. Changer de canal ne remplace pas l’autre installation.
Les nouveaux pointeurs indiquent leur canal et le launcher refuse de les
utiliser pour un autre canal ; un ancien pointeur sans canal est reconnu
seulement pour Bêta.

Chaque version possède un dossier distinct sous `releases/` dans son installation.
Le fichier `current.json` désigne la version active. Les versions précédentes sont
conservées ; cette première version ne comporte pas de nettoyage automatique
ni de retour de version dans l’interface. Quitter le jeu et le launcher avant
de supprimer manuellement une ancienne version ; conserver celle référencée
par `current.json`. Les sauvegardes Unity ne sont jamais supprimées.

## Construire le client Unity

Le pipeline existant est
`Unity3D/Arkya/Assets/Arkya/Build/Editor/CiBuild.cs`, méthode
`Arkya.Build.Editor.CiBuild.BuildWindowsClient`. Il inclut les scènes
`CharacterCreation` et `StarterIsland`, avec les crédits et licences du client.

Utiliser Unity **6000.6.6f1**, avec le support Windows, dans un checkout de
build distinct du projet actuellement ouvert. Exemple PowerShell à adapter :

```powershell
& $unityEditor -batchmode -nographics -quit `
  -projectPath $buildCheckout -buildTarget Win64 `
  -standaloneBuildSubtarget Player `
  -executeMethod Arkya.Build.Editor.CiBuild.BuildWindowsClient `
  -arkyaCiOutputDir "$buildCheckout/ci/artifacts/windows-client" `
  -logFile "$buildCheckout/ci/artifacts/windows-client.log"
```

Le build exploitable se trouve dans
`ci/artifacts/windows-client/player/` et doit contenir `Arkya.exe`,
`UnityPlayer.dll` et `Arkya_Data/`. Vérifier les rapports du pipeline et tester
ce client avec les vrais services. Le launcher ne modifie pas son parcours
d’authentification ou de jeu et ne transforme pas l’essai local natif en une
admission publique sur un serveur dédié.

La CI officielle définit `ARKYA_CLIENT_IDENTITY_URL`,
`ARKYA_CLIENT_CHARACTERS_URL` et `ARKYA_CLIENT_REPORTS_URL` sur
`https://api.arkya.online`. Après le build Windows, elle vérifie que
`identityBaseUrl`, `charactersBaseUrl` et `reportsBaseUrl` dans `build.json`
portent exactement cette origine ; une différence fait échouer le job.

Pour un build manuel destiné aux joueurs, fournir ces mêmes variables au
processus Unity. `ARKYA_CLIENT_REPORTS_URL` est facultatif et reprend l’origine
Characters si absent. Le pipeline Windows applique temporairement ces origines
HTTPS publiques aux paramètres du client et restaure ensuite le fichier source
exact. Il refuse HTTP, localhost, identifiants, chemins, query et fragments.
Les origines effectivement intégrées figurent dans `build.json`.
Sans ces variables, le pipeline conserve la configuration de développement
du projet : ne pas publier un tel build s’il pointe encore vers localhost.

## Produire la version distribuable du jeu

Depuis la racine du dépôt, avec PowerShell 7 et .NET 10 :

```powershell
./launcher/scripts/New-GameRelease.ps1 `
  -BuildDirectory "$buildCheckout/ci/artifacts/windows-client/player" `
  -Version '0.1.0-beta.1' `
  -PackageUrl $publicPackageUrl `
  -OutputDirectory './artifacts/windows-release-0.1.0-beta.1' `
  -ReleaseNotes 'Première version Windows de test.'
```

`$publicPackageUrl` est l’adresse HTTPS réelle où sera servi le ZIP généré.
Le script produit `Arkya-<version>-win-x64.zip` et `latest-win-x64.json`.
Il inclut les fichiers du build, hors PDB, logs et dossiers de debug Unity
explicitement marqués « DoNotShip ». Il refuse les liens, un build incomplet,
une sortie située dans le build et l’écrasement d’une publication existante.

Publier d’abord le ZIP à son URL immuable, puis remplacer atomiquement le
manifeste du canal une fois le téléchargement vérifié. Conserver les anciens
ZIP tant qu’un client peut avoir reçu leur manifeste. Configurer une durée de
cache courte pour le manifeste et longue pour les archives versionnées.

Le contrat JSON est :

| Champ | Signification |
| --- | --- |
| `schemaVersion` | `1` |
| `version` | Identifiant lisible, 1–64 caractères alphanumériques, points, tirets, underscores |
| `platform` | `win-x64` |
| `packageUrl` | URL HTTPS du ZIP, sans identifiants ni fragment |
| `packageSize` | Taille exacte du ZIP en octets, maximum 50 Gio |
| `unpackedSize` | Somme exacte des tailles des fichiers extraits, maximum 100 Gio |
| `sha256` | SHA-256 du ZIP, 64 caractères hexadécimaux |
| `executable` | Chemin relatif de l’exécutable `.exe`, par exemple `Arkya.exe` |
| `releaseNotes` | Texte facultatif, 16 000 caractères maximum |
| `launcherSessionProtocol` | Facultatif, `0` par défaut ; `1` seulement pour un client compatible avec la remise privée de session |

Un canal sans version publiée sert explicitement ce marqueur, avec son nom
minuscule exact :

```json
{ "schemaVersion": 1, "channel": "stable", "available": false }
```

Le launcher affiche **Aucune version publiée**. Il ne prend pas le manifeste
d’un autre canal, même si une Bêta est déjà installée. Pour ouvrir Stable,
publier et vérifier d’abord un build destiné à ce canal, puis remplacer ce
marqueur par son manifeste complet. La Bêta actuelle reste sur son adresse
historique pour préserver les launchers précédents et la CI de publication.

Les manifestes sont limités à 64 Kio, et les archives à 200 000 entrées.
Le launcher refuse HTTP, y compris après redirection. Il borne les délais,
vérifie taille et SHA-256 avant extraction, interdit traversées de répertoire,
chemins Windows spéciaux, liens et doublons sans distinction de casse.
L’extraction est bornée par la taille déclarée. Une vérification de l’espace
disponible inclut archive, fichiers décompressés et 256 Mio de marge.

Le launcher `0.1.1` évite le renommage du dossier du jeu, qui peut échouer
lorsqu’un fichier extrait est encore ouvert. L’archive est extraite dans une release
unique, encore inactive. Après validation complète, seul `current.json` est
remplacé atomiquement ; aucun renommage du dossier du jeu n’est nécessaire.
Le verrou empêche deux écritures simultanées. Une erreur ou une annulation
conserve le pointeur et la version précédents ; le nettoyage ne cible que la
nouvelle release. Un arrêt brutal ou un verrou persistant peut laisser une
release inactive orpheline sans changer la version active.

Le test Windows reproduit le refus de renommage avec un fichier extrait ouvert,
puis valide la nouvelle activation. La validation du launcher `0.1.1` comptait
47 tests réussis.
Les essais d’installation complets conservent séparément l’ancien échec et la
réussite avec l’exact Core publié dans le launcher `0.1.1`.

Le SHA-256 protège l’intégrité par rapport au manifeste HTTPS. Il ne remplace
pas une signature indépendante contre la compromission de l’hébergement.
Cette première version ne fournit ni mise à jour différentielle, ni reprise
partielle du téléchargement. Le launcher `0.4.0` ajoute son propre mécanisme
de mise à jour décrit ci-dessous. Pour
une distribution publique, signer les exécutables Arkya et le launcher avec
le certificat de publication de l’équipe ; aucun certificat n’est présent
dans le dépôt. Le binaire produit localement est non signé.

## Construire et publier le launcher

```powershell
dotnet test launcher/Arkya.Launcher.slnx -c Release
./launcher/tests/Test-ReleasePackaging.ps1
./launcher/scripts/Publish-Launcher.ps1 `
  -InnoSetupCompiler $isccPath `
  -OutputDirectory './artifacts/windows-launcher-release'
```

Fournir le compilateur Inno Setup par `InnoSetupCompiler`, par la variable
`ARKYA_INNO_SETUP_COMPILER` ou via `ISCC.exe` dans le `PATH`. La version **7.1.0**
est utilisée pour cette publication. L’installation du compilateur appartient
au poste de build ; le joueur n’en a pas besoin.

Le script produit l’application autonome `win-x64`, l’installateur
`Arkya-Launcher-Setup.exe` et le ZIP portable `Arkya-Launcher-win-x64.zip`. La
version de l’installateur est lue dans le projet du launcher. Les deux canaux
officiels sont inclus dans l’application et ne demandent aucune configuration
d’adresse. Le flux public d’actualités reste indépendant du canal du jeu,
mais son affichage exige la connexion au launcher. Les dossiers de sortie
existants sont refusés pour préserver une publication antérieure.

L’emblème Arkya est intégré à l’exécutable et à la fenêtre WPF depuis
`launcher/src/Arkya.Launcher/Assets/Arkya.ico`, avec plusieurs tailles pour
l’Explorateur, les raccourcis et la barre des tâches. Le même ICO identifie
l’installateur ; les raccourcis et la liste des applications installées
reprennent l’icône de l’exécutable. Le PNG `Assets/Arkya.png` fournit l’emblème
affiché dans le launcher. Les futures publications incluent ces ressources ;
les anciens exécutables conservent leur icône jusqu’à leur remplacement.
La [source visuelle et les vérifications](APP_ICONS.md) couvrent aussi
l’icône partagée avec le client Unity.

Publier d’abord l’installateur dans une release immuable, puis modifier le lien
du site. Le lien du launcher `0.4.0` utilise la release `launcher-v0.4.0` ; les
mises à jour du jeu continuent à suivre le manifeste Windows et restent
indépendantes de cette version du launcher.

## Mise à jour du launcher

Le launcher vérifie son propre manifeste au démarrage, puis toutes les
30 minutes : `https://arkya.online/downloads/launcher/latest-win-x64.json`.
Une nouvelle version affiche **Mettre à jour et redémarrer**, y compris sur
la page de connexion. Le bouton attend la fin d’une installation ou la
fermeture du jeu. Le launcher vérifie la version, la taille et le SHA-256
du setup téléchargé depuis sa release officielle GitHub.

Après la fermeture du launcher, un helper caché revérifie le setup,
l’installe silencieusement dans le dossier enregistré pour l’utilisateur,
puis redémarre le launcher. Il attend le processus exact et ne ferme aucun
jeu. Préférences, dossiers du jeu et session DPAPI restent hors de ce
remplacement. La fermeture attend un renouvellement de session déjà engagé
afin de sauvegarder son successeur avant de quitter.

Une distribution portable propose l’installateur du site. Le launcher
`0.3.1` ne possède pas cet updater : le premier passage à `0.4.0` exige
l’installation de cette nouvelle version. Les mises à jour suivantes sont
proposées directement dans le launcher.

`Publish-Launcher.ps1` produit aussi le manifeste du launcher, avec
`schemaVersion`, `version`, `platform`, `installerUrl`, `installerSize`
et `sha256`. Publier d’abord le setup immuable et le vérifier, puis déposer
ce manifeste sous `dist/downloads/launcher/latest-win-x64.json`. Le manifeste
du jeu reste indépendant sous `downloads/windows/`.

Vérification de `0.4.0` : 184 tests Core et 12 tests WPF réussis ; essai
réel du helper avec installateurs Inno isolés `0.3.1 → 0.4.0`, dossier
personnalisé, attente du processus, installation silencieuse, redémarrage
et conservation/déchiffrement de la session DPAPI. Aucun launcher installé
chez l’utilisateur n’est remplacé par ces essais.
Le renouvellement accepte l’arrondi des dates par MongoDB sans jamais
prolonger la limite absolue de connexion.

L’installateur peut être vérifié dans un dossier de test neuf, sans démarrer
le launcher ni télécharger le jeu :

```powershell
./launcher/tests/Test-LauncherInstaller.ps1 `
  -InstallerPath './artifacts/windows-launcher-release/Arkya-Launcher-Setup.exe' `
  -PublishDirectory './artifacts/windows-launcher-release/Arkya-Launcher' `
  -OutputDirectory './artifacts/windows-launcher-installer-check'
```

Ce scénario installe dans un chemin contenant des espaces, vérifie les
empreintes des fichiers, réinstalle, puis désinstalle. Il refuse une installation
Arkya existante pour préserver celle du poste et vérifie la conservation du jeu
et des préférences.

La publication utilise le déploiement autonome en fichier unique avec
extraction des bibliothèques natives, conformément à la
[documentation .NET](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/).
Le runtime WPF n’est pas supprimé par trimming.

## Vérification

La suite Core vérifie les canaux, la migration, la connexion Discord et le
transfert de session. Le correctif `0.3.1` accepte le format UUID avec tirets
renvoyé par Identity en production ; les réponses d’Identity et de `/me`
doivent toujours désigner le même compte. Les tests WPF chargent les vrais
écrans dans un dispatcher STA avec un dossier de préférences temporaire et un
transport qui interdit le réseau. Ils vérifient le verrou de connexion, les
commandes désactivées, l’expiration avec annulation et purge du contenu, et le
rendu aux dimensions 1280 × 830 et 1040 × 720.

```powershell
dotnet test launcher/tests/Arkya.Launcher.Tests -c Release
dotnet test launcher/tests/Arkya.Launcher.UiTests -c Release
```

Ces scénarios ne réalisent pas une connexion Discord réelle ni une admission
dans un royaume public.

La validation historique de la version `0.2.0` comptait **63 tests Core**, dont le choix et la mémorisation du
dossier, la migration des préférences, la conservation des anciennes
installations et l’annulation ou le refus d’une destination. La compilation WPF
termine sans erreur ni avertissement. L’installateur réel a été installé,
réinstallé et désinstallé dans un dossier isolé avec comparaison des empreintes
du payload et conservation des fichiers utilisateur. Le test ne lance pas le
launcher ; l’interaction visuelle avec le sélecteur Windows reste à vérifier.

Les tests utilisent un transport HTTP simulé et des archives synthétiques
qui ne sont jamais exécutées. Ils couvrent installation/mise à jour, maintien
de l’ancienne version, SHA-256, tailles annoncées et streaming, annulation,
verrou concurrent, état local corrompu, contenu Unity incomplet, traversées de
chemin, liens, noms Windows réservés, doublons et redirections vers HTTP.
Le flux de publications est aussi vérifié : contrats, texte brut, absence
d’authentification, bornes de taille et erreurs réseau.

Une capture du vrai rendu WPF est possible sans afficher de fenêtre :

```powershell
$process = Start-Process -FilePath $launcherExe `
  -ArgumentList @('--smoke-test', $absolutePngPath) -WindowStyle Hidden -PassThru
$process.WaitForExit(30000)
```

Ce mode capture la fenêtre hors écran puis quitte, sans téléchargement ni
lancement de jeu. Il valide l’initialisation WPF et permet une inspection
visuelle. Il ne constitue pas un test d’installation d’un build réel ou une
validation réseau en jeu.
