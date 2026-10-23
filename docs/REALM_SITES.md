# Sites dédiés aux realms

Un realm possède deux adresses dérivées de son identifiant stable :

| Adresse | Usage |
| --- | --- |
| `https://arkya.online/` et `/realms.html` | Liste publique des realms |
| `https://{realmId}.arkya.online/` | État public de ce monde |
| `https://admin.arkya.online/Realms` | Liste opérateur et création |
| `https://{realmId}.admin.arkya.online/` | Administration dédiée : aperçu, réglages, maintenance, commandes et historique |

Les sites partagent les services existants. Aucun déploiement web ni base de
données supplémentaire n'est créé pour chaque realm. L'identifiant conserve
la syntaxe existante de 3 à 31 caractères. Les noms `api`, `admin`, `www`,
`backoffice` et `beta-game` sont réservés pour le routage des sites.

## Catalogue public

`Arkya.Realms.Api` fournit deux lectures anonymes :

- `GET /v1/public/realms` retourne un tableau trié par identifiant.
- `GET /v1/public/realms/{realmId}` retourne un objet, ou `404` pour un
  identifiant inconnu, invalide ou un brouillon.

Le DTO contient uniquement `realmId`, `displayName`, `status`,
`readyServerCount`, `serverCount`, `updatedAt`, `lastObservedAt` et
`playerCount`. Les realms en suppression, en échec de suppression ou supprimés
disparaissent de ce catalogue. Leur page publique retourne `404` ; la prochaine
actualisation retire leur ligne de la liste. Le DTO ne contient ni adresses réseau, image, namespace, erreur
interne, historique opérateur, clé ou commande. Les brouillons restent privés.
`playerCount` vaut actuellement `null` : Realms ne possède pas de mesure
autoritaire de population. L'interface indique cette indisponibilité.

`lastObservedAt` est persisté après chaque observation Kubernetes réussie,
y compris sans changement d'état. `updatedAt` conserve la date du dernier
changement. Une erreur de réconciliation ne rafraîchit pas l'observation.
Si celle-ci manque, vient du futur ou dépasse
`max(30 secondes, 3 × Deployment:ReconcileIntervalSeconds)`, un realm
`Ready` est présenté comme `Unknown` et le nombre de serveurs prêts vaut zéro.
Les anciennes données deviennent observables après une réconciliation réussie.

Les pages consultent ces routes sur leur propre origine, sans cookies ni
bearer. Elles s'actualisent toutes les dix secondes, suspendent leurs lectures
en arrière-plan et signalent une panne ou des données anciennes. Il s'agit
d'une actualisation périodique, sans flux push. La disponibilité décrite est
celle des serveurs ; elle ne prouve pas une admission ou une entrée en jeu.

La gateway publie seulement les lectures de ce catalogue, retire les
identifiants reçus avant de joindre Realms et conserve les routes opérateur
et internes privées. Une adresse publique de realm ne peut pas consulter le
détail d'un autre identifiant. La sélection du realm public vient du nom
d'hôte ; une URL inconnue ne crée aucune entrée.

## Administration et connexion

`RealmSites:Enabled` active les liens et l'espace dédié dans Admin ;
`RealmSites:BaseDomain` vaut `arkya.online` par défaut. La racine d'un
sous-domaine admin ouvre le realm correspondant. Les contrôles de session,
allowlist Discord, antiforgery, révisions et motifs restent appliqués.
Un identifiant transmis dans un formulaire ou une URL ne peut pas remplacer
celui du nom d'hôte. Les pages globales rejoignent l'administration centrale.

