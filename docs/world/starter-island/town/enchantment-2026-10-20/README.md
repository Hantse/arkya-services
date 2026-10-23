# Aubefont — magie quotidienne et façades fleuries

Cette passe donne au village une présence magique jusque dans ses rues :
**dix lanternes de mana jade et ambre** flottent autour de la place et près de
quatre façades. Leur lévitation lente accompagne les ornements runiques des
pignons et des deux portes.

Les dix maisons reçoivent des crépis pastel dérivés localement, des lianes
fleuries lilas et roses et **vingt et une jardinières** supplémentaires. Les
bouquets réemploient la géométrie et les textures peintes des fleurs approuvées,
au niveau de détail LOD2. Les nouvelles compositions restent une proposition
artistique ; les références DA approuvées ne sont pas modifiées.

Les lanternes utilisent des matériaux émissifs et des animations locales.
Cette passe ajoute **aucune lumière ponctuelle, aucun collider et aucun état de
gameplay**.

## Comparaison

Les six cadrages sont identiques avant et après : place, marché et vue générale,
chacun de jour et au crépuscule. La revue restaure la caméra, l’heure, la météo,
les lumières, les particules et les paramètres de rendu.

| Vue | Avant | Après |
| --- | --- | --- |
| Place, jour | [Image](before/plaza-street-day.png) | [Image](after/plaza-street-day.png) |
| Place, soir | [Image](before/plaza-street-evening.png) | [Image](after/plaza-street-evening.png) |
| Marché, jour | [Image](before/market-street-day.png) | [Image](after/market-street-day.png) |
| Marché, soir | [Image](before/market-street-evening.png) | [Image](after/market-street-evening.png) |
| Ensemble, jour | [Image](before/village-overview-day.png) | [Image](after/village-overview-day.png) |
| Ensemble, soir | [Image](before/village-overview-evening.png) | [Image](after/village-overview-evening.png) |

## Sources et budget

La racine **Town enchantment - floating lanterns and painted gardens** appartient
à **Aubefont - mana well town**, dans la scène `StarterIsland` du projet actif
`Unity3D/Arkya`. Ses ressources sont isolées sous
`Assets/Arkya/World/StarterIsland/Town/Enchantment20261020/`.

[EnchantStarterVillage.cs](../../../../../Unity3D/Arkya/AgentScripts/World/EnchantStarterVillage.cs)
porte l’authoring.
[ReviewVillageEnchantment.cs](../../../../../Unity3D/Arkya/AgentScripts/World/ReviewVillageEnchantment.cs)
conserve la référence initiale, produit les captures et contrôle les ressources.
[ValidateVillageEnchantmentMotion.cs](../../../../../Unity3D/Arkya/AgentScripts/World/ValidateVillageEnchantmentMotion.cs)
observe l’animation en Play Mode.
[TuneVillageEnchantmentGlow.cs](../../../../../Unity3D/Arkya/AgentScripts/World/TuneVillageEnchantmentGlow.cs)
permet une retouche ciblée des matériaux émissifs. Leur indicateur
`BakedEmissive` conserve l’émission sous URP 17.6 ; les renderers ne contribuent
pas à la GI statique et aucun bake d’éclairage n’est ajouté.

Le [rapport d’authoring](authoring.json) compte **93 384 triangles et
64 renderers**, pour un budget fixé à 100 000 triangles et 80 renderers. Les
plantes sont regroupées par quartier et matériau ; les dix lanternes disposent
de boucles de lévitation. Ces comptes ne mesurent pas les performances du client
final.

## Vérification

Les **douze contrôles en Edit Mode** du [rapport de validation](validation.json)
réussissent : géométrie valide, matériaux et shaders supportés, aucun script
manquant, `.meta` des ressources référencées présents et budget respecté.
Les empreintes des hauteurs et de la pose du Terrain ainsi que de la géométrie
et de la pose des colliders sont identiques à la [référence initiale](baseline.json).
La scène conserve ses **2 499 colliders**, dont 2 270 dans la ville, et la ville
ses **21 lumières**.

Le [contrôle en Play Mode](runtime-validation.json) confirme le mouvement
automatique des **dix lanternes sur dix**, visibles pendant l’observation :
14,04 secondes de temps de jeu, avec progression réelle des frames et sans
échantillonnage manuel des animations. L’émission des matériaux jade et ambre
est également active. L’éditeur est revenu en Edit Mode ; les douze contrôles
de ressources et de conservation réussissent à nouveau.

La [lecture finale de console](console-validation.json) ne relève aucune
nouvelle erreur ni aucun avertissement pendant ce dernier parcours et ses
captures. Un appel de rendu explicite dans le premier outil de mesure provoquait
des erreurs RenderPass ; il a été retiré, puis le contrôle complet a été relancé
avec le rendu naturel du jeu.

Aucun build joueur, nouveau test hôte/client ni test de performance n’a été
exécuté pour cette passe.
