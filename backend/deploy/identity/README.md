# Identity sur identity.arkya.online

**Entrée publique actuelle : [Gateway YARP sur api.arkya.online](../gateway/README.md).** Pour cette topologie, copier `cluster.gateway.example.json` : Identity devient ClusterIP sur 8080, sans Ingress ni LoadBalancer, avec le callback Discord sur `api.arkya.online`. La plage `10.42.0.0/16` de l’exemple est le réseau de pods habituel de k3s, à vérifier pour le cluster ; seuls les pods gateway du même namespace sont autorisés par la NetworkPolicy. Les profils ci-dessous décrivent l’ancienne exposition directe.

Ce dossier prépare le déploiement d’Identity et l’activation de la connexion Discord de la vitrine. Le domaine est acheté chez Gandi. Ces fichiers ne modifient ni le DNS, ni le cluster, ni l’application Discord tant que les commandes de déploiement ne sont pas exécutées.

**Pour le montage confirmé — Traefik externe avec certificat, puis `192.168.100.218:80` sur k3s — suivre le [profil Traefik externe](EXTERNAL_TRAEFIK.md).** Les étapes Ingress/cert-manager ci-dessous concernent uniquement le profil alternatif où l’Ingress est dans Kubernetes. La construction de l’image, les secrets et l’activation du site sont communs aux deux profils.

## Contenu

- `base/` : Namespace `arkya-identity`, Deployment, ConfigMap, Service, Ingress TLS, NetworkPolicy et PVC pour les clés Data Protection.
- `cluster.example.json` : paramètres propres au cluster ; `Render-Manifests.ps1` produit un overlay Kustomize et un manifeste complet dans `.local/production/`.
- `secrets.env.example` : noms des quatre valeurs requises, sans secret réel.
- `Test-Deployment.ps1` : contrôles HTTPS/CORS/connexion MongoDB, préparation facultative de la configuration du site.
- `../../Dockerfile.identity` : image Linux .NET 10, non-root, bases Microsoft épinglées par digest.

Le périmètre est la connexion du site. MongoDB doit déjà être disponible ; il n’est pas créé avec un volume éphémère par ces manifests. Les API internes, le Social SDK et les sondes détaillées ne sont pas exposés par cet Ingress.

## 1. Construire l’image

Le workflow [GitHub Actions → GHCR](GITHUB_ACTIONS.md) automatise les tests, le build Linux AMD64/ARM64 et la publication de l’image. Son résumé fournit le digest à reporter dans `cluster.json`. Pour construire et pousser manuellement :

Depuis la racine du dépôt, avec Docker Linux démarré et une session ouverte sur votre registre :

```powershell
$identityImage = 'REGISTRE/arkya/identity:VERSION'
docker buildx build --platform linux/amd64 --push -f backend/Dockerfile.identity -t $identityImage backend
```

Adapter l’architecture aux nœuds (`linux/arm64`, ou un build multiarchitecture). Utiliser de préférence le digest de l’image poussée dans `cluster.json`. Les tags de release doivent rester immuables. Actualiser régulièrement les deux digests Microsoft du Dockerfile pour intégrer les correctifs .NET/Ubuntu.

## 2. Renseigner le cluster

Prérequis : `kubectl` avec Kustomize intégré, PowerShell 7.2+, contrôleur Ingress, CNI appliquant les NetworkPolicies et StorageClass compatible avec les volumes RWO. Le déploiement utilise un seul réplica avec `Recreate` pour éviter le montage concurrent du volume de clés : une mise à jour entraîne une courte interruption. Passer à plusieurs réplicas demande un trousseau partagé compatible et une stratégie de mise à jour adaptée.

Depuis ce dossier :

```powershell
New-Item -ItemType Directory -Path .local -Force
Copy-Item cluster.example.json .local/cluster.json
```

Renseigner les valeurs réelles dans `.local/cluster.json` :

| Paramètre | Valeur attendue |
| --- | --- |
| `image` | Référence du registre avec digest ou tag de release |
| `ingressClass` | Nom de la classe Ingress installée |
| `ingressNamespace` | Namespace des pods du contrôleur Ingress |
| `ingressPodLabels` | Labels identifiant exclusivement les pods de ce contrôleur |
| `trustedProxyNetworks` | CIDR des adresses sources de l’Ingress vues par Identity ; utiliser `/32` ou `/128` pour une adresse fixe |
| `storageClass` | StorageClass du PVC de clés (1 Gio, RWO) |
| `clusterIssuer` | Nom du ClusterIssuer cert-manager existant, ou chaîne vide pour un certificat géré autrement |
| `tlsSecretName` | Secret TLS dans `arkya-identity`, par défaut `arkya-identity-tls` |
| `imagePullSecretName` | Secret d’accès au registre dans ce namespace, ou chaîne vide pour une image publique |
| `ingressAnnotations` | Annotations propres au contrôleur, si nécessaires |

