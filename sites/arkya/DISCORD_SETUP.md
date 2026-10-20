# Activer Discord sur la vitrine Arkya

La vitrine est publique et bilingue. Le parcours de connexion et le lancement du client Unity sont raccordés dans le frontend : `dist/auth-config.js` désigne les vrais services. Cette configuration ne garantit pas leur disponibilité. L’entrée publique prévue est **`https://api.arkya.online`**, portée par une gateway YARP qui transmet les requêtes à Identity dans k3s. Le domaine `arkya.online` est acheté chez Gandi ; le Traefik externe gérera HTTPS et le certificat.

Les manifests GitOps référencent maintenant les images par digest. Cela ne confirme pas leur disponibilité ni leur déploiement : l’état réel du cluster, HTTPS, la configuration Discord et une connexion réelle restent à vérifier. Cette configuration du site n’active aucun de ces services.

## Déploiement progressif — étape 1 : frontend et connexion Discord

La cible est `https://arkya.online` avec l’API `https://api.arkya.online`.
Cette étape utilise uniquement le frontend, Gateway et Identity, ainsi que
la base MongoDB d’Identity. Les personnages, le backoffice et les players
Unity appartiennent aux étapes suivantes. Le frontend peut démarrer sans
le service Unity ; le lancement du jeu reste indisponible dans ce périmètre.

Utiliser le [profil frontend-discord](../../backend/deploy/gitops/frontend-discord/README.md),
inspiré du dépôt `arksaas-infra` : deux LoadBalancers privés pour frontend et
Gateway, Identity en ClusterIP, une seule source Argo. Traefik existe déjà
et reste hors du périmètre de ce déploiement.

Relever d’abord l’état des ressources depuis le contexte du cluster Arkya :

```bash
kubectl config current-context
kubectl -n argocd get applications
kubectl -n arkya-identity get deployments,pods,services,ingresses,pvc
kubectl get services -A -o wide
```

Les prérequis du profil existant sont, dans `arkya-identity` :

- `arkya-identity-secrets`, avec `Mongo__ConnectionString`, `Discord__ClientId`,
  `Discord__ClientSecret` et `ServiceAuthentication__Key` ;
- `arkya-ghcr` pour les images privées ;
- `arkya-identity-tls` pour le listener HTTPS interne déclaré dans Identity ;
- le PVC `arkya-identity-keys` pour les clés persistantes, créé par le profil.

Les trois Deployments ont également leurs ConfigMaps, Services et
NetworkPolicies. La Gateway conserve le LoadBalancer `192.168.100.218` ;
l'adresse privée du frontend est allouée par le cluster ou réservée après
vérification de disponibilité. Ce profil ne crée aucun ingress ni certificat
public. Le parent Argo doit utiliser `bootstrap-frontend-discord`, qui
configure la même Application `arkya-beta` ; suivre la procédure du profil
avant le sync pour éviter une seconde propriété ou un déploiement complet.

Le callback à enregistrer dans Discord est exactement
`https://api.arkya.online/v1/auth/discord/callback`.

Depuis la racine du dépôt, vérifier l’origine réellement utilisée :

```powershell
./backend/deploy/identity/Test-Deployment.ps1 -Origin https://api.arkya.online -SiteOrigin https://arkya.online
```

Ce contrôle vérifie HTTPS, CORS, les routes publiques/privées, puis crée un
challenge temporaire MongoDB et vérifie son polling. Il ne connecte aucun
compte Discord. `dist/auth-config.js` pointe déjà vers la Gateway : aucune
régénération de ce fichier n’est nécessaire pour cette étape. Le paramètre
`-SiteOrigin` conserve par défaut l’ancienne origine Sites pour les commandes
historiques.

Le critère de fin est une connexion Discord réelle depuis
`https://arkya.online/account.html`, la restauration du compte après
rechargement, puis la déconnexion avec refus de l’ancienne session.

**Constat du 20 octobre 2026 depuis le poste de travail :** les deux domaines
résolvent vers `77.42.49.61`, mais présentent `CN=TRAEFIK DEFAULT CERT`.
La validation TLS refuse le nom et la chaîne du certificat. L’API Kubernetes
du contexte local à `https://5.135.64.45:6443` ne répond pas ; aucun état
des workloads n’a donc pu être confirmé et aucune ressource distante n’a été
modifiée. Les 92 tests Node du frontend passent, sans valider la connexion
Discord publique.

## URL à renseigner dans Discord dès maintenant

