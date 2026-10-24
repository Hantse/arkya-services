# Inscriptions à la bêta

La page publique `https://arkya.online/beta.html` utilise le compte Discord
Arkya existant. La connexion seule n'inscrit personne : le joueur confirme
explicitement son inscription puis consulte son statut sur cette même page.
Les textes et parcours sont disponibles en français et en anglais.

L'inscription rejoint une liste de candidats ; elle ne donne aucun droit de
jeu. Aucun e-mail ni nouvelle identité n'est demandé. Identity conserve la date
de première inscription sur le compte et les droits bêta existants restent
l'unique source d'autorité pour Characters et les serveurs dédiés.

## Attribution des accès

`https://admin.arkya.online/BetaRegistrations` présente les inscrits avec
recherche, pagination et filtres `pending`, `granted`, `banned` et `all`.
L'opérateur peut attribuer un accès individuel avec la révision courante et un
motif. La fiche Compte conserve les actions de retrait d'accès et de modération.

Le bouton **Give x random access** ouvre une modale : quantité de 1 à 100 et
motif. Le tirage porte sur l'ensemble des inscrits en attente, indépendamment
des filtres affichés. Il exclut les comptes bannis et ceux qui possèdent déjà
l'accès. La réponse indique le nombre réellement attribué, qui peut être
inférieur à la quantité demandée si des candidats manquent ou changent d'état.

Chaque formulaire de tirage porte un UUID `requestId`. Identity enregistre une
sélection immuable et ses résultats dans `beta_grant_operations` avant de
répondre. Une reprise du même identifiant ne tire aucun nouveau candidat et ne
rétablit pas un accès retiré après le tirage. Changer la quantité, le motif ou
l'acteur pour le même UUID donne `409`. Après une erreur réseau, réessayer le
formulaire conservé ; ouvrir un nouveau tirage représente une nouvelle opération.

Les attributions sont conditionnelles à la révision et à l'éligibilité du
compte. Un court verrou persistant empêche une décision de modération de passer
entre l'attribution et l'enregistrement de son résultat. Le worker Identity
reprend les opérations inachevées au démarrage puis chaque minute, avec au plus
25 opérations par passage. Une décision concurrente doit être relue et retentée.
Les attributions conservent acteur, motif et révision dans l'audit du compte.

## Contrats

| Accès | Route | Contrat |
| --- | --- | --- |
| Joueur | `GET /v1/beta/registration` | `{ registered, registeredAt, hasBetaAccess }` |
| Joueur | `POST /v1/beta/registration` | `{}` ; inscription idempotente du compte de la session |
| Opérateur | `GET /admin/beta/registrations` | `search`, `status`, `page`, `pageSize` ; liste privée et total |
| Opérateur | `POST /admin/beta/registrations/{accountId}/grant` | `{ expectedRevision, reason }` |
| Opérateur | `POST /admin/beta/grants/random` | `{ requestId, count, reason }` |

Les réponses publiques ne contiennent ni Discord ID, ni liste de candidats, ni
identifiant d'un autre compte. Le bearer Arkya identifie le joueur ; aucun
`accountId` ou droit d'accès fourni par son navigateur n'est accepté. Les
lectures et écritures publiques utilisent le CORS Identity et `no-store`.
La gateway expose uniquement la route publique exacte en GET/POST/OPTIONS.

Les routes privées utilisent les contrôles Identity existants
`Administration:Enabled`, `X-Arkya-Admin-Key` et `X-Arkya-Admin-Actor`.
Le backoffice exige sa session opérateur et l'antiforgery pour chaque mutation.
Il utilise la connexion `IdentityAdminApi`, sans clé privée dans le navigateur.
Les contrats sont définis dans `Arkya.ServiceDefaults/BetaAccessContracts.cs`.

## Livraison et vérification

Le site, Gateway, Identity et Admin sont livrés par leurs pipelines et GitOps.
La news d'ouverture doit attendre que la page et la route publiques soient
effectivement disponibles. Les articles préparés sont conservés avec leurs
[sources éditoriales](publications/2026-10-24-beta/README.md), puis créés et
publiés explicitement par le MCP Admin ; aucun contenu n'est créé au démarrage.

Les tests couvrent inscription répétée, identité de session, éligibilité,
contrôle des capacités, révisions et reprise des tirages ; les tests MongoDB
exercent la concurrence et la persistance. Le site vérifie les états non
connecté/en attente/accepté, la restauration de session et le retour OAuth
limité à `beta.html`, sans inscription automatique après connexion.

Validation du 24 octobre 2026 : **252 tests Identity**, **887 tests Admin**,
**277 tests Gateway** et **129 tests Node** réussis. Les suites Identity et Admin
ont utilisé MongoDB réel sans test ignoré. Les essais Edge totalisent
**248 contrôles** : inscription, compte, renouvellement, launcher et modale Admin,
avec des API isolées. Aucun compte de production n'a été inscrit ou invité
par ces essais. La [CI complète](https://github.com/Hantse/arkya-services/actions/runs/40087241218)
a également construit les dix images et promu les digests vers GitOps.

Après livraison, les contrôles publics ont confirmé `beta.html` en HTTP 200,
l'API sans bearer en HTTP 401, le préflight POST autorisé depuis
`https://arkya.online` en HTTP 204 et la page Admin protégée par sa connexion.
La news FR/EN et les huit notes de version ont ensuite été publiées et relues
par leurs endpoints anonymes ; voir le [compte rendu MCP](publications/2026-10-24-beta/publication-receipts.json).
