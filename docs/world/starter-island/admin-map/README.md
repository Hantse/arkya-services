# Carte du relief de l’Île de l’Aube

L’administration utilise [starter-island.png](../../../../backend/src/Arkya.Admin/wwwroot/images/islands/starter-island.png),
une carte calculée à partir de l’export de hauteur réel de l’île. Ce n’est pas
une capture 3D : les bâtiments, arbres, objets et états de gameplay ne figurent
pas sur cette image. La palette et l’éclairage du relief servent uniquement à
la lecture des altitudes. La grille et ses libellés sont dessinés par l’Admin.

## Emprise et orientation

- Emprise exacte de l’image : **X et Z de 0 à 1 536 m**.
- **+X vers la droite, +Z vers le haut**, origine en bas à gauche.
- Image carrée de **1 024 × 1 024 pixels**, sans marge ni légende intégrée.
- Mer à **Y = 28 m** ; hauteur normalisée par **192 m**.
- Export source de **512 × 512 échantillons**, soit un pas d’environ 3 m.

La position `(0, 0, 0)` et la taille `(1536, 192, 1536)` du Terrain ont été
vérifiées dans Unity `6000.6.6f1` via MCP le 23 octobre 2026. Une image ne doit
être alignée sur la grille que pour ce contenu et cette emprise. Pour un autre
plan d’île, elle reste une illustration et ne permet pas de localiser ses
partitions.

## Source et reproduction

Le fichier [CoastalHeights.asset](../../../../Unity3D/Arkya/Assets/Arkya/World/StarterIsland/Terrain/CoastalHeights.asset)
est une texture Unity **R16 non compressée**, sérialisée en YAML. Le code
`CreateOcean` et `RefreshCoastalHeights` de
[SculptStarterIsland.cs](../../../../Unity3D/Arkya/AgentScripts/World/SculptStarterIsland.cs)
écrit `pixels[z * width + x] = GetInterpolatedHeight(x / 511, z / 511) / 192`.
Le générateur lit ces valeurs numériques, applique une palette d’altitude et
dessine le relief avec Matplotlib ; il ne modifie aucun asset Unity.

Depuis la racine du dépôt, avec Python, NumPy et Matplotlib disponibles :

```powershell
python docs/world/starter-island/admin-map/generate_relief.py
```

Le script remplace uniquement le PNG Admin et [metadata.json](metadata.json).
Ce dernier conserve les empreintes SHA-256 de l’export, du layout et de l’image,
l’emprise, l’orientation et les contrôles numériques. Les points d’arrivée et
de départ du layout servent à vérifier le décodage : écarts de **0,018 m** et
**0,022 m** lors de cette génération. Le bord du terrain reste intégralement
sous l’eau, avec une altitude maximale de **12,094 m**.

Après toute modification du relief, actualiser d’abord `CoastalHeights.asset`
dans Unity selon [le guide de l’île](../../STARTER_ISLAND.md#ouvrir-modifier-et-reproduire),
puis relancer le générateur et examiner l’image et ses métadonnées. Une nouvelle
origine, taille, échelle verticale ou résolution exige de revoir les contrôles
du script et le catalogue Admin. Ces vérifications ponctuelles ne prouvent pas
que l’export a été actualisé après chaque retouche ; son actualisation reste une
étape obligatoire. Cette carte n’est pas une donnée de collision.

## Vérification de cette livraison

Le PNG a été inspecté visuellement. L’ancienne `overview.png` est une vue
orthographique inclinée de la sculpture initiale ; elle n’est pas utilisée
comme carte géoréférencée.

Une tentative unique de capture verticale dans une scène Unity de preview a
produit un rendu corrompu, exclu de la livraison, puis la connexion Editor a
disparu. Aucun redémarrage de Unity n’a été tenté. Aucun Play Mode, serveur ou
enregistrement de scène n’a été lancé. La scène utilisateur était propre avant
la tentative ; la remise en état finale n’a pas pu être observée après la perte
de connexion. La génération définitive ci-dessus ne dépend pas de l’Editor.
