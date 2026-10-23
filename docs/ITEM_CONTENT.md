# Items : backoffice, modèles Unity et publication

Le backoffice possède les définitions d'items : armes, armures, outils,
consommables, matériaux et objets de quête. Unity possède leurs modèles et
matériaux. Le flux est **Unity → modèles et vignettes dans le BE → brouillon
d'items → publication → synchronisation Unity avant build**.

Une publication fige les définitions, leurs sources de loot et les références visuelles. Modifier
un brouillon ne modifie aucun catalogue déjà publié. Les clés Content restent
dans l'administration, l'Editor ou l'environnement de CI ; elles ne sont
pas incluses dans le client distribué.

## Préparer les modèles

Dans Unity, ouvrir **Arkya → Items → Content pipeline**. Configurer l'adresse
d'export visuel `https://admin.arkya.online`, puis sélectionner les prefabs ou modèles `.fbx` / `.obj`
à proposer aux créateurs d'items. La sélection d'un dossier permet de parcourir
ses modèles. Exporter les références visuelles vers Content.

L'export envoie un GUID Unity stable, le nom, le chemin dans `Assets/`, le type,
l'empreinte des dépendances et une vignette PNG produite par Unity. Il ne
transfère pas le fichier FBX/OBJ ni les textures au backend. Les originaux
fournisseurs restent à leur emplacement et les adaptations Arkya restent
séparées. Un FBX/OBJ représente ici son GameObject importé par Unity ; une
armure destinée à un personnage doit déjà disposer du rig et des points
d'attache appropriés.

L'export visuel passe par l'API HTTPS du backoffice, activée par
`ItemVisualExport:Enabled` dans le profil Kubernetes Admin. Elle accepte
uniquement `PUT /admin/content/item-assets/{guid}`, avec exactement un header
`X-Arkya-Admin-Key` contenant la clé Content de l'Editor. Le serveur Admin
relaie cette inscription au service Content privé. La gateway ne conserve
ce header que pour un `PUT` HTTPS sur l'hôte Admin central et un GUID Unity
valide. Les autres clés internes
et les autres routes restent filtrées. La requête JSON est limitée
à 512 Kio ; Content vérifie le chemin, l'empreinte et la vignette PNG. Un cookie
de connexion au backoffice ou une session joueur ne remplace pas cette clé.
Les appels doivent provenir d'un client d'export sans header `Origin` ;
aucune permission CORS n'est accordée aux sites web.
L'API limite les appels à 120 par minute et par adresse cliente.

L'adresse d'export est séparée de l'adresse Content utilisée pour la
synchronisation des catalogues. Configurer seulement l'export n'active pas
la synchronisation avant build. Celle-ci nécessite toujours un accès privé
autorisé au service Content ; ses routes de gestion et de publication ne sont
pas exposées par cette API. Sans adresse d'export spécifique, l'export utilise
l'adresse Content pour conserver le fonctionnement des installations locales.

## Créer et publier dans le BE

Ouvrir **Items** dans le backoffice. La bibliothèque affiche les vignettes,
la catégorie et l'emplacement, la rareté, le niveau requis et les mobs liés
avec leur pourcentage. La barre de publication conserve la révision et le
dernier catalogue à portée de main ; l'historique et ses téléchargements
restent accessibles dans **Published versions**.

La recherche porte sur le nom et l'identifiant. Les filtres se combinent :
catégorie, rareté, niveau minimum/maximum, mob et présence ou absence de
sources de loot. Le tri propose le nom, le niveau et la rareté. La pagination
affiche 24 items par page et conserve les filtres dans l'URL ; **Reset**
rétablit toute la bibliothèque. Ces filtres ne limitent pas la publication,
qui inclut toujours le brouillon complet enregistré.

Créer un item avec **Create an item**. Renseigner son identifiant
stable, son nom, sa description, sa catégorie, sa rareté, son emplacement
d'équipement, ses contraintes de pile et ses statistiques. Choisir son modèle
dans la galerie avec aperçu. La recherche et la pagination limitent la liste
aux modèles utiles.

Dans **Mob loot sources**, choisir un mob et saisir sa probabilité en pourcent,
de **0,01 à 100 %**, avec deux décimales au maximum. Par exemple, `12.5` ou
`12,5` représente 12,5 %. **Link a mob** ajoute une source et **Remove** la
retire ; une ligne entièrement vide est ignorée. Un mob ne peut apparaître
qu'une fois par item. Les pourcentages décrivent chaque source séparément,
sans somme imposée entre les mobs. Un item peut avoir jusqu'à 32 sources.

