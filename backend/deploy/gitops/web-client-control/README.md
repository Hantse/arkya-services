# Client Unity statique de la bêta

Ce composant ajoute uniquement le Deployment, le Service ClusterIP et la
NetworkPolicy **`arkya-web-client`** dans le namespace existant **`arkya-beta`**.
Il réutilise le Secret de registre `arkya-ghcr`, déjà provisionné dans ce
namespace. Il ne crée aucun namespace, Secret ou serveur de jeu dédié et ne
modifie pas les droits Argo CD.

Le client WebGL téléchargeable est distinct du realm. Un dedicated prêt
ne fournit ni `build-config.json`, ni le loader, ni les fichiers du navigateur.
Le frontend relaie `/game/` vers
`arkya-web-client.arkya-beta.svc.cluster.local:8080`, en conservant le préfixe.
Son Deployment configure cette destination avec `ARKYA_UNITY_UPSTREAM`.
La NetworkPolicy autorise uniquement les pods `arkya-frontend` du namespace
`arkya-beta` sur TCP 8080. Le Service n'est pas exposé directement au public.

Le profil Admin inclut ce composant pour conserver une seule source Argo.
Les anciens profils et la variante Unity dans `arkya-realm-beta-web` restent
indépendants ; ce composant ne doit pas être ajouté à cette ancienne variante.

## Image et mises à jour

`kustomization.yaml` sélectionne explicitement l'image Unity déjà publiée
`ghcr.io/hantse/arkya-mmorpg-web-client`, par digest immuable
`sha256:efdb1cc439cb93324015693e8204a08c0428382a3a29d17e522631ced3d80e83`.
L'annotation `arkya.online/source-revision` conserve la révision associée dans
le manifest Unity promu : `ef90ae8fae56047807e3b30cc79f179c66af3465`.
Ce composant n'effectue aucun build Unity.

La CI des services ne promeut pas cette image. Après une livraison Unity
vérifiée, actualiser volontairement le digest, les annotations de révision
du Deployment et du pod, ainsi que les valeurs attendues du validateur.
Conserver le protocole et le `buildId` compatibles avec le dedicated et
Characters ; le démarrage Nginx ne valide pas l'admission en jeu.

La [livraison Unity du 21 octobre](https://github.com/Hantse/arkya-mmorpg/actions/runs/39619856892)
a validé les tests, les trois builds et la santé puis l'arrêt des deux
conteneurs avant la promotion `ba8fdb2`. Elle conserve le nouvel atelier
« Un visage. Une histoire. » et l'île actuelle, et corrige l'arrivée à
l'origine océanique lorsque l'état sauvegardé est absent ou déjà corrompu.
Le profil Admin sélectionne le dedicated associé,
`sha256:a8fed3c9eb50312f970989f93af8c23c19881ab4d833f45197cfd1e931cbe74c`.
L'intention `Deployment:ImageReleases` de version `2` autorise uniquement
la transition du realm `beta-web` depuis le dedicated `7da7ef5` de la
release précédente ;
voir le [guide Realms](../../../../docs/REALM_DEPLOYMENT.md).

## Vérification

```powershell
python backend/deploy/gitops/web-client-control/Validate-Manifests.py
kubectl kustomize backend/deploy/gitops/admin
```

Le validateur rend le composant avec Kustomize sans contacter le cluster,
puis contrôle ses trois ressources, l'image épinglée, les sondes, le
confinement du conteneur, le Service et l'entrée réseau limitée au frontend.
Après synchronisation, vérifier publiquement `/game/build-config.json`
(HTTP 200 et type JSON), les fichiers qu'il référence, puis le vrai chargement
WebGL et l'admission WSS. Ces contrôles publics restent distincts du rendu
hors cluster et de la santé du realm.

## Vérification publique — 21 octobre 2026

La [CI de la correction](https://github.com/Hantse/arkya-services/actions/runs/39583358091)
a réussi, y compris le test Docker du proxy, puis promu les images dans
`8d115ed`. À 12:22 (Bruxelles), le manifest public est passé de 502 à 200 JSON.
Les quatre payloads répondent 200 avec leurs types attendus, sans en-tête
de compression ajouté. Le vrai WebGL ouvert sur `/game/index.html` affiche
la création de personnage ; aucune erreur console relevée, avec le seul
avertissement FSR déjà connu. Cette vérification sans session joueur ne
couvre pas la connexion Discord ni l'admission WSS dans le realm.
Elle concerne la version publiée `3c4cc6e`, antérieure à l'atelier de
personnage et à l'île présents dans le checkout local du 21 octobre.

À 15:19 (Bruxelles), après la sélection `92d05d6` et la promotion des
services `76e83b5`, le manifest public expose bien la source `fd77651`.
Les quatre payloads répondent HTTP 200 ; les fichiers compressés
`.unityweb` restent binaires, sans `Content-Encoding` HTTP ajouté.
Le vrai WebGL affiche le nouvel atelier, le personnage actuel et les
palettes magenta/jade. Aucun message console de niveau erreur ; le seul
avertissement concerne encore la passe FSR inutilisée.
Ce chargement autonome sans session ne valide pas l'admission Discord.

Le contrôle WSS a ensuite révélé un HTTP 400 de la gateway. Le correctif
`ca2a3d3`, vérifié par 176 tests Gateway et la
[CI des services](https://github.com/Hantse/arkya-services/actions/runs/39608548550),
normalise le schéma de confiance `wss` avant la garde HTTPS.
Après la promotion `dba05d4`, la route publique répond **101 Switching
Protocols** à 15:38, puis le client standard .NET confirme l'état `Open`
à 15:39. Ces probes vérifient TLS et l'upgrade, sans envoyer de ticket ou
d'identité joueur. Le digest effectif du realm et l'admission avec un
compte Discord restent non attestés faute de session Admin/joueur.
