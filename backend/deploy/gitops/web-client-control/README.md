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
`sha256:e2ff0826d02716de3d9e24e386e67e83e640c94bedef72b4a2c1dcb7d00bf516`.
L'annotation `arkya.online/source-revision` conserve la révision associée dans
le manifest Unity promu : `3c4cc6e9e3887f92bc704aff0ad1ea6069bc1841`.
Ce composant n'effectue aucun build Unity.

La CI des services ne promeut pas cette image. Après une livraison Unity
vérifiée, actualiser volontairement le digest, les annotations de révision
du Deployment et du pod, ainsi que les valeurs attendues du validateur.
Conserver le protocole et le `buildId` compatibles avec le dedicated et
Characters ; le démarrage Nginx ne valide pas l'admission en jeu.

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
