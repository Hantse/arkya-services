# Profil optionnel Inference

Ce profil ajoute `Arkya.Inference.Api` au profil Reports existant. Il active **Agents LFM** dans Admin et les routes publiques `POST /agent/inference/enroll` et `POST /agent/inference/heartbeat` dans Gateway. L'inscription exige une clé temporaire créée dans Admin ; chaque heartbeat exige ensuite le jeton propre à la machine. L'API opérateur reste privée ; aucun service Ollama n'est publié dans le cluster.

Préparer la version du backend et les images Admin/Gateway qui comprennent cette fonctionnalité. Le workflow `inference-image.yml` publie l'image du nouveau service sans activer ce profil. Remplacer `pending-publication` dans `kustomization.yaml` par le digest publié (champ `digest: sha256:...`, suppression de `newTag`). Le profil Reports parent doit également avoir été préparé et épinglé.

Créer un utilisateur MongoDB avec accès à `arkya_inference`, puis préparer les secrets localement :

```powershell
.\backend\deploy\gitops\inference\Prepare-Secrets.ps1
```

Ce script demande l'URI MongoDB et produit une clé opérateur aléatoire commune à Admin et Inference. Les fichiers restent dans `.local/`, exclu de Git. Ne pas remplacer des secrets existants lors d'une mise à jour : conserver la clé opérateur et la base pour préserver l'enregistrement des machines.

Créer les deux secrets dans `arkya-beta` avant activation :

```powershell
kubectl -n arkya-beta create secret generic arkya-inference-secrets --from-env-file=backend/deploy/gitops/inference/.local/inference.env
kubectl -n arkya-beta create secret generic arkya-admin-inference-secrets --from-env-file=backend/deploy/gitops/inference/.local/admin-inference.env
```

Vérifier hors cluster :

```powershell
python backend/deploy/gitops/inference/Test-Manifests.py --require-digests
kubectl kustomize backend/deploy/gitops/inference
```

L'activation se fait ensuite par le chemin GitOps habituel, avec ce profil comme cible. Les scripts et vérifications ci-dessus ne modifient pas le cluster par eux-mêmes. Le service utilise son propre stockage MongoDB ; la suppression d'un pod ne supprime pas les machines enregistrées.

Le protocole et les instructions d'installation Windows sont décrits dans [INFERENCE_AGENTS.md](../../../../docs/INFERENCE_AGENTS.md).
