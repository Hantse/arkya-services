# Persistance par realm et routage des services

Cette première tranche sépare les données de personnage par realm, tout en
conservant les API Characters existantes. Elle ajoute une configuration
explicite des destinations MongoDB, un annuaire global minimal, une migration
hors ligne et un choix d'URL Characters par realm pour les dedicateds.

`RealmStorage:Enabled` vaut `false` par défaut. L'activation exige une migration
terminée pour chaque destination configurée. Ce changement ne déploie aucun
service, compte MongoDB, namespace ou certificat dans l'environnement réel.

## Données et propriétaires

| Périmètre | Données |
| --- | --- |
| Identity global | Comptes, sessions et droits d'accès existants |
| Base globale Characters, désignée par `Mongo:DatabaseName` | `character_directory`, réservation globale des noms et marqueurs `realm_storage_migrations` |
| Base propre à chaque realm | `characters`, `game_leases`, `character_quests` |
| Realms et Content globaux | Catalogue de realms, exploitation et catalogues de contenu existants |

Une entrée d'annuaire relie l'identifiant du personnage à son compte, son realm
et son nom normalisé. La liste des personnages d'un compte reste globale et
les noms restent uniques entre realms. La fiche complète, les leases,
admissions, snapshots de simulation, possessions et quêtes sont lus et écrits
dans la destination du realm. Le dedicated continue d'appeler Characters ; il
ne reçoit aucun identifiant MongoDB.

La création réserve durablement le nom dans l'annuaire avant de créer le
personnage dans sa base. Une intention `Pending` permet de reprendre cette
étape après interruption, puis devient `Ready`. Une réservation incomplète ou
une destination indisponible ne déclenche aucune lecture de secours dans les
anciennes collections.

Le `POST` public de création ne possède pas de clé d'idempotence. Après une
réponse `503` ou un timeout dont le résultat est incertain, relire la liste des
personnages du compte avant de créer à nouveau : cette lecture reprend une
intention `Pending` existante. Répéter aveuglément la création peut seulement
retrouver un nom déjà réservé, sans rendre la requête HTTP idempotente.

La migration conserve les collections historiques dans la base globale. En
mode isolé, elles constituent une copie de conservation et ne sont plus la
source de gameplay active. Une panne ou une incohérence de routage est une
indisponibilité ; elle ne provoque jamais un retour automatique au stockage
historique.

Le compte reste global. En mode isolé, une création exige un `realmId`
configuré dans `RealmStorage:Realms` et présent dans le catalogue Realms ; une
requête sans realm configuré reçoit `realm_required`. Le rattachement d'un
personnage existant reste fixe. Le ticket est recherché dans le realm issu de
l'identité authentifiée du dedicated, avec les contrôles existants de compte,
lease, époque et révision.

## Configuration Characters

Le [modèle de variables pour le Secret Characters](../backend/deploy/realms/realm-storage.secrets.env.example)
complète les `envFrom` existants sans ajouter de secret aux manifests versionnés.

Extrait de configuration sans secrets, avec deux destinations explicites :

```json
{
  "Mongo": {
    "DatabaseName": "arkya_characters"
  },
  "RealmStorage": {
    "Enabled": false,
    "Realms": [
      { "RealmId": "nova", "DatabaseName": "arkya_realm_nova" },
      { "RealmId": "eu-one", "DatabaseName": "arkya_realm_eu_one" }
    ]
  }
}
```

Les connexions doivent être fournies séparément, dans le gestionnaire de
secrets de l'environnement, sous ces clés :

- `Mongo__ConnectionString` pour la base globale et la source historique ;
- `RealmStorage__Realms__0__ConnectionString` pour Nova ;
- `RealmStorage__Realms__1__ConnectionString` pour EU One.

L'extrait n'est donc pas une configuration complète à démarrer seul. Chaque
entrée exige sa propre `ConnectionString`, même avec `Enabled=false` lors de
la préparation de la migration. Aucun secret ne doit être ajouté au JSON
versionné, aux arguments de commande ou aux réponses Admin.

La configuration accepte au plus 100 realms. Les IDs sont exacts, uniques et
respectent le format d'identifiant Arkya. Chaque nom de base contient de 1 à
63 caractères ASCII alphanumériques, `_` ou `-`. Les noms sont distincts entre
destinations, sans distinction de casse, et différents du nom de la base
globale. Les connexions sont validées par le pilote MongoDB. Aucune base n'est
calculée à partir d'une URL, d'un profil de déploiement ou d'un champ libre
envoyé par le client.

L'exploitation provisionne les bases et les comptes autorisés avant la
bascule. Chaque connexion de realm doit utiliser un compte limité à sa base,
avec les droits nécessaires aux collections et index Characters. Le compte
global possède l'annuaire et les marqueurs. Un même cluster MongoDB peut
héberger plusieurs bases ; cette séparation logique ne fournit pas, à elle
seule, des machines ou des domaines de panne distincts.

