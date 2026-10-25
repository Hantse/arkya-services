# Nova natif : publications proposées, installation manuelle

Le composant conserve intégralement les profils historiques `nova-native`
(protocole 3) et `nova-native-v5` (protocole 5), leurs routes et la migration
manuelle de version 1. Il ajoute un profil fini `nova-native-v9` et la lecture
de l'inventaire publié par Unity pour proposer les versions compatibles dans
**Maintenance → Update realm**. Une publication ne lance aucune installation.

## Version initiale 0.1.62 et publications suivantes

Le profil `nova-native-v9` et la version initiale de `NativeReleases` épinglent :

- Version : **0.1.62**.
- Source : `cf745eb2efa087c5454b93ce28b60c28bcc9d726`.
- [CI Unity](https://github.com/Hantse/arkya-mmorpg/actions/runs/40141089077) : run `40141089077`.
- Paire réseau : `9 / arkya-craft-progression-v2`.
- Image : `ghcr.io/hantse/arkya-mmorpg-dedicated@sha256:ae1333150e0a31def941eb88eb600fad3c0e0d8529cd28bf1a959e627a988e53`.

Cette version initiale permet de proposer 0.1.62 lorsque la source native Argo
n'est pas encore activée. Realms consulte ensuite toutes les quinze secondes
le seul ConfigMap `arkya-native-runtime-release`, dans `arkya-beta`. Un inventaire
plus récent remplace la version proposée sans revenir à une version antérieure.
Une publication doit conserver une identité Windows/dedicated cohérente et la
paire réseau autorisée par le profil cible. Un protocole supplémentaire exige
un profil et des routes explicitement préparés ; il n'élargit pas ces droits.

Le rôle `arkya-native-release-reader` permet uniquement `get` sur ce ConfigMap
nommé, au ServiceAccount existant `arkya-realm-deployer`. Il n'accorde ni liste,
ni écriture, ni lecture d'autres objets ou namespaces. Aucun nouveau Secret,
Deployment ou ServiceAccount n'est créé.

Les versions suivantes compatibles sont proposées automatiquement. Leur
installation reste manuelle : l'opérateur vérifie la version et la provenance,
confirme le realm et saisit un motif. Le passage du protocole 5 au protocole 9
utilise les quatre IDs disjoints `v9-starter-island-01..04`. Les mises à jour
conservant le protocole 9 gardent ces IDs. Les candidats ne changent pas les
serveurs en cours avant cette confirmation.

Le [patch d'Application](../admin/native-runtime-metadata.application.patch.yaml)
conserve `arkya-services/master` sur `backend/deploy/gitops/admin` et ajoute
`arkya-mmorpg/main` sur `deploy/gitops/native-beta`. Il reste hors des ressources
du profil : sa présence dans Git ne l'active pas. Avant son application,
relire l'Application vivante et préserver toute autre source existante. La
lecture Kubernetes locale a expiré pendant la préparation du 25 octobre 2026 ;
l'activation de cette source et l'affichage de la version dans l'Admin vivant
restent à vérifier après synchronisation. Le seed 0.1.62 ne remplace pas cette
activation pour recevoir automatiquement les publications suivantes.

## Migration historique 0.1.49 conservée

Le profil `nova-native-v5` et son intention manuelle restent inchangés :

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

Le profil de protocole 9 utilise séparément `TopologyServerIdPrefix=v9`, quatre
Services `arkya-nova-v9-partition-01..04` et les entrées gateway 8 à 11. Les
douze alias WSS restent privés et liés à leurs IDs exacts.

Les trois NetworkPolicies gardent des sélecteurs finis pour ces douze IDs,
uniquement dans le realm `nova` : gateway vers TCP 17943, puis dedicateds vers
Characters et Realms sur TCP 8080. Le TLS public, les namespaces et les
références de secrets existants sont réutilisés. Aucun Deployment dédié ou
Secret n'est ajouté. Le seul nouveau RBAC lit l'inventaire natif nommé dans
`arkya-beta`. Le profil historique `beta-web`, ses releases et
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
exigent le delta exact : douze Services privés, douze routes, trois policies,
un Role et son RoleBinding, les trois profils, la seule intention historique
manuelle et la configuration des propositions natives avec son seed immuable.
Les dix-sept ressources ajoutées ne contiennent aucun serveur dédié. Les tests
refusent notamment une extension du rôle à d'autres verbes, objets ou namespaces.
Ils n'activent aucune migration et ne
démontrent ni la synchronisation Argo ni la readiness des dedicateds réels.