Dans les informations générales de l’application du [portail développeur Discord](https://discord.com/developers/applications) :

| Champ | URL |
| --- | --- |
| Terms of Service URL | `https://arkya-atlas.easykrill33.chatgpt.site/terms.html` |
| Privacy Policy URL | `https://arkya-atlas.easykrill33.chatgpt.site/privacy.html` |

Les deux pages proposent FR/EN. Ajouter `?lang=fr` ou `?lang=en` pour imposer une langue. L’hébergeur peut rediriger vers le même chemin sans `.html`. Le site est consultable sans compte ChatGPT. Les coordonnées utilisées sont **Pyxion Sarl-S** et **support@arkya.online**.

La page de compte est `https://arkya-atlas.easykrill33.chatgpt.site/account.html`. Elle tente la connexion réelle et affiche une erreur si le service est inaccessible. Les URL en `arkya.online` ne doivent remplacer celles-ci qu’une fois le domaine réellement raccordé et accessible.

## Déployer Gateway et Identity sur k3s

Suivre le [guide Gateway YARP](../../backend/deploy/gateway/README.md) pour la configuration exacte, les manifests et leur ordre d’application. Le [README backend](../../backend/README.md) décrit les contrats, index, secrets et contraintes des services.

1. Publier les images .NET 10 avec les workflows [Identity image](../../backend/deploy/identity/GITHUB_ACTIONS.md) et [Gateway image](../../backend/deploy/gateway/GITHUB_ACTIONS.md). Ils prévoient tests, build Linux AMD64/ARM64 et publication GHCR sur la branche par défaut. Copier leurs digests dans les configurations de déploiement ; ils ne déploient pas le cluster.
2. Déployer Identity avec MongoDB, ses secrets et son volume de clés, en utilisant le **profil `gateway`** et [cluster.gateway.example.json](../../backend/deploy/identity/cluster.gateway.example.json). Son Service devient interne, en ClusterIP. Déployer ensuite Gateway avec son Service LoadBalancer.
3. Créer chez Gandi l’enregistrement `api.arkya.online` vers l’adresse publique du Traefik externe, puis configurer son routeur HTTPS vers la gateway et son certificat. YARP transmet les routes publiques `/v1/auth/...` à Identity ; le navigateur conserve l’origine `api.arkya.online`.

Les profils directs `identity.arkya.online` restent disponibles pour une installation sans gateway, mais ne correspondent plus au raccordement prévu ici. En cas de déploiement direct déjà existant, suivre les instructions de bascule du guide Gateway avant de réattribuer son IP. Les routes Characters restent désactivées jusqu’au déploiement et à la configuration explicite de ce sous-service ; elles ne sont pas nécessaires à la connexion du site.

La terminaison TLS doit être reconnue à chaque saut : sources Traefik autorisées par Gateway, puis pods Gateway autorisés par Identity. Les profils définissent les réseaux de confiance et les NetworkPolicies ; adapter leurs hypothèses au cluster, sans confiance globale dans des en-têtes arbitraires. Les routes internes et d’administration restent inaccessibles depuis l’entrée publique.

Paramètres non secrets d’Identity (variables d’environnement .NET) :

```text
ASPNETCORE_ENVIRONMENT=Production
AllowedHosts=api.arkya.online
Mongo__DatabaseName=arkya_identity
Discord__PublicBaseUrl=https://api.arkya.online
Discord__RedirectUri=https://api.arkya.online/v1/auth/discord/callback
Sessions__Lifetime=08:00:00
WebClient__AllowedOrigins__0=https://arkya-atlas.easykrill33.chatgpt.site
```

Fournir `Mongo__ConnectionString`, `Discord__ClientId`, `Discord__ClientSecret` et `ServiceAuthentication__Key` par la gestion des secrets du cluster. Aucun secret ne va dans le site statique, Git ou une URL. L’utilisateur MongoDB doit être limité à sa base. Préserver et partager le trousseau Data Protection entre réplicas si l’autorisation Social SDK est utilisée. Les journaux/proxies ne doivent pas enregistrer les jetons, en-têtes Authorization, corps d’échange ou secrets OAuth.

Dans la section OAuth2 de l’application Discord, enregistrer exactement :

```text
https://api.arkya.online/v1/auth/discord/callback
```

Adapter toutes ces valeurs si l’origine retenue diffère. Garder l’application Discord en client confidentiel. La connexion du site utilise uniquement `identify` ; elle ne demande ni e-mail, ni accès aux serveurs/messages Discord, ni installation d’un bot.

`WebClient:AllowedOrigins` accepte des origines HTTPS exactes, sans slash final, chemin ou joker. Si la vitrine sert ensuite réellement `https://arkya.online`, ajouter cette origine séparément. Ajouter `https://www.arkya.online` uniquement si ce nom sert aussi la vitrine. Ce réglage CORS ne configure pas les DNS. Le backend expose uniquement les méthodes et en-têtes nécessaires aux routes publiques de connexion, sans cookies interorigine.

## Raccorder et publier le frontend

Après déploiement, exécuter depuis `backend/deploy/gateway` :

```powershell
../identity/Test-Deployment.ps1 -Origin https://api.arkya.online -PrepareWebsiteConfig
```

Ce contrôle vérifie HTTPS, le CORS, les routes privées et le début du parcours Discord. Après réussite, il prépare `backend/deploy/identity/.local/auth-config.js`. Une gateway répondant à `/health` ne prouve pas à elle seule la disponibilité d’Identity ou de MongoDB.

La configuration publique de cette révision dans `dist/auth-config.js` est :

```js
window.ArkyaAuthConfig = Object.freeze({
  identityBaseUrl: 'https://api.arkya.online',
  charactersBaseUrl: 'https://api.arkya.online',
});
```

Le nom de la propriété `identityBaseUrl` reste inchangé : sa valeur désigne désormais l’origine publique de la gateway, sans chemin ni slash final. Le navigateur ouvre l’autorisation dans un autre onglet, échange la preuve S256 avec Identity à travers YARP puis valide la session via `/v1/auth/me`. Le secret et les échanges Discord restent sur le backend. Une session Arkya dure au maximum huit heures pour ce client web ; conserver `Sessions__Lifetime=08:00:00` ou une durée inférieure. La session n’est pas partagée entre origines de vitrine.

Au moment de l’activation, mettre à jour les états FR/EN de la page d’avancement et les textes de confidentialité qui annoncent la connexion future, puis renseigner le prestataire, le lieu d’hébergement et les transferts éventuels du backend réellement retenu. Prévoir le traitement des demandes d’accès/suppression reçues sur support@arkya.online : aucune suppression automatique de compte n’est implémentée. Republier via le même projet Sites, en préservant l’accès public, puis vérifier le parcours réel ci-dessous. Si cette vérification échoue, ne pas annoncer la bêta disponible ; conserver l’erreur explicite ou désactiver volontairement l’origine après décision de l’exploitant.

## Vérifier l’activation réelle

1. Depuis une fenêtre sans session ChatGPT, ouvrir la vitrine et les deux pages juridiques.
2. Ouvrir la page de compte et lancer Discord. Vérifier l’application affichée, autoriser avec son propre compte puis constater le retour connecté sur la vitrine.
3. Recharger et naviguer vers les classes : le nom doit être restauré après validation serveur. Aucun jeton ne doit apparaître dans les URL.
4. Se déconnecter ; l’ancienne session doit être refusée par `/v1/auth/me`. Tester aussi le refus Discord et l’annulation depuis la page de compte.
5. Vérifier les deux langues et le comportement lorsque le backend est temporairement indisponible.

Les tests déjà exécutés utilisent un service Identity simulé pour le navigateur. Ils ne remplacent pas ce passage réel une fois le backend et l’application Discord configurés.


## Lancement Unity après connexion

La navigation Jouer ouvre `play.html`. Sans compte vérifié, elle propose `account.html?returnTo=play.html` ; les autres valeurs de retour sont ignorées. Après connexion, la page revient au jeu dans le même onglet, puis vérifie `/me` avant tout chargement Unity. Le bearer Arkya est remis en mémoire au code Unity premier parti via le callback étroit `withGameSession`, jamais dans le DOM, un événement, une URL ou une variable globale persistante. Unity vérifie à nouveau la session et conserve la création/sélection du personnage.

Activer les routes publiques Characters et Realms/admission sur YARP pour cette bêta, puis configurer leur CORS pour la même origine exacte que le site. Autoriser seulement les méthodes/en-têtes nécessaires (`Authorization`, `Content-Type`) ; le frontend n’envoie pas de cookies interorigine. Le cookie OAuth de corrélation reste Secure/HttpOnly/SameSite=Lax et limité au callback Discord. Aucun secret OAuth ou clé interservice ne doit entrer dans le build web.

Le build réel sous `dist/game/`, son manifest et le dedicated WSS doivent respecter le [contrat de bêta web](../../docs/WEB_BETA_CONTRACT.md). Le [README](README.md#jouer-dans-le-navigateur) décrit le manifest et la compression retenue. Sites héberge les fichiers du client ; le dedicated est déployé séparément et décide de l’admission.

Compléter le contrôle réel avec : connexion Discord → retour à Jouer → import de session validé par Unity → création/sélection persistante → admission au dedicated → mouvement confirmé par le serveur. Tester également expiration, déconnexion depuis Unity/site, refus `/me`, panne réseau, rechargement et retour Précédent/Suivant. Le shell envoie `ClearSiteSession` puis `Quit`, masque le canvas et n’effectue aucun démarrage local de secours.

**État de vérification de cette révision :** tests Node du gate et du bridge exécutés séparément ; vraie connexion Discord, CORS public et admission WSS publique restent à confirmer. Le contrôle du 18 octobre 2026 à 09:34 UTC confirme que `api.arkya.online` résout vers `77.42.49.61` et que HTTP redirige vers HTTPS. Le contrôle TLS à 10:33 UTC refuse encore le nom et la chaîne du certificat (`RemoteCertificateNameMismatch`, `RemoteCertificateChainErrors`). Corriger le certificat présenté par Traefik avant les essais publics ; ne pas désactiver sa validation. Les résultats de l'intégration locale avec les vrais services sont consignés dans le [guide bêta web](../../docs/WEB_BETA.md) et restent distincts d'une connexion Discord de production.
