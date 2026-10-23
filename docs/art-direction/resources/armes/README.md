# Armes et boucliers — collection 3D

Cette collection adapte en **21 objets 3D statiques** les
[concepts du 23 octobre 2026](concepts-2026-10-23/README.md), approuvés par
l'utilisateur avant sa demande d'objets Unity. Les silhouettes, les volumes
biseautés, le bois et le cuir, les garnitures dorées et les accents jade ou
turquoise reprennent les sept planches. La finition 3D utilise des pigments
aux sommets ; elle constitue une adaptation des illustrations.

## Catalogue

Chaque identifiant conserve la catégorie et la variante du concept. Les liens
ci-dessous ouvrent le rendu de contrôle correspondant.

| Catégorie | Variante A | Variante B | Variante C |
|---|---|---|---|
| Épées à une main | [E1-A](models/E1-A/E1-A-preview.png) | [E1-B](models/E1-B/E1-B-preview.png) | [E1-C](models/E1-C/E1-C-preview.png) |
| Épées à deux mains | [E2-A](models/E2-A/E2-A-preview.png) | [E2-B](models/E2-B/E2-B-preview.png) | [E2-C](models/E2-C/E2-C-preview.png) |
| Haches à une main | [H1-A](models/H1-A/H1-A-preview.png) | [H1-B](models/H1-B/H1-B-preview.png) | [H1-C](models/H1-C/H1-C-preview.png) |
| Haches à deux mains | [H2-A](models/H2-A/H2-A-preview.png) | [H2-B](models/H2-B/H2-B-preview.png) | [H2-C](models/H2-C/H2-C-preview.png) |
| Boucliers | [B-A](models/B-A/B-A-preview.png) | [B-B](models/B-B/B-B-preview.png) | [B-C](models/B-C/B-C-preview.png) |
| Bâtons | [BT-A](models/BT-A/BT-A-preview.png) | [BT-B](models/BT-B/BT-B-preview.png) | [BT-C](models/BT-C/BT-C-preview.png) |
| Arcs | [AR-A](models/AR-A/AR-A-preview.png) | [AR-B](models/AR-B/AR-B-preview.png) | [AR-C](models/AR-C/AR-C-preview.png) |

A privilégie les assemblages acier, bois et cuir ; B les courbes organiques,
le jade et l'ivoire ; C le cristal turquoise et les montures dorées. Ces lettres
ne définissent aucune faction, rareté ou statistique.

## Fichiers et repères

Chaque dossier `models/<ID>/` contient :

- `<ID>.blend` : construction Blender éditable, avec les pièces séparées et
  les repères d'attache ;
- `<ID>.fbx` : géométrie destinée à Unity, avec trois maillages nommés
  `<ID>_LOD0`, `<ID>_LOD1`, `<ID>_LOD2` et les repères ;
- `<ID>.glb` : échange portable du LOD0 avec ses matières, pigments et repères ;
- `<ID>.json` : dimensions, catégories, repères, budgets par LOD, contrôles
  géométriques et empreintes SHA-256 des trois formats ;
- `<ID>-preview.png` : rendu Blender de contrôle.

Les unités sont les **mètres**. Le pivot commun est le centre de la prise
principale, matérialisé par `Grip_Primary` à `(0, 0, 0)`. Dans Blender, le haut
est **+Z** et la face principale **−Y**. La convention attendue après conversion
FBX dans Unity est **+Y vers le haut et +Z vers la face principale**, avec le
même pivot et la même échelle physique.

`Grip_Secondary` existe sur les épées et haches à deux mains ainsi que sur
les bâtons. `Tip` repère l'extrémité principale. Les boucliers possèdent
également `Forearm` ; les arcs possèdent des repères dédiés à la pose de la
flèche. Les coordonnées exactes et la liste complète sont dans chaque JSON.
Ces repères préparent un raccordement d'équipement ou d'animation.

Les LOD1 et LOD2 visent respectivement 52 % et 24 % du nombre de triangles
du LOD0. Les nombres réellement exportés figurent dans les JSON. Les formes
secondaires restent géométriques : les modèles possèdent une épaisseur et un
dos construits, y compris les montures, les poignées et les attaches.

## Matières et intégration Unity

Les pigments sont enregistrés dans l'attribut de couleur `Painted` et
transportés dans les formats d'échange. Les cinq familles de matériaux sont
partagées par toute la collection :

| Matériau | Surfaces |
|---|---|
| `Arkya_Weapon_Steel` | Acier et tranchants |
| `Arkya_Weapon_Gold` | Or et bronze des montures |
| `Arkya_Weapon_Organic` | Bois, cuir, ivoire et corde |
| `Arkya_Weapon_Jade` | Incrustations jade |
| `Arkya_Weapon_Crystal` | Cristal turquoise et pierre ambrée |

