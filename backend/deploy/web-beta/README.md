# Services et WSS pour la bêta web

Le [contrat](../../../docs/WEB_BETA_CONTRACT.md) fixe le protocole applicatif `1` et le build `arkya-web-beta-v1`. Les services et MongoDB fonctionnent réellement en local ; aucun domaine, certificat, secret ou déploiement public n'est créé par cette tranche. Le [guide Unity](../../../docs/WEB_BETA.md) décrit le client et le dedicated.

## API et identités

Construire Characters avec `docker build -f Dockerfile.characters -t arkya-characters:local .` depuis `backend/`. Utiliser les images Identity/Gateway/Realms existantes. Déployer Characters sur le réseau privé, avec un utilisateur MongoDB limité à `arkya_characters`, et une destination HTTPS vérifiable pour Identity et Realms. Les autorités privées doivent être installées dans le magasin de confiance système du conteneur client ; ne jamais désactiver la validation TLS.

Configuration non secrète Characters, à adapter aux vraies origines et identifiants :

```json
{
  "Identity": { "BaseUrl": "https://identity.internal.example" },
  "Realms": { "BaseUrl": "https://realms.internal.example" },
  "WebClient": { "AllowedOrigins": ["https://arkya-atlas.easykrill33.chatgpt.site"] },
  "GameAccess": {
    "DedicatedServers": [{ "RealmId": "beta-web", "ServerId": "island" }]
  }
}
```

Configurer cette même origine exacte dans `WebClient:AllowedOrigins` d'**Identity et Characters**. La vitrine actuelle utilise `https://api.arkya.online` comme origine d'API publique. Dans Identity, définir `Discord:PublicBaseUrl=https://api.arkya.online` et `Discord:RedirectUri=https://api.arkya.online/v1/auth/discord/callback`, puis déclarer ce callback dans l'application Discord. Si Unity est servi depuis une origine différente ou si un domaine personnalisé remplace la vitrine, ajouter explicitement cette origine réelle aux deux listes ; aucun domaine de jeu hypothétique n'est automatiquement autorisé.

Secrets à injecter séparément : `Mongo__ConnectionString`, `Identity__ServiceKey`, `ServiceAuthentication__Key` (routes historiques), `Realms__ServiceKey`, `GameAccess__DedicatedServers__0__Key`. La dernière clé doit être unique par couple realm/serveur ; elle est fournie au dedicated concerné sous `ARKYA_DEDICATED_KEY`. Le dedicated reçoit aussi `ARKYA_CHARACTERS_URL`, `ARKYA_REALM_ID` et `ARKYA_SERVER_ID`. Ni le navigateur ni le client Unity ne reçoivent cette clé.

Déployer Identity avec la route privée `/internal/sessions/introspect-reference` avant d'activer les nouveaux Characters : elle permet de vérifier la révocation par empreinte de session sans transmettre le bearer au dedicated. Les anciens tickets/baux dépourvus de référence sont refusés à la consommation, au renouvellement et à la sauvegarde ; leur dernière position est conservée pour la réadmission. La libération de bail reste possible après révocation.

Dans Realms, `GameAccess__ServiceKey` doit correspondre à `Realms__ServiceKey` de Characters ; cette clé est distincte de la clé d'administration. Pour un serveur réellement lancé hors Kubernetes, la configuration opérateur peut inclure :

```json
{
  "GameAccess": {
    "StandaloneEntries": [{
      "RealmId": "beta-web", "DisplayName": "Bêta web", "ServerId": "island",
      "Endpoint": "wss://beta-game.arkya.online/", "Enabled": true,
      "ProtocolVersion": 1, "BuildId": "arkya-web-beta-v1"
    }]
  }
}
```

Une entrée standalone n'effectue pas de probe du processus : l'opérateur l'active après vérification. Un realm persistant de même identifiant prend priorité, avec sa readiness Kubernetes. Characters masque la readiness si la version ou l'identité du serveur n'est pas configurée. Le catalogue Realms reste privé ; le navigateur passe par `GET /v1/realms` authentifié dans Characters.