Lire les valeurs sans modifier le cluster :

```powershell
$identityContext = 'CONTEXTE_KUBERNETES'
kubectl --context $identityContext get ingressclass
kubectl --context $identityContext get clusterissuer
kubectl --context $identityContext get storageclass
kubectl --context $identityContext -n NAMESPACE_INGRESS get pods --show-labels -o wide
```

Le proxy doit remplacer correctement `X-Forwarded-Proto` et transmettre l’IP client avec `X-Forwarded-For`. Identity ne fait confiance qu’aux réseaux renseignés, traite un seul saut et ignore `X-Forwarded-Host`. Ne pas mettre `0.0.0.0/0`, `::/0` ni activer `ASPNETCORE_FORWARDEDHEADERS_ENABLED`. La confiance IP doit correspondre à la source réelle après éventuel SNAT. Le filtre réseau et les CIDR se complètent ; un contrôleur en `hostNetwork` ou un CNI avec SNAT peut demander une règle `ipBlock` spécifique à la place des sélecteurs de pods. Ne pas élargir cette règle à tous les pods pour contourner un échec.

Configurer la redirection HTTP→HTTPS dans le contrôleur si souhaitée. Même sans redirection, l’application refuse les appels API HTTP non sécurisés. Désactiver ou expurger les journaux d’accès de ce routage : le callback OAuth contient un code dans sa query string. Ne jamais journaliser Authorization, les corps d’échange ou les secrets. Les annotations varient selon le contrôleur ; aucun réglage NGINX/Traefik n’est supposé par le manifeste générique.

```powershell
./Render-Manifests.ps1 -Configuration .local/cluster.json
kubectl kustomize .local/production
```

Le rendu est entièrement local. Les valeurs d’exemple sont refusées et la base seule ne doit pas être appliquée. La ConfigMap porte un hash : modifier la configuration et rendre à nouveau provoque un changement du PodTemplate. Aucun secret n’est inclus dans ce rendu.

## 3. DNS, certificat et Discord

Dans Gandi, créer l’enregistrement `identity` pointant vers **l’entrée publique de l’Ingress**, pas vers l’API Kubernetes. Employer A/AAAA ou CNAME selon l’adresse fournie par le contrôleur. Ajouter un AAAA seulement si cette entrée fonctionne en IPv6.

Avec cert-manager, le rendu ajoute `cert-manager.io/cluster-issuer` et référence le Secret TLS ; le ClusterIssuer doit déjà être prêt, avec son challenge ACME fonctionnel. Sans cert-manager, provisionner le Secret TLS indiqué dans le namespace avant ouverture. Ne pas committer les clés privées.