Un modèle utilise seulement les familles dont il a besoin. Les couleurs sont
portées par les sommets, sans textures externes requises. Le rendu Unity
réutilise le shader URP existant `Arkya/Characters/Painted`, défini dans
`Assets/Arkya/Characters/Shaders/PaintedCharacter.shader`.

L'intégration est préparée sous `Assets/Arkya/Items/Weapons/` dans le projet
`Unity3D/Arkya`. L'outil
[`WeaponConceptImporter`](../../../../Unity3D/Arkya/Assets/Arkya/Items/Weapons/Editor/WeaponConceptImporter.cs)
importe les FBX, remappe les cinq matériaux et crée un prefab par identifiant.
Le [guide Unity](../../../../Unity3D/Arkya/Assets/Arkya/Items/Weapons/README.md)
décrit l'import et les contrôles effectués par l'outil.

Cette livraison concerne la présentation : maillages statiques, matières,
repères et LOD. Elle n'ajoute ni squelette, ni animation, ni collider de
gameplay, ni règles d'équipement, combat ou inventaire, ni publication au
catalogue serveur. La corde des arcs reste statique.

## Régénération

Exécuter depuis la racine du dépôt de services `D:/Repositories/arkya` avec
Blender 4.5.14 LTS. Le générateur conserve les sources dans ce dossier,
hors du projet Unity.

```powershell
$weaponCodes = @(
    'E1-A', 'E1-B', 'E1-C', 'E2-A', 'E2-B', 'E2-C',
    'H1-A', 'H1-B', 'H1-C', 'H2-A', 'H2-B', 'H2-C',
    'B-A', 'B-B', 'B-C', 'BT-A', 'BT-B', 'BT-C',
    'AR-A', 'AR-B', 'AR-C'
)
& 'D:/Tools/BlenderPortable/blender-4.5.14-windows-x64/blender.exe' --background --threads 6 --disable-autoexec --python-exit-code 1 --python 'docs/art-direction/resources/armes/scripts/weapon_common.py' -- --codes $weaponCodes
```

Pour une seule variante, remplacer la liste par son identifiant ; ajouter
`--no-render` après les identifiants pour omettre les previews. Les géométries
sont définies dans [swords.py](scripts/swords.py), [axes.py](scripts/axes.py)
et [support_weapons.py](scripts/support_weapons.py). Les pigments, exports,
LOD et rendus sont centralisés dans [weapon_common.py](scripts/weapon_common.py).

Après une modification, vérifier les sources et les deux formats d'échange :

```powershell
& 'D:/Tools/BlenderPortable/blender-4.5.14-windows-x64/blender.exe' --background --threads 6 --disable-autoexec --python-exit-code 1 --python 'docs/art-direction/resources/armes/scripts/validate_weapons.py'
```

Le validateur produit [validation.json](validation.json). Il contrôle les
21 identifiants, les maillages fermés et finis, les triangles d'aire positive,
les UV et pigments, la réduction des trois LOD, l'échelle, les repères et les
empreintes. Il relit les GLB indépendamment et réimporte chaque FBX dans
Blender pour comparer ses positions à la source. Une sélection `-- --codes
H1-A H1-B H1-C` produit un rapport partiel séparé.

## État de vérification

Les previews Blender ont fait l'objet d'un contrôle visuel pendant la
construction. Le résultat technique à retenir est le statut et le détail
du dernier `validation.json` ; une régénération nécessite un nouveau contrôle.
La validation technique hors ligne ne confirme pas le rendu dans Unity.

**Vérification Unity effectuée le 23 octobre 2026**, dans l'Editor 6000.6.6f1
via CoplayDev Unity MCP : compilation réussie, 21 prefabs importés puis relus
avec `LoadPrefabContents`, 63 LOD contrôlés, pigments présents, matériaux
compatibles, échelle en mètres et prises à l'origine. La console ne contient
aucune erreur à la fin du contrôle. Les FBX du projet correspondent aux sources
par SHA-256 ; les assets et dossiers disposent de leurs `.meta` Unity.

La scène `Assets/Arkya/Items/Weapons/Scenes/WeaponGallery.unity` rassemble la
collection. Cette capture provient de sa caméra URP, en 2400 × 1800 avec LOD0
forcé pendant le rendu uniquement. Le fonctionnement automatique des LOD est
rétabli ensuite. Le [guide Unity](../../../../Unity3D/Arkya/Assets/Arkya/Items/Weapons/README.md)
détaille le périmètre de la livraison.

![Les 21 objets dans Unity URP](unity-gallery.png)