## Profil dedicated WebSocket distinct

Les profils UDP existants restent inchangés. Ajouter un profil séparé et une cible/namespace dédiée dans la configuration Realms :

```json
{
  "id": "island-web-beta", "displayName": "Île · bêta web",
  "image": "ghcr.io/OWNER/arkya-dedicated@sha256:REPLACE_WITH_PUBLISHED_DIGEST",
  "environmentSecretName": "arkya-beta-dedicated",
  "servers": [{
    "id": "island", "port": 17943, "transport": "WebSocket",
    "publicEndpoint": "wss://beta-game.arkya.online/",
    "protocolVersion": 1, "buildId": "arkya-web-beta-v1", "args": []
  }]
}
```

Le déployeur crée un Service ClusterIP **TCP** pour ce profil et transmet `ARKYA_GAME_TRANSPORT=WebSocket`, `ARKYA_GAME_PORT=17943`, `ARKYA_BUILD_ID=arkya-web-beta-v1` et les identifiants. Il fixe `ARKYA_READY_FILE=/tmp/arkya-ready` et vérifie ce fichier avant readiness ; Unity le crée seulement après terrain, collisions et écoute. L'image Linux doit fournir la commande `test` utilisée par cette probe. Le Secret de profil fournit l'URL Characters et la clé dédiée. Chaque profil de cette bêta doit contenir un seul serveur si son Secret inclut une clé propre à ce serveur. Le profil UDP reste `ARKYA_GAME_TRANSPORT=Udp`, Service UDP. Les profils sont figés à la création d'un realm ; les modifier ne transforme pas un realm existant.

Le renderer operations accepte `charactersNamespace` facultatif. Si renseigné, il injecte `GameAccess__ServiceKey` depuis `arkya-operations-secrets/realms-catalog-key` et autorise seulement les pods `app.kubernetes.io/name=arkya-characters` de ce namespace vers Realms. Sans cette option, la politique historique Admin seulement reste intacte.

## Entrée WSS et frontières réseau

Publier un domaine dédié au socket de jeu avec certificat public valide. Le proxy TLS doit router WebSocket HTTP vers le Service TCP du dedicated et conserver l'upgrade. Le chemin WSS configuré doit correspondre au listener ; `/` est le choix de cette bêta. Un WSS `https://` substitué, une IP client choisie librement ou une URL avec jeton ne sont pas des destinations admises.

Exemple Traefik **dans le cluster**, à compléter avec le véritable nom de Service créé pour ce realm :

```yaml
apiVersion: traefik.io/v1alpha1
kind: IngressRoute
metadata:
  name: arkya-beta-game
  namespace: arkya-realm-beta-web
spec:
  entryPoints: [websecure]
  routes:
    - match: Host(`beta-game.arkya.online`)
      kind: Rule
      services:
        - name: REPLACE_WITH_DEDICATED_SERVICE
          port: 17943
          scheme: http
  tls:
    secretName: arkya-beta-game-tls
```

Le Traefik externe existant sur une autre VM ne peut pas atteindre un ClusterIP sans route réseau : utiliser un ingress du cluster joignable depuis cette VM ou un point d'entrée privé approuvé. Ne pas publier Characters `/internal/game`, Realms ou Mongo sur le domaine de jeu. La gateway HTTP publique expose uniquement les routes explicitement autorisées, dont `/v1/realms` et `/v1/characters/{id}/admissions` lorsque Characters est activé.

Restreindre l'accès au TCP dedicated au proxy et l'accès aux routes privées Characters aux dédiés approuvés ; autoriser Characters → Identity/Realms et Mongo selon les besoins. Installer les secrets et le certificat avant activation. La readiness Kubernetes indique l'état du workload ; elle ne remplace pas un essai navigateur WSS, admission, déplacement, sauvegarde, reconnexion et rejet d'un ticket rejoué. La fixture locale décrite dans [backend/README](../../README.md#fixture-locale-avec-les-vrais-services) couvre les vrais services et Mongo, mais utilise WS loopback en Development et ne valide pas l'OAuth public.
