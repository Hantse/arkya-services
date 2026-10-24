# Nova natif : migration manuelle vers 0.1.49

Le composant conserve le profil déployé `nova-native`, sa paire réseau
`3 / arkya-multi-area-v1` et son image `cbb0cb0…`. Il prépare séparément le
profil `nova-native-v5` pour le client Windows et le dedicated **0.1.49** :

- Source : `22bc5eeabce7dfadaba55a42e4b4e9cf40fd8460`.
- [CI Unity vérifiée](https://github.com/Hantse/arkya-mmorpg/actions/runs/40095092545) : run `40095092545`.
- Paire réseau : `5 / arkya-town-npcs-v1`.
- Image : `ghcr.io/hantse/arkya-mmorpg-dedicated@sha256:241b6d9eca943b6701c52aa99e76746e6fa3c5c7886e4a03e0c1067ea5ee9693`.

La CI a construit Windows et Linux depuis ce même commit, vérifié leurs
identités, publié le conteneur après son contrôle de démarrage/arrêt et promu
l'inventaire natif. Ses tests Unity EditMode/PlayMode restent suspendus ; la
réussite de cette livraison ne remplace pas un parcours client/serveur réel.

## Plan et ressources conservés

La topologie reste exactement celle du snapshot Nova : Area `starter-island`,
contenu implicite `starter-island` (`ContentId` absent), 1536 × 1536, origine
X/Z 0/0, grille 2 × 2, quatre dedicateds, zéro instance et zéro raid. Aucun
portail n'est ajouté.

Les quatre anciens serveurs `island-starter-island-01` à `04`, leurs Services
et leurs routes restent configurés. Le nouveau profil utilise
`TopologyServerIdPrefix=v5`, ce qui produit les identifiants disjoints
`v5-starter-island-01` à `04`. Quatre nouveaux Services privés
`arkya-nova-v5-partition-01` à `04` et quatre routes gateway WSS les desservent.

Les trois NetworkPolicies gardent des sélecteurs finis pour ces huit IDs,
uniquement dans le realm `nova` : gateway vers TCP 17943, puis dedicateds vers
Characters et Realms sur TCP 8080. Le TLS public, les namespaces et les
références de secrets existants sont réutilisés. Aucun Deployment dédié,
Secret ou RBAC n'est ajouté. Le profil historique `beta-web`, ses releases et
le client WebGL restent inchangés. Le composant `partition-handoff` ne peut
pas être activé simultanément car il réserve les mêmes indices.

## Préparation puis demande opérateur

`Deployment__RuntimeReleases__0__Automatic` reste **`false`**. La synchronisation
GitOps prépare le profil, les routes et l'intention ; elle ne remplace aucun
serveur du realm. La migration de runtime version `1` épingle l'empreinte
exacte du profil Nova, confirmée dans l'admin authentifié le 24 octobre 2026 :

```text
c652962aa8072e2680ed4406fbcc3b3a6b373743560aa89ff50ef558f4ac1781
```

Le service doit comparer cette empreinte au snapshot vivant avant d'accepter
une demande. Une définition modifiée depuis ce relevé rend l'intention
inapplicable ; ne pas remplacer l'empreinte par une valeur approximative.

Après déploiement des API et de l'Admin compatibles, l'opérateur peut demander
la migration approuvée depuis **Update realm**, avec confirmation du realm,
motif et révision attendue. L'opération arrête les anciens serveurs avant de
créer les nouveaux, déconnecte les joueurs et exige le client Windows 0.1.49.
Suivre ensuite les quatre réplicas prêts, leurs heartbeats versionnés et les
parcours réels d'admission, handoff et persistance.

Pour préparer une livraison ultérieure, vérifier d'abord l'inventaire CI et
ses preuves Windows/dedicated. Une mise à jour conservant la paire réseau
utilise une `ImageRelease` manuelle avec digest précédent exact et version
strictement croissante. Un nouveau protocole nécessite un autre profil,
une empreinte vivante et des IDs/Services/routes disjoints. Le préfixe `v5`
ne doit pas être recyclé pour une seconde migration de runtime.

## Vérification hors ligne

```powershell
python backend/deploy/gitops/native-nova/Validate-Manifests.py --adopted
python backend/deploy/gitops/Test-Manifests.py --require-digests
python -m unittest discover -s scripts/ci -p test_native_nova_manifests.py -v
python -m unittest discover -s scripts/ci -p test_partition_handoff_manifests.py -v
```

Ces contrôles rendent des copies temporaires avec `kubectl kustomize`. Ils
exigent le delta exact : huit Services privés, huit routes, trois policies,
les deux profils et une seule intention manuelle avec sa topologie,
son empreinte et son identité CI. Ils n'activent aucune migration et ne
démontrent ni la synchronisation Argo ni la readiness des dedicateds réels.
