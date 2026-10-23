# Items : backoffice, modèles Unity et publication

Le backoffice possède les définitions d'items : armes, armures, outils,
consommables, matériaux et objets de quête. Unity possède leurs modèles et
matériaux. Le flux est **Unity → modèles et vignettes dans le BE → brouillon
d'items → publication → synchronisation Unity avant build**.

Une publication fige les définitions et les références visuelles. Modifier
un brouillon ne modifie aucun catalogue déjà publié. Les clés Content restent
dans l'administration, l'Editor ou l'environnement de CI ; elles ne sont
pas incluses dans le client distribué.

## Préparer les modèles

Dans Unity, ouvrir **Arkya → Items → Content pipeline**. Configurer l'adresse
du service Content, puis sélectionner les prefabs ou modèles `.fbx` / `.obj`
à proposer aux créateurs d'items. La sélection d'un dossier permet de parcourir
ses modèles. Exporter les références visuelles vers Content.

L'export envoie un GUID Unity stable, le nom, le chemin dans `Assets/`, le type,
l'empreinte des dépendances et une vignette PNG produite par Unity. Il ne
transfère pas le fichier FBX/OBJ ni les textures au backend. Les originaux
fournisseurs restent à leur emplacement et les adaptations Arkya restent
séparées. Un FBX/OBJ représente ici son GameObject importé par Unity ; une
armure destinée à un personnage doit déjà disposer du rig et des points
d'attache appropriés.

L'Editor doit disposer d'un accès direct autorisé au service Content. La
gateway publique n'expose pas les routes de gestion et de synchronisation.
Pour le profil Kubernetes privé, utiliser un accès HTTPS interne approuvé ou
un port-forward local ; l'URL publique du backoffice n'est pas celle de Content.

## Créer et publier dans le BE

Ouvrir **Contenu et support → Objets**, puis créer un item. Renseigner son identifiant
stable, son nom, sa description, sa catégorie, sa rareté, son emplacement
d'équipement, ses contraintes de pile et ses statistiques. Choisir son modèle
dans la galerie avec aperçu. La recherche et la pagination limitent la liste
aux modèles utiles.

Enregistrer le brouillon, puis publier depuis la liste des items. La publication
porte sur tout le brouillon enregistré. L'historique conserve les versions
publiées. Un conflit de révision impose de recharger avant d'enregistrer ;
aucune écriture concurrente n'est écrasée automatiquement.

Conserver le même `itemId` pour les évolutions d'un item. Un autre identifiant
désigne une autre définition. La publication ne distribue aucun objet à un
personnage et n'accorde aucune statistique directement à un joueur.

## Synchroniser et construire

Dans la fenêtre Unity, synchroniser le catalogue publié. Sans version épinglée,
une synchronisation récupère la publication la plus récente une seule fois,
puis travaille sur ce snapshot. Pour reproduire un build, renseigner son UUID
de publication.

Unity résout chaque GUID localement et vérifie le chemin et l'empreinte des
dépendances avant d'écrire le catalogue. Un modèle manquant, déplacé ou modifié
fait échouer la synchronisation. Réexporter les modèles modifiés et publier
une nouvelle révision avant de reconstruire. Un vieux catalogue reste immuable.

L'empreinte est calculée sur les sources des dépendances et leurs `.meta`,
avec des chemins logiques et une normalisation des fins de ligne des fichiers
texte Unity. Elle ne dépend pas de la plateforme de build. Les ressources
intégrées à Unity sont liées à la version exacte de l'Editor. Exporter depuis
Windows n'impose donc pas de republier uniquement parce que le dedicated
est construit pour Linux ou le client pour WebGL.

Le catalogue Unity conserve les références aux GameObjects importés, afin
que leurs dépendances soient incluses dans le build. Il expose les métadonnées
et sa version aux systèmes de présentation et aux futurs systèmes de gameplay.

Le préprocesseur de build synchronise le catalogue lorsque le pipeline items
est configuré ou qu'un catalogue local existe. Une panne Content ou un modèle
incompatible bloque alors le build ; le cache n'est pas utilisé silencieusement.
Les anciens builds sans configuration et sans catalogue conservent leur
parcours actuel. Configurer ce pipeline sur chaque machine qui produit des
builds contenant les items. Épingler le même UUID pour les builds client et
serveur d'une même livraison.

Configuration de l'Editor ou de la CI :

