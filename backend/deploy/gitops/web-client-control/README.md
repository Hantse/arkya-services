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
`sha256:f523c1b5ddc88a321ac7708a8dcec1b59469ee01390270c53df74d573b63e07a`.
L'annotation `arkya.online/source-revision` conserve la révision associée dans
le manifest Unity promu : `fd77651a2121832d5c1c955e28280d5a6258b01a`.
Ce composant n'effectue aucun build Unity.

La CI des services ne promeut pas cette image. Après une livraison Unity
vérifiée, actualiser volontairement le digest, les annotations de révision
du Deployment et du pod, ainsi que les valeurs attendues du validateur.
Conserver le protocole et le `buildId` compatibles avec le dedicated et
Characters ; le démarrage Nginx ne valide pas l'admission en jeu.

La [livraison Unity du 21 octobre](https://github.com/Hantse/arkya-mmorpg/actions/runs/39591280771)
a validé les tests, les trois builds et la santé puis l'arrêt des deux
conteneurs avant la promotion `aa8fbc9`. Elle contient le nouvel atelier
« Un visage. Une histoire. » et l'île actuelle.
Le profil Admin sélectionne le dedicated associé,
`sha256:7da7ef5df8bcb011cbce9ab6fa8dc244481162c7274299539ec021e485dd70da`.
L'intention `Deployment:ImageReleases` de version `1` autorise uniquement
la transition du realm `beta-web` depuis l'ancienne image épinglée ;
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
