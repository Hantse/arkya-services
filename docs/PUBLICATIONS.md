# Actualités et notes de version

`Arkya.Content.Api` possède les actualités (`news`) et notes de version
(`release-note`) dans `arkya_content.content_posts`. Les catalogues de quêtes
restent indépendants. Aucun article n’est créé ou publié au démarrage.

L’Admin utilise sa clé Content existante ; les identifiants joueurs et la clé
des serveurs dédiés ne permettent aucune modification. Le launcher et le site
lisent uniquement les publications publiques, sans compte ni cookie.

## Backoffice

- `/Publications` : liste filtrée par type/langue, avec pagination.
- `/Publication` : création d’un brouillon.
- `/Publication/{id}` : édition, aperçu en texte brut, publication et dépublication.

Enregistrer un article déjà publié conserve le contenu public précédent.
Publier copie le brouillon enregistré dans le snapshot public. Les changements
non enregistrés dans le formulaire ne font pas partie de cette publication.
Une saisie conflictuelle reste visible pour pouvoir la reprendre après rechargement.
Dépublier retire immédiatement l’article des lectures publiques et conserve son brouillon.

## Contrat HTTP

Le Gateway expose seulement les deux lectures publiques ci-dessous, ainsi que
leur préflight `OPTIONS`, sur `https://api.arkya.online`.

| Méthode | Chemin | Résultat |
| --- | --- | --- |
| GET | `/v1/content/posts` | `{ items: [...], hasMore: boolean }`, publiés seulement |
| GET | `/v1/content/posts/{id}` | Snapshot publié, ou `404` |
| GET | `/admin/content/posts` | Même enveloppe, brouillons et métadonnées de publication |
| POST | `/admin/content/posts` | Crée un brouillon, `201` et `Location` |
| GET | `/admin/content/posts/{id}` | Brouillon et métadonnées, ou `404` |
| PUT | `/admin/content/posts/{id}` | Enregistre le brouillon, `200` |
| POST | `/admin/content/posts/{id}/publish` | Publie le brouillon enregistré, `200` |
| POST | `/admin/content/posts/{id}/unpublish` | Retire la publication, `200` |
| DELETE | `/admin/content/posts/{id}?revision=N` | Supprime article et publication, `204` |

Toutes les routes `/admin/content` restent internes et demandent exactement un
header `X-Arkya-Admin-Key`. La suppression est disponible par API ; le backoffice
conserve les brouillons et propose la dépublication.

Les deux listes acceptent `kind=news|release-note`, `language=fr|en`,
`limit=1..50` (défaut `20`) et `offset=0..10000` (défaut `0`). Omettre type ou
langue sélectionne toutes leurs valeurs. Le feed public trie par date de
publication décroissante puis ID décroissant ; la liste Admin par date de
modification puis ID. Le filtrage public porte sur le snapshot publié, même si
le type ou la langue du brouillon a changé. Une publication simultanée à la
pagination peut déplacer les entrées ; recharger la première page actualise la liste.

Création : `{ kind, language, title, summary, body, gameVersion }`.
Modification : mêmes champs avec `revision` obligatoire.
Publication/dépublication : `{ revision }` uniquement.

- `title` : obligatoire, 160 caractères maximum, sans caractères de contrôle.
- `summary` : facultatif, 400 caractères maximum, sans contrôles ; `null` devient `""`.
- `body` : obligatoire, 20 000 caractères maximum ; retours ligne et tabulations acceptés.
- `gameVersion` : obligatoire pour `release-note`, 64 caractères maximum,
  motif `[A-Za-z0-9][A-Za-z0-9._+-]*` ; absent ou vide pour `news`.

Le contenu est du **texte brut**, sans HTML ni Markdown rendu. Les consommateurs
l’affichent comme texte, jamais dans `innerHTML` ou un navigateur embarqué.
Les champs JSON inconnus, valeurs invalides et révisions manquantes donnent `400`.
Le serveur conserve la limite globale de requête à 512 Kio.

Un item public contient `id`, `kind`, `language`, `title`, `summary`, `body`,
`gameVersion` et `publishedAt` UTC. Un item Admin contient les champs du brouillon,
avec `id`, `revision`, `createdAt`, `updatedAt`, `publishedAt` nullable et
`publishedRevision` nullable. Les dates et IDs sont générés par le serveur.

La révision débute à `1` et augmente à chaque sauvegarde, publication ou
dépublication. Juste après publication, `revision == publishedRevision` ; après
une édition, `revision > publishedRevision`. Toute mutation exige la révision
courante ; un conflit concurrent donne `409`, sans écraser l’autre modification.
Une répétition avec une ancienne révision demande donc de recharger, même après
une réussite dont la réponse aurait été perdue.

## Persistance et livraison

Chaque document utilise un ID GUID stable, `SchemaVersion=1`, un brouillon et
un snapshot publié facultatif. Le remplacement conditionnel Mongo vérifie ID,
schéma et révision dans une seule opération, sans transaction multi-document.
Les index `ix_posts_public_feed` et `ix_posts_admin_feed` couvrent les filtres et
l’ordre de consultation. Les schémas inconnus restent invisibles et non modifiables.
Les erreurs de persistance donnent `503`, sans remplacer le résultat par un faux feed vide.

Le CORS public autorise `GET` depuis toute origine, sans credentials. Les réponses
restent `no-store` pour rendre les retraits visibles immédiatement. Les endpoints
Admin n’exposent pas ce CORS public.

Le profil GitOps `admin` active `Gateway__ContentEnabled`, configure la destination
Content interne, accepte le Host public et autorise uniquement le Gateway et
l’Admin vers le port Content 8080. L’accès réseau de Realms reste inchangé.
La livraison passe par Git, CI, les images puis la réconciliation Argo CD.

Les tests Content vérifient séparation des capacités, confidentialité des
brouillons, publication/dépublication, limites, pagination, CORS, indisponibilité
et conflits. Avec `ARKYA_TEST_MONGO`, ils vérifient également les opérations
concurrentes entre instances, la reprise, les index et l’isolation des schémas
dans une base jetable. Les tests Gateway refusent écritures et routes internes.
