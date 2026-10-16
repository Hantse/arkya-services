# Troncs couchés — référence DA validée

**Référence DA validée par l'utilisateur le 15 octobre 2026.**
L'approbation couvre les **12 variantes** : quatre formes/tailles et trois
matières dans le langage peint de l'arbre V3. La collection rejoint les
références approuvées pour le bois et les objets d'environnement.

| Forme | Longueur | Triangles | Construction |
|---|---:|---:|---|
| Billot court | 0,90 m | 7 040 | Bois trapu, coupes arrondies et cernes décentrés |
| Tronc noueux | 1,80 m | 10 906 | Cambrure discrète, renflements et moignon latéral |
| Tronc creux | 2,80 m | 10 752 | Cavité traversante réelle, écorce épaisse et bois intérieur |
| Grand tronc fourchu | 4,20 m | 13 898 | Fourche intégrée et extrémités rompues sculptées |

Les trois matières sont **Ancien moussu**, **Mystique** et **Bois brûlé**.
Les fibres de bois restent larges et peintes. La mousse couvre des nappes
irrégulières sur les faces supérieures ; magie et braises restent localisées.
Les cernes sont irréguliers et décentrés, avec quelques fissures peintes.

Chaque variante fournit un `.blend` éditable, un `.glb` portable et un aperçu
1100 × 1100. Les cartes 2K couleur, rugosité et émission sont incorporées
aux deux formats et conservées séparément dans `textures/`.
Les modèles utilisent un matériau opaque, des unités en mètres et un pivot
centré au sol. Leur longueur correspond à l'axe X dans Blender.
Les trois matières conservent la même géométrie et les mêmes UV.

La [source d'écorce approuvée](../references/arbre-peint-v3/source/painted-bark.png)
est réutilisée en lecture seule ; sa [provenance](../references/arbre-peint-v3/source/painted-bark-provenance.md)
est conservée. Les sections de bois, les nappes de mousse, les colorations
et les accents lumineux sont construits par le générateur de cette famille.
Aucun fichier de la référence validée n'est modifié.

- [Catalogue des 12 variantes](manifest.json)
- [Comparaison des longueurs à la même échelle](comparaison-tailles.png)
- [Scène Blender de comparaison](comparaison-tailles.blend)
- [Rapport de validation](validation.json)
- [Générateur Blender](scripts/generate_logs.py)
- [Rendu de comparaison](scripts/render_comparison.py)
- [Contrôle des exports](scripts/validate_logs.py)
- [Ressources DA](../README.md)

Reproduction dans Blender 4.5 :

```text
blender --background --python scripts/generate_logs.py -- --shape all --material all --render --gpu
blender --background --python scripts/render_comparison.py -- --gpu
blender --background --python scripts/validate_logs.py
```

Le budget est limité à 35 000 triangles par modèle. Les contrôles vérifient
les parois fermées, les normales et UV finis, les dimensions, le pivot,
les textures incorporées et l'identité de géométrie entre matières.
Les 12 modèles passent aussi le contrôle de composant connecté unique et
de volume solide positif ; la cavité traversante conserve des parois fermées.
Les colliders, LOD et l'intégration Unity restent hors de cette livraison DA.
