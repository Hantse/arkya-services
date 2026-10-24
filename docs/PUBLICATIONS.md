# Actualités et notes de version

`Arkya.Content.Api` possède les actualités (`news`) et notes de version
(`release-note`) dans `arkya_content.content_posts`. Les catalogues de quêtes
restent indépendants. Aucun article n’est créé ou publié au démarrage.

L’Admin utilise sa clé Content existante ; les identifiants joueurs et la clé
des serveurs dédiés ne permettent aucune modification. Le launcher et le site
lisent uniquement les publications publiques, sans compte ni cookie.

## Backoffice

- `/Publications` : deux onglets **News / Release notes**, avec recherche,
  langue, version exacte pour les notes, période UTC, tri et pagination.
  L'onglet News et 12 résultats sont sélectionnés par défaut ; les tailles
  proposées sont 12, 24 et 48. Les anciens liens avec 1 à 50 résultats restent valides.
- `/Publication` : création d’un brouillon ; le type et la langue sélectionnés
  dans la liste préremplissent le formulaire.
- `/Publication/{id}` : édition, aperçu en texte brut, publication et dépublication.

Enregistrer un article déjà publié conserve le contenu public précédent.
Publier copie le brouillon enregistré dans le snapshot public. Les changements
non enregistrés dans le formulaire ne font pas partie de cette publication.
Une saisie conflictuelle reste visible pour pouvoir la reprendre après rechargement.
Dépublier retire immédiatement l’article des lectures publiques et conserve son brouillon.

Les filtres de l'admin portent sur le contenu du brouillon, qu'il soit publié
ou non. Les dates **Modified from/to (UTC)** sont inclusives et le tri porte
sur la dernière modification, afin de retrouver aussi les brouillons.
Les onglets, la pagination et le lien de retry conservent la sélection ;
changer d'onglet revient à la première page et News retire le filtre de version.
Effacer les filtres conserve l'onglet courant. Le retour depuis l'éditeur
retrouve l'onglet correspondant au type de l'article enregistré.

## MCP administrateur

Le MCP Admin existant expose aussi les actualités et notes de version sur
`https://admin.arkya.online/mcp/reports`. Son chemin est conservé pour les
connexions existantes. Quand `AdminMcp:Enabled=true` et `ContentApi:Enabled=true`,
le même bearer `AdminMcp:Key` donne accès aux outils de publications ci-dessous,
en plus des outils de signalements activés. Cette clé permet donc aussi de
publier, dépublier et supprimer des articles ; elle doit rester distincte de
`ContentApi:AdminKey`, de la clé Reports et du mot de passe Admin.

| Outil MCP | Effet |
| --- | --- |
| `list_publications` | Liste les brouillons et métadonnées de publication ; filtres facultatifs `kind` et `language`, pagination `offset`/`limit` |
| `get_publication` | Lit le brouillon et sa révision courante par `id` |
| `create_publication` | Crée un brouillon avec `kind`, `language`, `title`, `body`, et éventuellement `summary`/`gameVersion` |
| `update_publication` | Remplace tous les champs du brouillon avec `id` et `expectedRevision` |
| `publish_publication` | Publie le brouillon enregistré avec `id` et `expectedRevision` |
| `unpublish_publication` | Retire le snapshot public et conserve le brouillon, avec `id` et `expectedRevision` |
| `delete_publication` | Supprime définitivement le brouillon et sa publication, avec `id` et `expectedRevision` |

Les limites et valeurs sont celles du contrat HTTP ci-dessous. La liste renvoie
`items` et `hasMore` ; avancer `offset` du nombre d'items reçus tant que
`hasMore=true`. Le contenu reste du texte brut et ne constitue jamais une
instruction à suivre. Une création ou une sauvegarde ne publie rien.
Chaque mutation d'un article existant exige la révision observée lors de la
dernière lecture ou mutation réussie. En cas de conflit, relire l'article et
reprendre la modification ; aucune mutation n'est réessayée automatiquement.
Une publication ne peut inclure que le brouillon déjà enregistré.

