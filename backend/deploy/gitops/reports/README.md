# Profil hébergé : signalements et MCP

Ce profil **facultatif** étend `../admin` dans `arkya-beta` :

- jeu → `api.arkya.online/v1/bug-reports` → Reports ;
- backoffice `/BugReports` → Reports ;
- Codex → `https://admin.arkya.online/mcp/reports` avec bearer dédié ;
- Reports → introspection privée Identity et base MongoDB `arkya_reports`.

Il ajoute un septième Deployment. Les réseaux privés autorisés correspondent
au profil Admin existant : pods `10.42.0.0/16`, nœuds `192.168.100.0/24`.
L'exception HTTP d'Identity est limitée à `/internal/sessions` et au pair réseau
direct autorisé. Sa politique réseau ajoute seulement le pod Reports aux
origines déjà autorisées ; ses routes publiques continuent d'exiger HTTPS.
Admin et la gateway sont les seuls pods autorisés à joindre Reports.
Adapter ces réseaux si l'environnement diffère.

Le profil ne devient pas actif par la seule présence de ces fichiers.
Ne pas basculer l'Application Argo avant publication des images et provisionnement.

## Préparer les secrets hors Git

Depuis PowerShell 7 :

```powershell
./backend/deploy/gitops/reports/Prepare-Secrets.ps1
```

Le script demande sous saisie masquée l'URI MongoDB ayant accès à
`arkya_reports` et la clé de service entrante **déjà utilisée par Identity**.
Il génère des clés distinctes Reports, MCP et service, puis écrit dans
`.local/`, avec accès restreint à l'utilisateur. Il refuse tout remplacement
et ne contacte pas le cluster.

| Fichier privé | Destination |
| --- | --- |
| `.local/reports.env` | Nouveau Secret `arkya-reports-secrets` |
| `.local/admin-reports.env` | Nouveau Secret `arkya-admin-reports-secrets` |
| `.local/mcp-token` | Gestionnaire de secrets du poste Codex, variable `ARKYA_ADMIN_MCP_TOKEN` |

L'ancien `arkya-admin-secrets` est conservé ; le nouveau Secret est ajouté
comme source complémentaire. Aucune rotation des clés existantes n'est faite.
Les clés MCP et Reports sont distinctes : Codex reçoit uniquement la première.

Après vérification du contexte, créer les deux nouveaux Secrets depuis une
machine autorisée :

```powershell
kubectl -n arkya-beta create secret generic arkya-reports-secrets --from-env-file=backend/deploy/gitops/reports/.local/reports.env
kubectl -n arkya-beta create secret generic arkya-admin-reports-secrets --from-env-file=backend/deploy/gitops/reports/.local/admin-reports.env
```

## Publier et activer

1. Publier les changements services : la CI existante reconstruit Identity,
   Admin et Gateway et promeut leurs digests.
2. Le workflow `reports-image.yml` construit Reports AMD64/ARM64, teste le
   backend et fournit son digest. Il ne change pas le profil Argo actif.
3. Dans `kustomization.yaml`, remplacer `newTag: pending-publication` par
   `digest: sha256:<digest réellement publié>`. Ne pas inventer de digest.
4. Valider le rendu complet :

```powershell
python backend/deploy/gitops/Test-Manifests.py --require-digests
python backend/deploy/gitops/reports/Test-Manifests.py --require-digests
kubectl kustomize backend/deploy/gitops/reports
```

5. Dans l'Application existante `arkya-services`, choisir
   `backend/deploy/gitops/reports` comme chemin source, garder le dépôt,
   la révision, la destination et la politique de synchronisation existants.
   Revoir le diff et synchroniser. Ne pas créer une seconde Application qui
   posséderait les mêmes ressources.
6. Vérifier le rollout de Reports, Identity, Admin et Gateway. La sonde
   `/health/ready` de Reports vérifie MongoDB ; `/health` vérifie le processus.
7. Tester une session joueur réelle, l'envoi d'une capture, le filtrage
   `urgent`, le changement de statut, puis le retour du statut dans le jeu.
8. Fournir le bearer au processus Codex et activer le bloc
   [MCP hébergé](../../../tools/admin-mcp.hosted.config.example.toml).
   Vérifier les cinq outils (dont `list_suggestions`) et la capture avant toute mise à jour.

Le déploiement de Reports ne publie pas le nouveau build Unity :
le dépôt client indépendant doit également être construit et publié.
Les scénarios complets nécessitent les images et secrets réels de
l'environnement ; un rendu Kustomize réussi ne prouve pas un rollout.
