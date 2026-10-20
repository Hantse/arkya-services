# Console d'administration — activation facultative

Ce profil étend **`../beta`**, avec la topologie complète dans
`arkya-identity` et `arkya-operations`. Il active les comptes Identity,
le répertoire Characters et le contrôle d'accès à la bêta. Admin conserve
son mode d'authentification hérité de `beta` et ses secrets existants.
Les versions d'images restent celles promues dans `beta/kustomization.yaml` ;
attendre la publication des images contenant ces API avant toute activation.

Les appels Admin → Identity/Characters utilisent HTTPS sur `8443`, avec
vérification du nom du serveur et de la CA montée dans
`/etc/arkya/internal-ca/ca.crt`. Le Secret `arkya-internal-ca` hérité doit
contenir la CA qui signe les certificats de ces deux services ; les options
de CA explicite chargent son premier certificat PEM. Les certificats doivent
couvrir leurs noms Kubernetes complets. Aucun contournement TLS ni HTTP
interne n'est activé. Une NetworkPolicy supplémentaire autorise uniquement
les pods Admin du namespace `arkya-operations` vers le port `8443` de ces
deux API ; les politiques existantes restent présentes.

## Secrets à provisionner hors Git

Les Deployments héritent déjà de ces Secrets via `envFrom`. Ajouter les
clés suivantes dans leurs **Secrets existants**, sans remplacer leurs
autres valeurs :

| Namespace | Secret | Clé |
| --- | --- | --- |
| `arkya-operations` | `arkya-admin-secrets` | `IdentityAdminApi__AdminKey` |
| `arkya-identity` | `arkya-identity-secrets` | `Administration__AdminKey` |
| `arkya-operations` | `arkya-admin-secrets` | `CharactersAdminApi__AdminKey` |
| `arkya-identity` | `arkya-characters-secrets` | `AdminAuthentication__Key` |

Les deux premières valeurs forment une paire ; les deux suivantes une
autre paire. Utiliser deux clés aléatoires distinctes de 32 à 512 caractères,
différentes des clés de service, des clés dédiées et du mot de passe Admin.
Les connexions MongoDB, les clés d'introspection Identity, le mot de passe
`Admin__Password`, les certificats internes et les autres secrets de `beta`
restent nécessaires. Aucune valeur de secret n'est créée dans ce profil.

`GameAccess__RequireBetaAccess=true` refuse l'admission et le renouvellement
des baux aux comptes sans autorisation bêta. Après activation, accorder
explicitement cet accès depuis la fiche du compte avant de tenter de jouer.
Un bannissement ou un retrait d'accès est pris en compte par les contrôles
serveur ; la présence affichée correspond à un bail encore actif.

## Vérification locale, sans déploiement

```powershell
kubectl kustomize backend/deploy/gitops/admin-console
python backend/deploy/gitops/admin-console/Validate-Manifests.py
```

Ces commandes rendent et valident les manifests sans contacter le cluster.
Le validateur nécessite `kubectl` et PyYAML et vérifie aussi les contrats
du profil `beta`, les destinations TLS, les secrets externes et les règles
réseau supplémentaires. Il ne prouve pas que les certificats, secrets,
images ou services sont disponibles dans l'environnement réel.

**Ce profil ne remplace pas directement `../admin`**, dont les ressources
se trouvent dans `arkya-beta` avec des communications HTTP privées et sans
Characters. Passer de ce profil à la topologie complète demande un plan
de migration explicite pour les namespaces, PVC, Secrets, certificats,
connexions MongoDB et ressources possédées par Argo CD. Aucun changement de
source Argo ni aucune application au cluster n'est effectué ici. Voir le
[guide de la console](../../../../docs/ADMIN_CONSOLE.md).
