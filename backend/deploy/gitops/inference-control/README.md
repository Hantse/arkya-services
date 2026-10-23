# Composant Inference du profil Admin

Ce composant ajoute `Arkya.Inference.Api`, son ConfigMap, son Service ClusterIP et sa NetworkPolicy au profil Admin existant. Il active la page Agents et les deux routes publiques `POST /agent/inference/enroll` et `POST /agent/inference/heartbeat`. Les routes opérateur restent privées. Il ne dépend pas de Reports et ne change pas les namespaces des ressources parentes.

La publication suit le circuit CI/CD et Argo CD existant. Le workflow Services images construit neuf images et promeut leurs digests dans un seul commit : les huit images historiques dans `beta/kustomization.yaml`, Inference dans `inference-control/kustomization.yaml`. Cette seconde pin est nécessaire car une transformation d'images exécutée dans une base ne modifie pas une ressource ajoutée ensuite par un composant.

La première activation attend la réussite de cette publication et du contrôle `--require-digests`. Un commit distinct ajoute alors au profil `admin/kustomization.yaml` :

```yaml
components:
- ../inference-control
```

L'Application Argo existante conserve son dépôt, son chemin Admin, son propriétaire et sa politique de synchronisation. Aucun déploiement direct avec kubectl ni nouvelle Application ne fait partie de ce circuit.

## Identifiants existants

Le composant référence uniquement des clés de Secrets déjà utilisées par le profil Admin ; aucune valeur secrète n'est écrite dans Git ou transmise à la CI :

| Configuration | Secret / clé existants |
|---|---|
| `Mongo__ConnectionString` dans Inference | `arkya-content-secrets` / `Mongo__ConnectionString` |
| `InferenceOperator__KeySeed` dans Admin et Inference | `arkya-admin-secrets` / `ContentApi__AdminKey` |

Inference conserve sa base propre `arkya_inference`. L'utilisateur MongoDB existant doit y disposer des permissions nécessaires ; la création des index et la readiness échouent si ce droit manque. La réutilisation de la connexion suit le profil Characters et ne fusionne pas les données des services.

`InferenceOperatorKeys` dérive une clé HMAC-SHA256 avec la finalité `arkya.inference.operator-administration.v1`. Admin et Inference obtiennent ainsi la même clé, distincte des clés Content et Player Administration. Le secret d'origine n'est jamais envoyé comme clé opérateur Inference. La résolution intervient après liaison des options et avant leur validation au démarrage. Un seed invalide ou la présence simultanée d'un seed et d'une clé explicite empêche le démarrage, sans afficher les valeurs.

Les configurations hors GitOps peuvent conserver leurs clés explicites `InferenceApi:AdminKey` et `InferenceAdmin:Key` en omettant le seed. Une rotation du Secret source exige le redémarrage coordonné d'Admin et d'Inference ; elle ne remplace pas les jetons permanents des agents conservés dans MongoDB.

## Vérification sans cluster

```powershell
python backend/deploy/gitops/inference-control/Test-Manifests.py
python backend/deploy/gitops/inference-control/Test-Manifests.py --require-digests
```

Avant activation, le validateur assemble une preview temporaire Admin + composant. Après activation, il contrôle directement le rendu Admin. Il vérifie les références précises aux Secrets, la séparation des réseaux, la base Inference et l'absence de Reports. Le mode strict refuse toute image non épinglée par digest. Ces contrôles ne prouvent ni la disponibilité du registre ni les permissions MongoDB du cluster.

Après synchronisation Argo, l'inscription publique doit répondre avec une erreur de validation/authentification à une requête sans identifiant, puis fonctionner avec une clé d'installation émise dans Admin. Un HTTP 404 signifie que la route n'est pas encore disponible ; un 502/503 nécessite de vérifier le rollout et la readiness. Le protocole Windows est décrit dans [INFERENCE_AGENTS.md](../../../../docs/INFERENCE_AGENTS.md).