Le choix provient du registre de mobs pris en charge par Content. Pour la
tranche actuelle, il contient uniquement **Prototype monster**,
`prototype-monster`, l'identité déjà utilisée par les quêtes du prototype.
Les noms libres et les identifiants inconnus sont refusés. Importer un modèle
Unity ne crée pas un nouveau type de mob dans ce registre.

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
Il conserve aussi les associations `dropSources`, avec leurs identifiants de
mobs et probabilités exactes. La validation Unity refuse les doublons, les
identifiants mal formés et les valeurs hors limites ; les anciennes publications
sans ce champ restent valides et fournissent une liste vide.

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
| `ARKYA_ITEM_VISUAL_EXPORT_URL` | Origine HTTPS du backoffice pour l'export, ou origine Content si absent |
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
| `GET /admin/content/item-mobs` | Mobs autorisés pour les sources de loot : `[{ mobId, name }]` |
| `GET /internal/content/items/catalogs/latest` | Publication la plus récente |
| `GET /internal/content/items/catalogs/{version}` | Publication immuable épinglée |
| `GET /admin/content/item-assets?search=&limit=&offset=` | Références visuelles paginées, sans octets PNG |
| `GET /admin/content/item-assets/{guid}` | Métadonnées d'une référence |
| `PUT /admin/content/item-assets/{guid}` | Export d'une référence et de sa vignette |
| `GET /admin/content/item-assets/{guid}/thumbnail` | Vignette PNG |
| `DELETE /admin/content/item-assets/{guid}` | Retrait du choix proposé aux prochains brouillons |

Une définition contient `itemId`, `name`, `description`, `category`, `rarity`,
`equipmentSlot`, `visualAssetGuid`, `maxStack`, `weightGrams`, `requiredLevel`,
`attack`, `defense` et `dropSources`. La publication ajoute le chemin `visualAssetPath` et
l'empreinte `visualDependencyHash` du modèle enregistré. Le catalogue utilise
`schemaVersion: 1`, un UUID `version` et une date UTC `publishedAt`.

`dropSources` est un tableau facultatif à l'enregistrement. Chaque entrée
contient un `mobId` du registre et un entier `chanceBasisPoints` de 1 à 10 000.
Un point vaut **0,01 %** : `1` vaut 0,01 %, `1250` vaut 12,5 % et `10000` vaut
100 %. Le contrat utilise ces entiers pour éviter les arrondis de virgule
flottante. Exemple de champ d'une définition :

```json
{
  "dropSources": [
    { "mobId": "prototype-monster", "chanceBasisPoints": 1250 }
  ]
}
```

Les entrées nulles, les doublons, les mobs inconnus, les valeurs hors limites
et les tableaux de plus de 32 sources sont refusés. `[]` retire les associations
du prochain brouillon. L'absence du champ ou `null` dans une requête signifie
également aucune source ; les clients qui modifient un item doivent donc
retransmettre ses sources pour les conserver. Les réponses renvoient toujours
un tableau. Les documents MongoDB historiques sans ce champ sont lus avec
une liste vide, sans migration ni modification des versions publiées.
L'ajout est compatible avec le schéma 1 et les sources sont figées avec
chaque publication.

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

- API publique d'export : **44 tests réussis** (clé, HTTPS, quota par adresse,
  corps bornés, erreurs Content et annulation). Suite Admin : **725 tests
  réussis, 1 ignoré** sans MongoDB de test avant le dernier ajustement de quota.
- Gateway : **263 tests réussis**, dont 24 contrôlant le passage de la clé
  uniquement sur l'export Admin et sa suppression sur les autres routes.
- Editor connecté : **89 tests Edit Mode du module items réussis**, après
  compilation dans Unity `6000.6.6f1`. Les 21 prefabs d'armes ont été préparés
  avec leur empreinte de sources et une vraie vignette Unity.
- Refonte liste/éditeur et loot : **773 tests Admin réussis**, avec MongoDB.
  Vérification navigateur sur une base jetable avec les 21 vraies références
  visuelles : filtre des sources, ajout/retrait, saisie `7,25` conservée comme
  `7.25`, rejet de `0.001`, affichages bureau et mobile sans débordement global.
  Ces items de démonstration ne sont pas ajoutés au catalogue de production.

- Content : **126 tests réussis**, dont six avec MongoDB de test. Ils couvrent
  la concurrence, les snapshots immuables, les reprises, le registre de vignettes,
  les probabilités exactes, les sources invalides et la compatibilité des
  documents historiques après redémarrage.
- Vérification navigateur initiale : création d'une arme et d'une armure, recherche et sélection de
  vignettes, publication et relecture du snapshot dans Content. Les captures
  desktop et mobile utilisent trois références visuelles synthétiques dans
  une base jetable ; elles ne constituent pas un export de modèles Unity réels.
- Le build avec un catalogue d'items publié reste à vérifier ; les contrôles
  d'export et de références ne valident pas à eux seuls cette intégration.

Ce module fournit le contenu et sa distribution au build. L'inventaire ECS
historique utilise encore ses identifiants numériques propres. L'équipement
sur un personnage, les règles de combat, le tirage du loot à la mort d'un mob
et la persistance d'instances d'items nécessitent leur intégration serveur
autoritaire. L'association et sa probabilité sont enregistrées et publiées,
mais elles ne déclenchent pas encore de récompense en jeu.
