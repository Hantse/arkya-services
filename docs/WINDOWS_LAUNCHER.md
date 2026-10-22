# Launcher Windows Arkya

`launcher/` fournit un launcher WPF .NET 10, en français, pour Windows x64.
Il vérifie les versions disponibles, télécharge et installe un build Unity,
puis lance son exécutable. La publication autonome inclut .NET et ne demande
pas de droits administrateur. La connexion Discord et l’admission restent
gérées par le client Unity ; le launcher ne reçoit aucun secret de service.

Le launcher distribué depuis la page [Jouer](https://arkya.online/play.html)
utilise le canal `https://arkya.online/downloads/windows/latest-win-x64.json`.
Les archives versionnées sont hébergées dans les publications GitHub du dépôt
`Hantse/arkya-services`. Le manifeste public contient les tailles et l’empreinte
SHA-256 du ZIP réellement construit. Le launcher peut ouvrir une version déjà
installée si le serveur de distribution est indisponible. Cela ne rend pas les
services de jeu accessibles hors ligne.

L’accueil rassemble une capture du jeu, les actualités et les notes de version,
avec une barre d’installation et de lancement toujours accessible. Les onglets
**Actualités** et **Notes de version** chargent les dernières publications
françaises depuis `https://api.arkya.online/v1/content/posts`. Leur contenu est
administré dans **Admin → Publications** ; voir [le guide](PUBLICATIONS.md).
Les articles sont affichés en texte brut. Une panne de ce flux ne bloque ni
l’installation ni le lancement ; les notes du manifeste restent disponibles
dans le panneau de version. Les paramètres de distribution sont accessibles
par la roue dentée en bas de la barre latérale.

## Utilisation

1. Extraire `Arkya-Launcher-win-x64.zip` puis ouvrir `Arkya.Launcher.exe`.
2. Si le canal n’est pas préconfiguré, renseigner l’adresse HTTPS du manifeste
   fournie par l’équipe Arkya dans **Paramètres de distribution**.
3. La version est vérifiée au démarrage. Choisir **Installer le jeu**, puis **Jouer**.
4. Une future version propose **Mettre à jour**. Une opération peut être annulée.

La page publique ne charge plus de lecteur Unity dans le navigateur. Son bouton
**Installer le launcher** télécharge l’archive Windows ; le bouton **Jouer** du
launcher ouvre ensuite le client natif. Le compte reste accessible sur le site.

## Entrée dans les royaumes

Après la connexion Discord dans le client Windows, le jeu consulte les
personnages du compte, les royaumes disponibles et
`GET /v1/characters/entry-context`. Un compte sans personnage choisit d’abord
son royaume. Pour un compte déjà joué, le client retrouve le dernier royaume
dont le serveur a accepté une admission, puis affiche les personnages de ce
royaume. Le choix d’un autre royaume reste accessible.

L’historique ne retient que les personnages encore détenus par le compte.
S’il n’en reste aucun, ou si le dernier royaume n’est plus disponible ou
compatible, le client revient au choix du royaume. Un clic sur un royaume ou
une tentative de connexion échouée ne
remplace pas cet historique : seul le backend l’enregistre après consommation
réussie du ticket par un serveur dédié. Il est associé au compte, indépendamment
du PC et du launcher.

Ce parcours nécessite un client construit avec ces changements. La publication
initiale `0.1.0-beta.20261023` reste une exploration locale ; modifier le site ou
le backend ne met pas à jour ce binaire. Une nouvelle archive Windows validée
doit être publiée sous une version distincte avant de changer le manifeste du
canal. Voir [le contrat d’entrée](CHARACTER_ENTRY.md) pour l’historique et
[le contrat d’admission](WEB_BETA_CONTRACT.md) pour le transport et les contrôles
côté serveur.

Le jeu est installé dans `%LOCALAPPDATA%\Arkya\Game`. La préférence de canal
reste dans `launcher-preferences.json`. `ARKYA_LAUNCHER_MANIFEST_URL` permet
une configuration locale prioritaire ; sinon la préférence locale précède
`launcher-settings.json` situé près de l’exécutable. Ne renseigner que des
canaux officiels : le propriétaire du manifeste choisit le code téléchargé.

Chaque version possède un dossier distinct sous `releases/`. Le fichier
`current.json` désigne la version active. Les versions précédentes sont
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

Pour un client Windows destiné aux joueurs, fournir au processus Unity
`ARKYA_CLIENT_IDENTITY_URL=https://api.arkya.online` et
`ARKYA_CLIENT_CHARACTERS_URL=https://api.arkya.online`.
`ARKYA_CLIENT_REPORTS_URL` est facultatif et reprend l’origine Characters
si absent. Le pipeline Windows applique temporairement ces origines HTTPS
publiques aux paramètres du client et restaure ensuite le fichier source
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

Les manifestes sont limités à 64 Kio, et les archives à 200 000 entrées.
Le launcher refuse HTTP, y compris après redirection. Il borne les délais,
vérifie taille et SHA-256 avant extraction, interdit traversées de répertoire,
chemins Windows spéciaux, liens et doublons sans distinction de casse.
L’extraction est bornée par la taille déclarée. Une vérification de l’espace
disponible inclut archive, fichiers décompressés et 256 Mio de marge.

L’installation est préparée dans un dossier temporaire et un verrou empêche
deux écritures simultanées. Le pointeur actif est remplacé atomiquement
seulement après validation du package complet. Une annulation ou une erreur
conserve la version précédente. Les fichiers temporaires de cette opération
sont nettoyés ; après un arrêt brutal du processus, un dossier temporaire
orphelin peut rester sans changer la version active.

Le SHA-256 protège l’intégrité par rapport au manifeste HTTPS. Il ne remplace
pas une signature indépendante contre la compromission de l’hébergement.
Cette première version ne fournit ni mise à jour différentielle, ni reprise
partielle du téléchargement, ni mise à jour automatique du launcher. Pour
une distribution publique, signer les exécutables Arkya et le launcher avec
le certificat de publication de l’équipe ; aucun certificat n’est présent
dans le dépôt. Le binaire produit localement est non signé.

## Construire et publier le launcher

```powershell
dotnet test launcher/Arkya.Launcher.slnx -c Release
./launcher/tests/Test-ReleasePackaging.ps1
./launcher/scripts/Publish-Launcher.ps1 `
  -ManifestUrl $publicManifestUrl `
  -OutputDirectory './artifacts/windows-launcher-release'
```

Le script produit une application autonome `win-x64` et son ZIP. Si
`ManifestUrl` est omis, le launcher demande le canal dans ses paramètres ;
aucun téléchargement du jeu ne peut commencer sans ce canal. Le flux public
d’actualités reste indépendant de ce réglage. Les dossiers de sortie
existants sont refusés pour préserver une publication antérieure.

La publication utilise le déploiement autonome en fichier unique avec
extraction des bibliothèques natives, conformément à la
[documentation .NET](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/).
Le runtime WPF n’est pas supprimé par trimming.

## Vérification

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