La connexion commence toujours sur `admin.arkya.online`, avec le callback
Discord existant `https://admin.arkya.online/signin-discord`. Le retour local
`/Realms/{realmId}` ramène ensuite à l'espace dédié. Le cookie de corrélation
OAuth reste propre au domaine central. Les cookies de session et de
renouvellement sont partagés uniquement sous `admin.arkya.online`, jamais avec
les pages publiques. Cette portée suit la [configuration ASP.NET Core des
cookies entre sous-domaines](https://learn.microsoft.com/aspnet/core/security/cookie-sharing?view=aspnetcore-10.0#share-cookies-across-subdomains).

Le mode partagé utilise `Arkya.Admin.RealmSites` et
`Arkya.Admin.Refresh.RealmSites`, distincts des anciens cookies propres au
domaine central. Une connexion initiale est nécessaire après activation ;
les anciennes sessions ne bloquent pas ce parcours par un cookie homonyme.
La déconnexion depuis un realm révoque la session partagée dans le store Admin.
Les cookies antiforgery restent propres à chaque hôte.

## Configuration et activation

Le profil GitOps [admin](../backend/deploy/gitops/admin/README.md) active le
catalogue anonyme, les routes wildcard et l'espace Admin dédié. L'Application
Argo existante `arkya-beta` conserve son chemin
`backend/deploy/gitops/admin` sur `master`. Les nouvelles images compatibles
Gateway/Admin/Realms/frontend sont livrées et promues par la CI ; la
synchronisation existante applique ensuite la configuration. Aucun profil
supplémentaire ni changement de source Argo n'est nécessaire.

Pour un lancement hors de ce profil :

```text
# Gateway, en complément des destinations Admin et Frontend existantes
Gateway__PublicRealmsEnabled=true
Gateway__PublicRealmsAddress=https://<service-realms-prive>:8443/
Gateway__RealmSitesEnabled=true
Gateway__RealmBaseDomain=arkya.online
AllowedHosts=api.arkya.online;arkya.online;admin.arkya.online;*.arkya.online;*.admin.arkya.online

# Admin
RealmSites__Enabled=true
RealmSites__BaseDomain=arkya.online
AllowedHosts=admin.arkya.online;*.admin.arkya.online
Discord__PublicBaseUrl=https://admin.arkya.online
```

Les réseaux et certificats internes existants doivent autoriser Gateway à
joindre Realms. À l'entrée publique, configurer DNS et Traefik pour
`*.arkya.online` **et** `*.admin.arkya.online`, avec TLS couvrant ces deux
niveaux. Le certificat `*.arkya.online` seul ne couvre pas les noms
`{realmId}.admin.arkya.online`. Le profil ne provisionne ni DNS ni certificats
publics. Le routeur Traefik existant qui couvre les sous-domaines à un niveau
prend déjà en charge les realms publics ; lui ajouter les domaines
`{realmId}.admin.arkya.online` et le SAN `*.admin.arkya.online` du certificat,
avec le resolver DNS existant. Conserver la destination YARP
`http://192.168.100.218:80`. L'[exemple Traefik](../backend/deploy/gateway/traefik/realm-sites.dynamic.example.yaml)
propose aussi des routeurs dédiés pour les installations qui en ont besoin.

La portée partagée des cookies Admin demande une première reconnexion après
le rollout. Les secrets, PVC, workloads, images épinglées et règles réseau
existants sont conservés.

## Vérification

Le rendu et la configuration du profil actuel se vérifient hors cluster :

```powershell
python backend/deploy/gitops/admin/Validate-PlayerManagement.py
python backend/deploy/gitops/Test-Manifests.py --require-digests
kubectl kustomize backend/deploy/gitops/admin
```

Les tests Realms vérifient le DTO public, les brouillons, les observations
fraîches/anciennes et leur persistance. Les tests Gateway vérifient les
méthodes, la suppression des identifiants, les noms d'hôte et la séparation
des services. Les tests Admin exercent la connexion centrale, le retour
Discord, le partage/renouvellement/révocation des sessions, l'antiforgery et
le refus des identifiants d'un autre realm. Le site possède ses tests de
client et des scénarios navigateur avec un service simulé.

Après synchronisation, vérifier les deux niveaux de DNS et leurs certificats,
une page publique de realm connu et inconnu, la connexion Discord depuis un
realm Admin et le retour vers ce même realm. Contrôler aussi le refus d'une
mutation visant un autre realm et le maintien des routes API/WSS existantes.
Ces contrôles locaux ne valident pas le DNS/TLS public, une vraie autorisation
Discord ou l'admission d'un client Unity. Aucun changement Unity n'est requis.