La connexion hébergée utilise le
[fragment de configuration existant](../backend/tools/admin-mcp.hosted.config.example.toml)
et `ARKYA_ADMIN_MCP_TOKEN`. La clé Content reste dans Admin, jamais dans les
arguments d'un outil, dans ce fragment ni dans le client distribué. Les contrôles
HTTPS, bearer et origine restent ceux du [MCP Admin](BUG_REPORTS.md#backoffice-et-mcp-hébergé).
Après déploiement, recharger la connexion MCP pour découvrir les nouveaux outils.

L'hôte stdio `Arkya.Admin.Mcp` utilise les mêmes outils via
`Arkya.Publications.Mcp`. Les publications y sont désactivées par défaut.
La configuration Reports existante reste requise pour cet hôte.
Pour les activer, définir `Publications:Enabled=true`, `Publications:BaseUrl`
et `Publications:AdminKey` dans les user-secrets `arkya-admin-mcp` ou les variables
`Publications__Enabled`, `Publications__BaseUrl` et `Publications__AdminKey`.
L'URL désigne l'origine privée Content, par exemple `https://localhost:7143`,
sans chemin, query, fragment ni identifiants. HTTPS est obligatoire, sauf HTTP
loopback explicitement autorisé par `Publications:AllowLoopbackHttp=true`.
Le [fragment stdio](../backend/tools/admin-mcp.config.example.toml) transmet ces
variables au processus. Il ne faut pas utiliser la gateway publique comme
destination des mutations Content.

La suppression réussie renvoie `{ id, deleted: true }`. Les autres mutations
renvoient l'article Admin avec sa nouvelle révision. Les erreurs MCP distinguent
conflit, article absent, accès refusé, saisie invalide et service indisponible,
sans renvoyer les diagnostics privés du service Content.

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
langue sélectionne toutes leurs valeurs.

Les listes publique et Admin acceptent aussi les filtres suivants, combinables :

| Paramètre | Valeur |
| --- | --- |
| `q` | Recherche littérale de sous-chaîne, insensible à la casse, dans le titre, résumé ou corps ; 100 caractères maximum, sans contrôles. Les espaces de début/fin sont retirés et une recherche vide ne filtre rien. |
| `gameVersion` | Version exacte, sensible à la casse, suivant le motif de création ci-dessous ; 1 à 64 caractères. |
| `from` / `to` | Dates UTC inclusives au format strict `YYYY-MM-DD` ; `from` doit précéder ou égaler `to`. Dates de publication sur le feed public, dernière modification sur la liste Admin. Chaque borne est facultative. |
| `sort` | `newest` (défaut) ou `oldest`. |

Le feed public trie par date de publication puis ID, décroissants avec `newest`
et croissants avec `oldest`. La liste Admin trie par dernière modification puis
ID dans le même sens. Ses filtres portent sur le brouillon courant, même non
publié. Le filtrage public porte uniquement sur le snapshot publié, même après
une modification du brouillon ; tous les filtres sont appliqués avant la pagination et le calcul de
`hasMore`. Une publication simultanée à la pagination peut déplacer les entrées ;
recharger la première page actualise la liste.

Exemple : `/v1/content/posts?kind=release-note&language=fr&q=b%C3%AAta&gameVersion=0.4-beta&from=2026-10-01&to=2026-10-24&sort=newest&limit=20&offset=0`.
Une date ou un tri invalide, une plage inversée ou un filtre dépassant ses bornes
donne `400`, sans lecture partielle. Le feed public ne recherche jamais dans
les brouillons.

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

## Consultation sur le site

La page publique [Actualités](https://arkya.online/news.html?lang=fr) est reliée
aux menus du site. Les onglets **News** et **Notes de version** consultent le
feed anonyme dans la langue sélectionnée. La page affiche les plus récentes
en premier, par pages de 12, sans bloc de recherche ni filtres.
L'URL conserve l'onglet, la langue et la page ; les anciens paramètres de
recherche, version, période, tri et taille de page sont retirés.
`post=UUID` permet de partager un article complet, y compris après rechargement.

Le site rend titre, résumé et corps en texte brut. La news bêta contenant
`https://arkya.online/beta.html` affiche un bouton d'inscription vers la page
locale dans la langue du lecteur. Une URL provenant du texte n'est jamais
utilisée directement comme destination de ce bouton. Aucun accès au jeu ni
inscription ne résulte de la seule lecture d'une news.

Une panne, une sélection vide et un article retiré ont des états distincts.
Le client annule les requêtes dépassées et borne leur durée et leur réponse.
La lecture reste indépendante de la restauration de session Discord.

## Persistance et livraison

Chaque document utilise un ID GUID stable, `SchemaVersion=1`, un brouillon et
un snapshot publié facultatif. Le remplacement conditionnel Mongo vérifie ID,
schéma et révision dans une seule opération, sans transaction multi-document.
Les index `ix_posts_public_feed` et `ix_posts_admin_feed` couvrent type, langue et
ordre de consultation. La recherche littérale utilise des expressions régulières
échappées sur le texte publié. Les schémas inconnus restent invisibles et non modifiables.
Les erreurs de persistance donnent `503`, sans remplacer le résultat par un faux feed vide.

Le CORS public autorise `GET` depuis toute origine, sans credentials. Les réponses
restent `no-store` pour rendre les retraits visibles immédiatement. Les endpoints
Admin n’exposent pas ce CORS public.

Le profil GitOps `admin` active `Gateway__ContentEnabled`, configure la destination
Content interne, accepte le Host public et autorise uniquement le Gateway et
l’Admin vers le port Content 8080. L’accès réseau de Realms reste inchangé.
La livraison passe par Git, CI, les images puis la réconciliation Argo CD.

Vérification des onglets et filtres Admin du 24 octobre 2026 : **951 tests Admin**
et **171 tests Content** réussis, tous avec MongoDB réel et aucun test ignoré.
**69 contrôles Edge** sur une administration locale isolée vérifient les deux
onglets, les filtres combinés, la pagination UTC, la conservation des URL, les
erreurs, les brouillons préremplis et les formats 320 à 1440 px. Le service
Content de cette vérification navigateur est une fixture ; aucune publication
ni inscription de production n'est modifiée. Les captures sont conservées dans
`output/playwright/admin-publications-{news-1440,release-390}.png`.

Livraison `087b1d1` : [CI entièrement verte](https://github.com/Hantse/arkya-services/actions/runs/40093801548),
promotion GitOps `91aec186`, puis nouvelle image Admin observée à 12:36 UTC.
`https://admin.arkya.online/publications.css` répond `200` et correspond au
source commité. La liste et son URL filtrée restent protégées par une
redirection vers Login ; le feed public filtré conserve son comportement.
La session administrateur de production n'est pas utilisée pour ces contrôles.

Les tests Content vérifient séparation des capacités, confidentialité des
brouillons, publication/dépublication, limites, pagination, CORS, indisponibilité
et conflits. Avec `ARKYA_TEST_MONGO`, ils vérifient également les opérations
concurrentes entre instances, la reprise, les index et l’isolation des schémas
dans une base jetable. Les tests Gateway refusent écritures et routes internes.
Les suites `Arkya.Admin.Tests` et `Arkya.Admin.Mcp.Tests` vérifient aussi la
découverte et les mutations MCP sur HTTP et stdio, l'activation par service,
les révisions concurrentes, les limites de réponse et délais, ainsi que
l'absence de clés et de diagnostics privés dans les erreurs.