## Migration hors ligne

Le programme Characters possède deux commandes. Elles utilisent la
configuration et les secrets déjà injectés, puis quittent sans ouvrir le
serveur HTTP. Depuis le répertoire d'une publication Characters contenant sa
configuration, garder `RealmStorage:Enabled=false` et lancer :

```powershell
dotnet Arkya.Characters.Api.dll --realm-storage-migration plan
```

Le plan lit la source, résout les destinations et affiche un rapport JSON
contenant les nombres de personnages, leases et quêtes, une empreinte de la
source et `Applied=false`. Il ne copie aucun document et ne crée aucun
marqueur. Chaque realm rencontré dans la source doit posséder une destination
configurée ; les leases ou quêtes sans personnage doivent être résolues avant
la migration.

Les anciens personnages sans realm exigent un choix explicite de l'opérateur,
par exemple :

```powershell
dotnet Arkya.Characters.Api.dll --realm-storage-migration plan --unassigned-realm nova
```

Ce choix affecte tous les personnages historiques sans realm à la destination
indiquée. Il doit être conservé lors de l'application et des retries de cette
migration. Le programme ne choisit pas un realm par défaut.

Procédure de bascule :

1. Préparer une sauvegarde de la source, les destinations, leurs comptes et
   leurs secrets. Configurer tous les realms présents dans les données.
2. Bloquer les admissions, arrêter proprement les dedicateds avec leur
   sauvegarde finale et leur libération de lease, puis arrêter toutes les
   instances Characters et autres writers historiques. Attendre la libération
   ou l'expiration des derniers tickets et transferts.
3. Exécuter le plan avec cette configuration. Le plan et l'application
   refusent une source contenant encore une lease, un ticket ou un transfert
   dont l'expiration est future.
4. Appliquer avec l'accusé explicite d'arrêt des writers :

   ```powershell
   dotnet Arkya.Characters.Api.dll --realm-storage-migration apply --writers-stopped true
   ```

   Ajouter `--unassigned-realm nova` si ce choix était nécessaire au plan.
   `--writers-stopped true` est une déclaration de l'opérateur : le programme
   n'arrête aucun processus ni aucun workload.
5. Vérifier le rapport appliqué et les marqueurs `Complete` pour toutes les
   destinations. Passer ensuite `RealmStorage:Enabled=true`, puis démarrer
   Characters avec exactement les mêmes destinations et secrets.
6. Vérifier la liste et les fiches Admin, la restauration d'un personnage avec
   ses possessions et quêtes, puis le parcours d'admission, sauvegarde,
   transfert et déconnexion avant de rouvrir les admissions générales.

Les documents sont copiés en BSON sans reconstruction des snapshots : IDs,
schémas, révisions, époques et données de gameplay restent conservés. Le seul
ajout métier éventuel est le `RealmId` choisi explicitement pour un personnage
historique non rattaché. La source reste intacte.

Un document de destination déjà présent doit être identique à celui attendu.
Un conflit n'est jamais résolu par écrasement. L'application vérifie également
les décomptes, l'absence de documents supplémentaires et la correspondance
entre personnages et annuaire avant d'achever le marqueur du realm. Elle
compare l'empreinte complète de la source avant et après copie. Une source
modifiée depuis une tentative précédente ou une destination différente de
celle déjà migrée demande une résolution explicite par l'opérateur.

Une interruption peut laisser une copie partielle et un marqueur `Copying`.
Le mode isolé reste alors indisponible. Après résolution de la cause, une
nouvelle application avec la même source accepte les documents déjà copiés
identiques ; une destination déjà `Complete` n'est pas recopiée. Le démarrage
isolé exige un marqueur `Complete` lié au realm, au
nom de base et à l'empreinte de la destination ; changer la destination n'est
pas une simple modification d'URL autorisant une activation vide.

Pour un realm neuf sans données historiques, exécuter aussi cette procédure
avec une destination vide : l'application crée son marqueur après vérification
des décomptes nuls. Ajouter seulement une route à `RealmStorage:Realms` ne
suffit pas à activer un nouveau realm. L'ajout à une installation existante
reste une opération hors ligne ; conserver toutes les destinations déjà
configurées et la source historique intacte.

Un retour au mode historique est possible avant toute nouvelle écriture dans
les bases isolées, après arrêt des writers. Dès que le nouveau mode a reçu
des écritures, l'ancienne copie est périmée : un retour exige une migration
inverse contrôlée, qui n'est pas fournie par cette commande. Désactiver
`Enabled` seul ne constitue pas une restauration.

## URL Characters par realm

Les destinations HTTP sont configurées séparément dans **Realms**, sans
introduire d'URL opérateur dans les profils ou contrats publics :

