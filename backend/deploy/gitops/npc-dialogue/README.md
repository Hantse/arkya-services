# Dialogues PNJ — activation facultative

Ce composant complète `admin` et son composant `inference-control`. Le profil
`admin` l'active pour raccorder les dialogues du jeu. Il ne crée ni valeur secrète ni modèle : les agents
Windows doivent déjà être inscrits et mis à jour avec le package **1.2.0**.

1. Publier les nouvelles images Inference et Realms. Vérifier que la Gateway
   dessert déjà les routes `dialogues/claim` et `dialogues/complete` et que le
   dedicated accepte les variables d'inférence et le protocole PNJ 5 ; la
   version commune 0.1.49 possède ce support et ne demande pas de rebuild Unity.
2. Vérifier la présence du Secret existant `arkya-admin-secrets`, clé
   `ContentApi__AdminKey`, dans `arkya-beta`, sans afficher sa valeur.
   Inference et Realms reçoivent cette même graine par `NpcDialogue__KeySeed`.
   Chacun dérive une clé HMAC propre aux dialogues PNJ, distincte des clés
   opérateur, Content et d'admission ; la graine ne rejoint jamais les dedicateds.
3. Après promotion des nouvelles images, ajouter `../npc-dialogue` aux
   `components` du profil `admin`. Inference active `NpcDialogue__Enabled=true` ;
   aucun nouveau Secret de service n'est à créer.
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

Une installation externe peut fournir une clé explicite de 32 à 512 caractères,
distincte des autres clés : `NpcDialogue__ServiceKey` dans Inference et
`RealmRuntime__InferenceServiceKey` dans Realms. Ces clés explicites restent
acceptées ; le composant livré utilise uniquement les références à la graine.
Ne pas placer une valeur de clé ou de graine dans Git ou une ConfigMap.

## Realm déjà déployé

La réconciliation automatique conserve son contrôle de dérive : elle ne complète
pas silencieusement les variables d'un Deployment existant. L'activation d'un
realm déjà lancé exige une action de maintenance explicite **Restart** dans
l'Admin (`AdminRestart`), après drainage des joueurs et vérification du build
et du protocole du dedicated.

Ce redémarrage peut ajouter `ARKYA_INFERENCE_API_URL` et
`ARKYA_INFERENCE_SERVICE_KEY` uniquement si les deux variables sont absentes et
si tous les autres champs du Deployment correspondent au manifest attendu.
Realms vérifie l'appartenance du Deployment et du Secret, puis ajoute l'origine
privée et une référence à `arkya-npc-dialogue/service-key` avec une précondition
de version Kubernetes. Une variable déjà présente, une autre dérive, une image
différente ou une réponse Kubernetes concurrente bloque cette adaptation.
Une réconciliation ordinaire ou une mise à jour d'image ne l'autorise pas.

Attendre le rollout de chaque partition et l'état Ready dans Realms, puis
tester une conversation en jeu. Cette action démarre de nouveaux processus
dedicateds ; elle ne change ni leur contenu ni l'autorité de gameplay.

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

Le validateur exerce les rendus sans composant et avec composant, que celui-ci
soit en prévisualisation ou déjà actif, puis vérifie les invariants complets du
profil Admin. Il contrôle les références de graine, l'unique coordinateur,
les permissions exactes et la préservation des ressources existantes.

Voir le [contrat des dialogues](../../../../docs/INFERENCE_AGENTS.md#dialogues-pnj-du-jeu)
pour les délais, champs et garanties.