Dans [Discord Developer Portal](https://discord.com/developers/applications), application en client confidentiel :

| Champ | Valeur |
| --- | --- |
| OAuth2 Redirect URI | `https://identity.arkya.online/v1/auth/discord/callback` |
| Terms of Service URL | `https://arkya-atlas.easykrill33.chatgpt.site/terms.html` |
| Privacy Policy URL | `https://arkya-atlas.easykrill33.chatgpt.site/privacy.html` |

La connexion utilise seulement `identify`. Conserver les éventuels callbacks du client Unity existant. Le client secret reste dans Identity.

## 4. Secrets et application

Créer d’abord le namespace (une seule fois) :

```powershell
kubectl --context $identityContext apply -f base/namespace.yaml
Copy-Item secrets.env.example .local/identity.env
```

Remplir `.local/identity.env` dans un éditeur local. Le dossier `.local` est ignoré par Git. Utiliser un compte MongoDB limité à `arkya_identity`, capable de créer les index et de lire/écrire les collections. Encoder les caractères réservés du login/mot de passe dans l’URI et adapter TLS/authSource à l’installation. Renseigner le Client ID et le Client Secret Discord, puis une clé de service aléatoire d’au moins 32 caractères. Ne pas copier ces valeurs dans une conversation, une commande `--from-literal` ou un manifest versionné.

```powershell
kubectl --context $identityContext -n arkya-identity create secret generic arkya-identity-secrets --from-env-file=.local/identity.env
```

Pour un registre privé, créer aussi le Secret `kubernetes.io/dockerconfigjson` indiqué dans `imagePullSecretName` avec le gestionnaire de secrets habituel. Le script de rendu n’inclut aucun identifiant de registre. Les secrets Kubernetes doivent être protégés par les droits d’accès et le chiffrement du cluster ; le PVC contient aussi des clés sensibles. Une rotation des variables secrètes nécessite un redémarrage des pods.

Après lecture du manifeste, valider sur le serveur puis appliquer :

```powershell
kubectl --context $identityContext apply --dry-run=server -f .local/production/identity.yaml
kubectl --context $identityContext apply -f .local/production/identity.yaml
kubectl --context $identityContext -n arkya-identity rollout status deployment/arkya-identity --timeout=300s
kubectl --context $identityContext -n arkya-identity get pods,svc,ingress,pvc
```

`/health/live` et `/health/ready` répondent uniquement sur le port de gestion 8081. Les sondes envoient le Host attendu ; la readiness teste MongoDB, la liveness ne dépend pas de MongoDB. Le Service expose seulement 8080. Les NetworkPolicies n’autorisent que les pods Ingress vers 8080 ; le trafic local du kubelet vers les sondes reste géré par le CNI. Les sorties ne sont pas filtrées dans cette base pour permettre MongoDB, DNS et Discord HTTPS ; les adapter aux règles d’egress du cluster si nécessaire.

En cas de déploiement bloqué : vérifier les événements, le PVC, l’accès au registre et les logs de l’application. Un `400` derrière HTTPS indique notamment un Host ou une source proxy non reconnu. Une readiness `503` indique une dépendance MongoDB indisponible. Ne pas désactiver la vérification TLS ou la restriction aux proxies pour masquer le problème.

## 5. Activer la connexion du site

Après DNS, certificat et rollout réussis :

```powershell
./Test-Deployment.ps1 -PrepareWebsiteConfig
```

Ce contrôle valide le certificat, les routes publiques, CORS, l’absence d’accès aux routes internes et un challenge en attente enregistré dans MongoDB. Il ne connecte aucun compte Discord ; le challenge de test expire après cinq minutes. En cas de réussite, `.local/auth-config.js` contient l’origine de production. La commande ne modifie pas encore le site public.

Mettre à jour les notices FR/EN de [confidentialité](../../../sites/arkya/dist/privacy.html) et les traductions pour annoncer l’activation réelle, le prestataire et le lieu d’hébergement retenus. Puis, depuis ce dossier :

```powershell
Copy-Item .local/auth-config.js ../../../sites/arkya/dist/auth-config.js
```

Publier ensuite la vitrine via le même projet Sites, en conservant son accès public. Le [guide web](../../../sites/arkya/DISCORD_SETUP.md) décrit le fonctionnement de la session. Aucune clé Discord ou MongoDB n’entre dans le frontend.

Faire enfin un parcours réel : autoriser son compte Discord, revenir connecté, recharger, naviguer vers les classes puis se déconnecter. Tester aussi un refus. La session dure huit heures au maximum. Un succès du précontrôle ne prouve pas que Discord accepte le Client ID/secret et le callback : seul ce parcours le confirme.

## Références et limites de validation

Vérification du 16 octobre 2026 : compilation/publication Release `linux-x64` réussie ; 144 tests Identity réussis, trois tests de persistance ignorés faute de `ARKYA_TEST_MONGO`. Les sept ressources rendues passent les schémas Kubernetes 1.36.1 en mode strict, avec 23 assertions d’intégration. Le renderer refuse aussi les configurations d’exemple, le tag `latest`, les sélecteurs manquants et les réseaux de confiance universels. Scripts et résultats de contrôle : `output/identity-manifest-validation/` et `output/identity-render-guards.ps1` à la racine du dépôt. Les manifests de test avec image fictive ont été retirés de `.local/production` après validation.

L’API du contexte Kubernetes local ne répondait pas pendant la préparation et le daemon Docker Linux était arrêté : aucun build d’image complet, `dry-run` serveur, déploiement ou OAuth Discord réel n’a été exécuté. La vitrine reste dans son état actuel « Connexion en préparation » jusqu’au raccordement décrit ci-dessus.

Les choix suivent les documentations [Ingress Kubernetes](https://kubernetes.io/docs/concepts/services-networking/ingress/), [sondes Kubernetes](https://kubernetes.io/docs/tasks/configure-pod-container/configure-liveness-readiness-probes/), [cert-manager Ingress](https://cert-manager.io/docs/usage/ingress/) et [proxies ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-10.0). Aucune ressource cluster, aucun DNS et aucun paramètre Discord ne sont créés par la préparation des fichiers. Le build d’image, la validation serveur, les volumes et le parcours OAuth doivent être vérifiés sur l’infrastructure disponible.