| Variable | Usage |
| --- | --- |
| `ARKYA_ITEM_CONTENT_URL` | Adresse Content HTTPS ou HTTP loopback en local |
| `ARKYA_ITEM_ADMIN_KEY` | Clé `ContentAdmin:Key`, pour exporter les modèles |
| `ARKYA_ITEM_SERVICE_KEY` | Clé `ServiceAuthentication:Key`, pour lire une publication |
| `ARKYA_ITEM_CATALOG_VERSION` | UUID publié facultatif ; dernière publication si absent |

L'adresse et la version peuvent être conservées par l'Editor. Les clés saisies
dans la fenêtre ne durent que la session Editor ; elles ne sont pas sérialisées
dans les assets. Les deux clés ont des rôles distincts et ne sont pas
interchangeables.

## Contrats Content

Les routes `/admin/content` exigent exactement un header `X-Arkya-Admin-Key`.
Les routes `/internal/content` exigent exactement un header
`X-Arkya-Service-Key`. Une session joueur ne permet aucun accès.

| Méthode et route | Usage |
| --- | --- |
| `GET /admin/content/items/draft` | Brouillon `{ revision, items }` |
| `PUT /admin/content/items/draft` | Enregistrement complet avec révision attendue |
| `GET /admin/content/items/catalogs` | Historique des catalogues publiés |
| `POST /admin/content/items/catalogs` | Publication `{ draftRevision }`, idempotente par révision |
| `GET /internal/content/items/catalogs/latest` | Publication la plus récente |
| `GET /internal/content/items/catalogs/{version}` | Publication immuable épinglée |
| `GET /admin/content/item-assets?search=&limit=&offset=` | Références visuelles paginées, sans octets PNG |
| `GET /admin/content/item-assets/{guid}` | Métadonnées d'une référence |
| `PUT /admin/content/item-assets/{guid}` | Export d'une référence et de sa vignette |
| `GET /admin/content/item-assets/{guid}/thumbnail` | Vignette PNG |
| `DELETE /admin/content/item-assets/{guid}` | Retrait du choix proposé aux prochains brouillons |

Une définition contient `itemId`, `name`, `description`, `category`, `rarity`,
`equipmentSlot`, `visualAssetGuid`, `maxStack`, `weightGrams`, `requiredLevel`,
`attack` et `defense`. La publication ajoute le chemin `visualAssetPath` et
l'empreinte `visualDependencyHash` du modèle enregistré. Le catalogue utilise
`schemaVersion: 1`, un UUID `version` et une date UTC `publishedAt`.

Content possède les collections MongoDB des révisions d'items et des références
visuelles dans sa base `arkya_content`. Les révisions sont immuables ; la
publication fige leurs références visuelles. La suppression ou la mise à jour
d'une référence du registre ne réécrit pas les publications existantes.

## Vérification et périmètre

Les tests ciblés backend s'exécutent avec :

```powershell
dotnet test backend/tests/Arkya.Content.Tests/Arkya.Content.Tests.csproj
dotnet test backend/tests/Arkya.Admin.Tests/Arkya.Admin.Tests.csproj
```

Pour les tests de persistance, définir `ARKYA_TEST_MONGO` vers une instance
MongoDB de test jetable. Les tests créent et suppriment leurs propres bases.
Ne pas utiliser une base de production.

Les tests Edit Mode du module items vérifient les contrats et les références.
La compilation dans l'Editor, l'export d'un vrai modèle, l'inspection de sa
vignette et un build client/serveur constituent les vérifications Unity du flux.

### Vérification du 23 octobre 2026

- Content : **110 tests réussis**, avec MongoDB de test, dont concurrence,
  immutabilité, reprises et registre de vignettes.
- Admin : **634 tests réussis**, puis son test MongoDB **1/1 réussi** ; les
  32 nouveaux tests d'items sont inclus dans cette suite.
- Navigateur : création d'une arme et d'une armure, recherche et sélection de
  vignettes, publication et relecture du snapshot dans Content. Les captures
  desktop et mobile utilisent trois références visuelles synthétiques dans
  une base jetable ; elles ne constituent pas un export de modèles Unity réels.
- Sources Unity : compilation des sources et tests du module contre les
  assemblies installées de Unity `6000.6.6f1`, puis **54 vérifications isolées
  réussies**. Cette compilation externe ne remplace pas celle de l'Editor.
- Le MCP a retourné zéro instance Unity. Les quatre tests Edit Mode utilisant
  l'AssetDatabase, l'export réel avec miniature et le build restent à exécuter
  dans l'Editor connecté.

Ce module fournit le contenu et sa distribution au build. L'inventaire ECS
historique utilise encore ses identifiants numériques propres. L'équipement
sur un personnage, les règles de combat, le loot et la persistance d'instances
d'items nécessitent leur intégration serveur autoritaire ; ils ne sont pas
activés par la publication d'une définition.