```json
{
  "RealmRuntime": {
    "CharactersBaseUrl": "https://characters.internal.example/",
    "RealmsBaseUrl": "https://realms.internal.example/",
    "Services": [
      {
        "RealmId": "nova",
        "CharactersBaseUrl": "https://characters-nova.internal.example/"
      }
    ]
  }
}
```

Cet extrait complète les clés de runtime existantes, dont la seed privée des
identités dedicated. `CharactersUrlFor(realmId)` sélectionne une entrée par
égalité exacte ; en son absence, il utilise le `CharactersBaseUrl` commun.
Le déployeur injecte le résultat dans `ARKYA_CHARACTERS_URL`. Une façade
Characters commune peut ainsi conserver le routage des bases de tous les
realms sans multiplier les processus HTTP.

`RealmsBaseUrl`, lorsqu'il est renseigné, devient `ARKYA_REALMS_API_URL` et
conserve un canal de commandes indépendant du choix de l'API Characters.
Le dedicated sait déjà utiliser cette variable. Il faut la configurer
explicitement avec une destination Characters HTTPS distincte : la déduction
historique de l'API Realms connaît seulement l'origine HTTP Characters du
cluster bêta.

Les listes de services sont bornées à 100 entrées avec IDs uniques. Les URLs
exigent HTTPS, une origine sans chemin métier, identifiant utilisateur, query
ou fragment, et une longueur maximale de 512 caractères. Avec
`RealmRuntime:AllowClusterHttp=true`, les seules origines HTTP acceptées sont :

- Characters : `http://arkya-characters.arkya-beta.svc.cluster.local:8080/` ;
- Realms : `http://arkya-realms.arkya-beta.svc.cluster.local:8080/`.

Les endpoints distincts par realm nécessitent donc HTTPS avec le binaire
Unity actuel. L'exploitation fournit DNS, certificats reconnus, services et
règles réseau. Modifier la configuration runtime ne migre ni une DB, ni un
snapshot de gameplay, ni un workload déjà présent.

Les routes publiques du launcher et du client, ainsi que l'API privée Admin,
continuent d'utiliser la façade Characters configurée. La liste `Services`
choisit l'API du dedicated ; elle ne crée pas de routage de gateway par realm
et ne configure pas les autres clients. Des façades HTTP séparées doivent
partager un annuaire cohérent et avoir accès aux destinations nécessaires aux
requêtes globales. Cette tranche ne fournit pas d'isolation RBAC de l'Admin
par realm ni de déploiement automatique de ces façades.

## Limites de cette tranche

La sauvegarde reste un snapshot HTTP autoritaire, conditionné par la lease,
l'époque et la révision. Le batch d'événements, son acquittement durable,
le worker asynchrone, le replay et le bus de synchronisation par realm restent
une phase suivante. Aucun scénario de migration en ligne, de bascule de
production ou de restauration inverse n'est validé par cette implémentation.

## Vérification

Le 24 octobre 2026, les suites Characters (**364 tests**), Realms (**510 tests**)
et Backend Flow (**1 test**) passent avec MongoDB jetable : **875 réussites,
aucun échec ni test ignoré**. Le programme compilé a également exécuté `plan`
puis `apply` sur une destination vide de test ; le service a ensuite démarré
avec le mode isolé activé et répondu `200` sur `/health`. Ce contrôle de
démarrage complète les parcours HTTP joueur/dedicated testés avec deux bases.

La vérification de cette tranche porte sur les contrats HTTP backend et les
repositories avec MongoDB de test, ainsi que la validation des routes de
service et les manifests produits en mémoire. La suite Characters couvre les
parcours de personnages, admissions, persistance et administration ; les tests
de migration exercent séparément les copies et les refus de conflit. Le test
du manifest ne constitue pas un déploiement Kubernetes.

Aucune migration du cluster réel ni session Unity après bascule n'est
revendiquée ici. Le parcours de contrôle après activation, les identités
MongoDB et les certificats de l'environnement restent à valider sur le realm
de test avant une ouverture publique.

Sources principales :

- [Configuration et validation du stockage](../backend/src/Arkya.Characters.Api/Application/RealmStorageOptions.cs).
- [Résolution des bases et garde d'activation](../backend/src/Arkya.Characters.Api/Infrastructure/Persistence/RealmMongoDatabaseProvider.cs).
- [Migration hors ligne](../backend/src/Arkya.Characters.Api/Infrastructure/Persistence/RealmStorageMigration.cs).
- [Réservation globale et lecture du personnage](../backend/src/Arkya.Characters.Api/Infrastructure/Persistence/MongoCharacterRepository.cs).
- [Configuration du routage HTTP](../backend/src/Arkya.Realms.Api/Application/RealmRuntimeOptions.cs).
- [Injection dans le dedicated](../backend/src/Arkya.Realms.Api/Infrastructure/KubernetesRealmDeployer.cs).
