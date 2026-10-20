# Vérification des signalements — 20 octobre 2026

La fonctionnalité est implémentée dans le dépôt services et le dépôt Unity
indépendant. Elle n'a pas été déployée dans l'environnement hébergé.

## Backend

Commande avec un MongoDB 8.0.30 isolé sur loopback :

```powershell
$env:ARKYA_TEST_MONGO = 'mongodb://127.0.0.1:27028'
dotnet test backend/Arkya.Backend.slnx --configuration Release --logger trx --results-directory artifacts/bug-report-validation/backend-results
```

| Suite | Résultat global |
| --- | --- |
| Reports | 54/54, dont 3 tests Mongo réels |
| Admin, y compris MCP HTTP | 182/182 |
| MCP stdio/client partagé | 13/13 |
| Gateway | 138/138 |
| Identity | 203/203 |
| Characters | 124/124 |
| Content | 48/48 |
| Parcours backend | 1/1 |
| Realms | 54/55 |

Total du passage global : **817/818**, sans test ignoré. Le cas positif
`KubernetesTlsTests.HttpsRequiresBothConfiguredAuthorityAndMatchingHostname`
a dépassé son délai pendant le passage parallèle. Une relance ciblée, sans
modifier son code, a réussi les **3/3** cas TLS. Le résultat global initial
reste donc enregistré comme un échec ; les preuves du recontrôle sont dans
`artifacts/bug-report-validation/backend-recheck`.

Les tests MCP utilisent réellement le protocole stdio et le transport HTTP,
avec découverte des outils, lecture du PNG, changements d'état et conflits.
Le transport HTTP couvre aussi l'initialisation MCP 2025, le SDK 2.2.0,
les mauvaises clés, l'isolation des cookies, les origines étrangères et
l'activation explicite. Les fixtures ne prouvent pas la disponibilité
des services hébergés.

## Unity

- Compilation de la feature dans Unity `6000.6.6f1` ; aucune erreur de
  compilation au contrôle final.
- **11/11** tests de signalement, dont rendu réel en Play Mode, F8, capture
  avant formulaire, fermeture, annulation, limites et fidélité des couleurs.
- **23/23** tests du client de session existant.
- Inspection visuelle de `artifacts/bug-report-validation/unity/bug-report-form.png`
  et `bug-report-evidence.png`.
- Les tests Play Mode ont utilisé une copie de validation pour préserver
  le projet principal. Voir le [compte rendu client](BUG_REPORTS_UNITY.md).

Le projet principal avait initialement `StarterIsland.isDirty=true` et le
contrôle final indique `false`, avec les mêmes 18 objets racines. L'horodatage
du fichier est proche des premiers tests Edit Mode. Sans hash initial, la
cause de ce changement ne peut pas être attribuée ; aucun rollback n'a été
effectué. Les détails et l'empreinte finale figurent dans le compte rendu client.

Le test d'envoi Unity → API n'a pas été exécuté : le contrôle automatique
d'approbation a refusé le lancement des services temporaires avec le seul
motif « blocked by policy ». Les tests client, HTTP, persistance et MCP ont
été réalisés séparément.

## Déploiement et scripts

- Profil Reports : **30 objets, 7 Deployments**, rendu Kustomize et contrats
  de secrets/réseau/sondes validés.
- Profils existants : rendu et contrôles avec `--require-digests` réussis.
- Six tests de promotion d'images réussis.
- Syntaxe des deux scripts PowerShell validée sans exécuter la saisie de secrets.
- `git diff --check` propre sur le périmètre.

L'API Kubernetes configurée a expiré lors d'une lecture des services :
aucune ressource du cluster n'a été changée. L'image Reports porte encore
le marqueur `pending-publication` ; il doit être remplacé par un digest réel.
La configuration Codex locale est préparée, désactivée en attente du
déploiement et de `ARKYA_ADMIN_MCP_TOKEN`.

Docker Desktop n'était pas actif : aucune construction de conteneur locale
n'est revendiquée. Les workflows assurent cette construction après publication
du code. Le processus MongoDB temporaire a été arrêté après les tests.

## Ajout F9 et préparation de livraison — 21 octobre 2026

F9 ouvre une suggestion avec le même parcours de capture et d'envoi que F8.
Le type reste immuable pendant un retry et dans le suivi administrateur.
Les anciens rapports sans type restent des bugs, y compris leur empreinte
d'idempotence et les filtres MongoDB.

- Reports : **71/71** tests, dont MongoDB réel, après l'ajout du type.
- MCP stdio : **14/14**, avec les cinq outils et les deux types.
- Unity : **15/15**, dont ouverture F9, payload et parcours Play Mode rendu.
  La capture `artifacts/bug-report-validation/suggestions/suggestion-form.png`
  a été contrôlée ; hash, taille et horodatage de StarterIsland sont inchangés
  entre les mesures avant/après cette série.
- La publication est préparée dans des worktrees séparés depuis les branches
  distantes actuelles, pour préserver les changements locaux de scènes et d'art.
- La CI commune comporte dix images et une promotion atomique de trois fichiers
  de digests. **28 tests Python**, Actionlint et les rendus des profils Admin,
  Inference et Reports ont réussi avant publication.

Les résultats de livraison et la connexion hébergée seront consignés après
la promotion des images et l'activation du composant Reports.

Après ajout du raccordement aux secrets existants et de l'export Codex :
**318/318 Admin**, **170/170 Gateway**, **14/14 MCP** passent dans le worktree
de publication. Reports : **68 réussis, 4 Mongo ignorés** faute d'instance
sur ce second passage ; la CI conserve l'exécution MongoDB réelle obligatoire.

## Publication GitOps — 21 octobre 2026

Le code Services est publié dans `5915299` et le client Unity dans `88f3e8f`.
La CI [Services images](https://github.com/Hantse/arkya-services/actions/runs/39507370833)
a publié les dix images puis inscrit leurs digests dans `be344fc`, depuis
`4c3b9cc` qui inclut le changement Reports et une correction Realms concurrente.
La première exécution a volontairement refusé de promouvoir une source dépassée.

Le composant Reports est maintenant ajouté au profil Admin existant, avec
Inference. Le rendu composé contient **39 objets** ; les contrats et les digests
ont été validés sans accès Kubernetes. La synchronisation reste assurée par Argo CD.
La disponibilité publique et la connexion Codex restent à vérifier après sync.

La [CI Unity](https://github.com/Hantse/arkya-mmorpg/actions/runs/39507179874)
construit séparément les clients et le dedicated à partir du commit publié.
