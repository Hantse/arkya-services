# Entrée du personnage et dernier royaume

Le launcher installe le client Windows et effectue la connexion Discord avant
de le lancer. Unity revalide la session Arkya reçue, puis consulte Characters
pour choisir l’écran d’entrée. Les personnages restent liés à leur royaume ;
ce parcours ne transfère aucun personnage.

## Session avant l’entrée

Le launcher conserve le bearer Arkya en mémoire et vérifie `/v1/auth/me` après
la connexion puis avant le démarrage. La remise au jeu utilise un pipe nommé
Windows local à usage unique, limité à l’utilisateur courant et au PID lancé.
Seul son nom passe dans l’environnement ; le bearer n’est écrit ni dans les
arguments de lancement ni dans un fichier. Le contrat et les délais bornés
sont décrits dans le [guide de création](CHARACTER_CREATION.md#connexion-et-sauvegarde-serveur).

La sélection et la création restent masquées jusqu’à la réussite de `/v1/auth/me`
dans Unity, sur l’origine Identity intégrée au build. Aucune URL reçue du pipe
ne configure les services. Le client vérifie le compte
et retient la plus courte expiration. Sans session valide ou après expiration,
ces vues restent masquées ou sont masquées de nouveau. Le joueur est invité à
quitter le jeu puis à se reconnecter dans le launcher. Ce contrôle d’entrée ne
remplace pas la validation des requêtes et des admissions par les services.

L’Editor conserve l’aperçu artistique et l’essai local sans compte. Le parcours
WebGL conserve la session remise par le site et sa revalidation dans Unity.
Ces chemins ne permettent pas d’admettre un personnage persistant sans les
contrôles serveur décrits ci-dessous.

## Contrat Characters

`GET /v1/characters/entry-context` exige la session Arkya `Authorization: Bearer`.
La gateway expose uniquement `GET` et le préflight `OPTIONS` lorsque Characters
est activé. Le service retourne `Cache-Control: no-store` et ne reçoit aucun
identifiant de compte du client.

```json
{
  "hasCharacters": true,
  "lastRealmId": "beta-web",
  "lastCharacterId": "ab1890c5-9930-4ac0-bafd-cd15a48bceca",
  "lastConnectedAt": "2026-10-23T00:00:00+00:00"
}
```

`hasCharacters` porte sur tous les personnages appartenant au compte authentifié.
Les trois champs `last...` sont `null` en l'absence d'admission enregistrée pour
un personnage encore détenu par ce compte. Cela comprend les anciens personnages
créés avant ce suivi. Un compte vide reçoit `hasCharacters: false` et trois valeurs
`null`. Un compte différent ne reçoit jamais cet historique.

Le client utilise également `GET /v1/characters` et `GET /v1/realms` :

- Sans personnage : afficher le choix de royaume avant la création.
- Avec personnage et dernier royaume disponible : revenir à la sélection des
  personnages de ce royaume, en préselectionnant `lastCharacterId` s'il existe.
- Historique absent, royaume supprimé ou indisponible : afficher le choix de
  royaume ; ne pas sélectionner silencieusement une autre destination.

Restaurer l'écran d'un royaume ne donne pas accès au monde. Le client demande
toujours une admission ; le dedicated valide le ticket et les services conservent
les contrôles de propriété, de compatibilité et d'accès bêta.

## Persistance autoritaire

`game_leases` écrit désormais `SchemaVersion = 3` et un `LastConnectedAt` nullable.
Les documents des schémas 1 et 2 restent lisibles sans migration destructive.
La compatibilité est ascendante : une ancienne version du service ne sait pas
lire les documents de schéma 3 ni leur nouveau champ. Éviter de faire servir les
admissions simultanément par une ancienne et une nouvelle version pendant une
mise en ligne ; après les premières écritures, un retour à l'ancien binaire
nécessite une adaptation de lecture ou une migration contrôlée. Le présent
changement ne modifie pas l'orchestration du déploiement.
La consommation réussie de `/internal/game/admissions/consume`, authentifiée par
la clé du dedicated concerné, écrit cet horodatage avec la lease dans le même CAS.
Il correspond à l'admission serveur, sans prétendre mesurer l'arrivée du premier
rendu sur le client. Aucun endpoint joueur ne permet de déclarer une connexion.

Émission ou remplacement d'un ticket, échec, rejeu, heartbeat, sauvegarde et
libération ne déplacent pas cet horodatage. Un échec d'écriture de la consommation
n'enregistre pas d'historique. Les leases n'ont pas de TTL destructif : position et
historique survivent à la déconnexion et au redémarrage du service.

L'index `ix_account_last_connection` porte sur `AccountId`, `LastConnectedAt`
décroissant et l'ID du personnage. La lecture filtre le compte et les IDs de ses
personnages actuels, puis retourne un seul document. Les égalités sont départagées
par ID croissant. Un personnage supprimé ne fournit plus de destination ; le
personnage possédé ayant la connexion précédente sert de repli.

## Vérification

`CharacterEntryContextTests` couvre le compte vide, les personnages historiques,
les admissions refusées/rejouées, la conservation lors des opérations de lease,
les comptes distincts, le repli après suppression, l'ordre déterministe et les
contrôles HTTP. Le test Mongo réel vérifie l'index, la lecture après redémarrage,
l'absence de TTL et la compatibilité des documents existants. Les tests Gateway
vérifient le routage vers Characters sans transmission de clé dédiée publique.

La remise de session launcher/Unity et la fermeture de l’interface disposent
de tests avec fixtures. Le parcours est implémenté, mais la connexion OAuth
Discord réelle et le parcours complet entre launcher, jeu Windows construit
et dédié n’ont pas été validés de bout en bout pour cette révision. Un nouveau
build Windows validé doit être publié pour distribuer ces changements.
