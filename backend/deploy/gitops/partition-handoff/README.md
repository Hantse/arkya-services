# Préparation du runtime de transfert StarterIsland

Ce composant est **inactif** : le profil `admin` ne le référence pas. Son image
reprend volontairement le digest historique du profil bêta, son empreinte de
profil précédent vaut 64 zéros et `Automatic=false`. Il ne constitue pas une
release jouable et ne doit pas être activé avec ces valeurs.

Il prépare quatre Services `ClusterIP` stables dans `arkya-beta-realm`, leurs
routes statiques Gateway et trois règles réseau limitées aux quatre identités
`island-starter-island-01..04`. Gateway est le seul point d'entrée du jeu ;
les dedicateds peuvent appeler Characters et le canal de commandes Realms.
Aucun namespace, Deployment, Secret, Ingress, certificat ou droit RBAC n'est
créé ici. Seule l'annotation de phase des templates des trois Deployments
Gateway, Characters et Realms existants est modifiée. Le composant
`realm-control` garde son refus réseau par défaut.

Characters conserve le realm `beta-web` et dérive, depuis le seed existant,
les quatre nouvelles clés plus l'identité historique `island`. La clé du
serveur v1 reste ainsi reconnue pendant son drainage. Aucun secret ne figure
dans les manifests. Les Services et le routage v1 restent présents.

Le profil supplémentaire `beta-web-handoff` annonce exactement la paire
`2 / arkya-partition-handoff-v1`. Son template fournit une URL distincte par
processus : `wss://api.arkya.online/game/beta-web/{serverId}/`. La grille est
2 × 2 sur X/Z `[0,1536]`, sans instance ni raid. L'origine `(0,0)` est celle
du terrain et de l'[export cartographique vérifié](../../../../docs/world/starter-island/admin-map/README.md),
également utilisée par `CharacterAuthorityBoundaryTests` dans Unity.

## Vérification hors cluster

```powershell
python backend/deploy/gitops/partition-handoff/Validate-Manifests.py
```

Le test rend une copie temporaire du vrai profil Admin, ajoute le composant
et retire dans cette copie le champ historique `RealmRuntime__ServerId`.
Il compare le résultat au profil actuel : seuls sept objets privés, les
trois configurations exactes et leurs trois annotations de phase peuvent changer. Les anciennes images, leur
intention de release, les namespaces, PVC, Secrets et droits restent ceux
du profil actuel. Aucun appel Kubernetes n'est effectué.

## Activation après publication de la paire Unity

Avant activation, remplacer le digest de `Deployment__Profiles__1__Image`
par celui du nouveau dedicated officiel v2 et calculer
`PreviousProfileSha256` depuis l'exact `ProfileSnapshot` vivant avec
`RealmRuntimeReleases.Fingerprint`. Une empreinte calculée depuis une forme
JSON différente n'autorise pas la transition. Conserver les pins et
l'intention de release historique du profil 0.

Adopter la configuration puis déclencher la migration dans deux commits
distincts, après publication et validation des nouveaux services compatibles.
Le Deployment Realms doit utiliser `Recreate` avant ces étapes afin que ses
workers ayant des configurations différentes ne se chevauchent pas.

Dans le premier commit d'adoption :

1. Ajouter `../partition-handoff` aux composants de `admin/kustomization.yaml`.
2. Retirer `RealmRuntime__ServerId: island` de son patch Characters. Les
   patches du profil Admin passent après ceux des composants : conserver ce
   champ produirait simultanément `ServerId` et `ServerIds`, configuration
   refusée au démarrage.
3. Conserver `Deployment__RuntimeReleases__0__Automatic=false` et les trois
   annotations `arkya.online/partition-handoff-config-revision=prepared-v1`.
4. Exécuter la validation avec `--require-release`, publier le commit et
   attendre les trois nouvelles générations prêtes : routes Gateway,
   identités Characters et profil Realms. Le serveur v1 reste en place.

Les ConfigMaps générés changent aussi de nom avec leur contenu ; leurs
références font évoluer les templates. L'annotation explicite identifie
néanmoins la phase attendue et garantit son redémarrage observable.

Dans le second commit, après inspection des présences/leases, annonces et
sauvegardes, mettre `Deployment__RuntimeReleases__0__Automatic=true` et changer
uniquement l'annotation Realms en `enabled-v1`. Gateway et Characters gardent
`prepared-v1`. Valider avec :

```powershell
python backend/deploy/gitops/partition-handoff/Validate-Manifests.py --require-release --enable-migration
```

Publier ce second commit, puis suivre la nouvelle génération Realms et les
quatre dedicateds jusqu'à leur disponibilité effective. Une mise à jour de
ConfigMap seule ne doit pas être considérée comme une preuve de rechargement
des options par un processus déjà lancé.

Le mécanisme de migration et le droit limité de patch du Secret dédié sont
gérés séparément. Ne pas utiliser une simple release d'image pour modifier
protocole ou topologie. La migration garde `beta-web` et ses personnages ;
elle interrompt les connexions du serveur v1. Inspecter les présences et
leases, annoncer la maintenance et vérifier les sauvegardes avant lancement.

Après readiness des quatre dedicateds, vérifier l'admission native réelle,
les passages de frontières, la sauvegarde et la réadmission avant de remplacer
le manifeste Windows Bêta. Le client livré doit déclarer
`launcherSessionProtocol: 1`. Le canal Stable reste inchangé.
