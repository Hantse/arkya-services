# Dialogues PNJ — activation facultative

Ce composant complète `admin` et son composant `inference-control`. Il n'est pas
activé dans le profil livré. Il ne crée ni valeur secrète ni modèle : les agents
Windows doivent déjà être inscrits et mis à jour avec le package **1.2.0**.

1. Publier les nouvelles images Inference, Gateway, Realms et le dedicated Unity.
2. Provisionner hors Git un Secret `arkya-npc-dialogue-secrets` dans `arkya-beta`,
   contenant la clé `service-key` : une valeur aléatoire de 32 à 512 caractères,
   distincte des clés opérateur et d'admission.
3. Ajouter `../npc-dialogue` aux `components` du profil `admin` après sa préparation.
   Inference et Realms lisent la même clé par référence explicite à ce Secret.
4. Vérifier l'upgrade des agents : leur heartbeat annonce `1.2.0` et au moins un
   worker `ready`. Un agent 1.1 reste compatible avec l'administration, mais ne
   reçoit pas de dialogues du jeu.

Le composant garde **une réplique Inference**, stratégie `Recreate` : les demandes
de dialogue sont temporaires, en mémoire, et ne doivent pas être distribuées
entre deux coordinateurs. Un redémarrage perd ces demandes ; le dedicated utilise
alors son salut écrit et indique l'indisponibilité. Aucun progrès de quête, souvenir ou transaction ne dépend
de cette file. Une queue distribuée sera nécessaire avant plusieurs répliques.

Realms crée un Secret `arkya-npc-dialogue` appartenant au déploiement dans le
namespace du realm, puis injecte `ARKYA_INFERENCE_API_URL` et la référence secrète
`ARKYA_INFERENCE_SERVICE_KEY` dans les nouveaux dedicateds. Les clés restent hors
du catalogue public, des manifests de Deployment et des builds distribués.
Le HTTP privé n'est admis qu'à l'origine
`http://arkya-inference.arkya-beta.svc.cluster.local:8080/`, avec l'option existante
`RealmRuntime__AllowClusterHttp=true`. Utiliser HTTPS hors de ce profil.

## Realm déjà déployé

L'activation ne modifie pas automatiquement les variables d'un Deployment
existant. Après création du Secret, Realms signale `resource_configuration_drift`
si les deux variables manquent : son contrôle de dérive est préservé. Un simple
redémarrage ou une publication d'image ne contourne pas cette vérification.

Effectuer une maintenance explicite :

1. Drainer les joueurs, et vérifier le nouveau build/protocole du dedicated.
2. Lire les Deployments avec
   `kubectl -n arkya-beta-realm get deployments -l app.kubernetes.io/managed-by=arkya-realms -o json`.
   Pour chaque partition du realm ciblé, vérifier les labels realm/deployment,
   le premier container `dedicated`, et l'absence des deux variables ci-dessous.
3. Vérifier que `arkya-npc-dialogue` appartient au même deployment (labels) et
   contient `service-key`, sans afficher ou copier sa valeur.
4. Préparer un fichier JSON par Deployment avec la `resourceVersion` venant de
   cette lecture. Appliquer uniquement les deux ajouts ci-dessous avec
   `kubectl -n arkya-beta-realm patch deployment <nom-vérifié> --type=json --patch-file <fichier>`.
   La précondition échoue si le Deployment a changé entre lecture et écriture.

```json
[
  {"op":"test","path":"/metadata/resourceVersion","value":"<resourceVersion-vérifiée>"},
  {"op":"test","path":"/metadata/labels/app.kubernetes.io~1managed-by","value":"arkya-realms"},
  {"op":"test","path":"/spec/template/spec/containers/0/name","value":"dedicated"},
  {"op":"add","path":"/spec/template/spec/containers/0/env/-","value":{"name":"ARKYA_INFERENCE_API_URL","value":"http://arkya-inference.arkya-beta.svc.cluster.local:8080/"}},
  {"op":"add","path":"/spec/template/spec/containers/0/env/-","value":{"name":"ARKYA_INFERENCE_SERVICE_KEY","valueFrom":{"secretKeyRef":{"name":"arkya-npc-dialogue","key":"service-key"}}}}
]
```

Attendre le rollout de chaque partition, puis vérifier l'état Ready dans Realms.
Les deux variables se placent après `ARKYA_ALLOW_CLUSTER_HTTP`, en fin de liste,
comme dans le manifest attendu. Ne pas appliquer cette addition si une variable
existe déjà ou si une autre dérive est présente ; traiter cette différence
séparément. Cette mise à jour démarre de nouveaux processus dedicateds.

La NetworkPolicy ajoutée permet uniquement les pods gérés par Realms dans
`arkya-beta-realm` vers Inference sur TCP 8080. Pour un nouveau namespace approuvé,
ajouter explicitement sa règle réseau et sa permission de lecture/suppression
du Secret `arkya-npc-dialogue`. Les routes `/internal/inference/*` restent absentes
de Gateway ; seuls les deux POST machine `dialogues/claim` et `dialogues/complete`
rejoignent les routes existantes d'inscription/heartbeat.

Ne retirer le composant et ses permissions qu'après drainage des dedicateds
qui utilisent le Secret. La rotation d'une clé déjà provisionnée reste une
opération de maintenance : le reconciler refuse de remplacer un Secret existant
qui diffère de la valeur attendue.

Vérification locale, sans connexion Kubernetes :

```powershell
python backend/deploy/gitops/npc-dialogue/Test-Manifests.py
```

Voir le [contrat des dialogues](../../../../docs/INFERENCE_AGENTS.md#dialogues-pnj-du-jeu)
pour les délais, champs et garanties.
