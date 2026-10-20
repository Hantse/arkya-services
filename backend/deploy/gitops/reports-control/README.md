# Reports, suggestions et MCP dans le profil Admin

Ce composant ajoute le service privé Reports au profil `admin`, ainsi que ses routes joueur via Gateway, les pages de gestion Admin et le MCP hébergé `/mcp/reports`. Il conserve Inference, Discord, les PVC et les services existants. Il ne transforme pas les namespaces et ne crée aucun Secret, compte de service ou permission Kubernetes.

Le workflow commun `Services images` construit les dix images pour `linux/amd64` et `linux/arm64`. Après les tests Backend, frontend et packaging, il publie les images d'un même commit, puis promeut atomiquement leurs digests dans `beta/kustomization.yaml`, `inference-control/kustomization.yaml` et `reports-control/kustomization.yaml`. Une publication partielle ou une modification concurrente du code ou de la configuration bloque la promotion. Les images Unity restent inchangées.

L'activation se fait en deux commits : publier d'abord le code et le composant sans l'ajouter à `admin/components`, attendre le succès de la promotion et le remplacement de `pending-publication` par un digest, puis ajouter `../reports-control` aux composants du profil Admin. Conserver `../inference-control`. L'Application Argo CD existante continue de cibler `backend/deploy/gitops/admin` ; sa synchronisation automatique applique les commits sans déplacer la source vers un autre profil.

Le composant utilise uniquement ces références existantes :

| Processus | Variable | Secret / clé |
| --- | --- | --- |
| Reports | `Mongo__ConnectionString` | `arkya-content-secrets` / `Mongo__ConnectionString` |
| Reports | `Identity__ServiceKey` | `arkya-identity-secrets` / `ServiceAuthentication__Key` |
| Reports et Admin | `ReportsOperator__KeySeed` | `arkya-admin-secrets` / `ContentApi__AdminKey` |

La seed fournit des clés dérivées distinctes pour l'administration Reports, l'identité de service Reports et le Bearer MCP. Aucune seed ni clé dérivée ne doit figurer dans Git, les ConfigMaps ou les journaux. Le MCP et les captures restent protégés ; aucune route d'administration Reports n'est exposée directement par Gateway. MongoDB utilise la base `arkya_reports` et la sonde `/health/ready` vérifie sa disponibilité.

Validation locale sans accès au cluster :

```powershell
python -m unittest discover -s scripts/ci -p 'test_*.py' -v
python backend/deploy/gitops/admin/Validate-PlayerManagement.py
python backend/deploy/gitops/inference-control/Test-Manifests.py
python backend/deploy/gitops/reports-control/Test-Manifests.py
```

Le dernier validateur rend Reports seul et les deux ordres de composition avec Inference. Il vérifie les services existants, les références de secrets, les restrictions réseau et plusieurs mutations interdites. Ajouter `--require-digests` après publication pour refuser tout tag provisoire.
