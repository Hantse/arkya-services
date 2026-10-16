# Crédits des personnages — Arkya, études Blender V2 et V3

15 octobre 2026.

Les personnages des études V2 et V3 utilisent **Rain** et **Snow**, des créations de **Blender Studio / Blender Foundation**. Leur anatomie, leurs visages, leurs cheveux et leurs maillages de base ne sont pas des sculptures originales réalisées pour Arkya.

Les deux sources sont distribuées sous **[Creative Commons Attribution 4.0 International — CC BY 4.0](https://creativecommons.org/licenses/by/4.0/)**.

## Rain

- Création et publication : **Blender Studio / Blender Foundation**.
- Source : [Rain — page officielle Blender Studio](https://studio.blender.org/characters/rain/v2/).
- Téléchargement utilisé : [archive officielle Rain](https://studio.blender.org/download-source/files/ee/a7/eea73e55dba1cea31c09848df6a794b2-4.zip).
- Fichier source dans l’archive : `Rain v3.3/rain_v3.2.blend`. Le nom du dossier et celui du fichier portent des numéros de version différents ; ils sont conservés ici tels que fournis.
- Licence : [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/).

## Snow

- Création et publication : **Blender Studio / Blender Foundation**.
- Source : [Snow — page officielle Blender Studio](https://studio.blender.org/characters/snow/v4/).
- Téléchargement utilisé : [archive officielle Snow](https://studio.blender.org/download-source/files/ba/0f/ba0fe6d810333b1d73c1c359e4dc03cb-6.zip).
- Fichier source dans l’archive : `Snow/snow_v4.2.blend`.
- Licence : [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/).

## Adaptations pour Arkya

Les modèles sont adaptés pour les images de direction artistique : pose figée, calcul des déformations et des modificateurs puis conversion en maillages statiques, ajustements de matériaux, et ajout ou adaptation de tissus, armures et capes pour les propositions fantasy d’Arkya. Les environnements et leurs éléments procéduraux sont construits séparément pour ces études.

La V3 ajoute une réduction modérée et coordonnée des proportions de tête, visage, yeux et cheveux, avec une transition au cou. Les adaptations d’équipement comprennent :

- **Snow / Cités d’Éther :** épaulières articulées, incrustations et gemme de plastron, brassards, tassettes segmentées, protections de genoux et de jambes, sabatons et broderie de cape ; variations procédurales discrètes des matières. Les surfaces de pantalon cachées sous les bottes sont retirées pour éviter les chevauchements.
- **Rain / Terres des Anciens :** gilet de cuir ajusté et lacé, coutures, bandoulière, sacoches, épaulières en feuilles, bottes avec semelles et sangles, pans de tunique, cape plissée et brodée ; ajustements de teinte et de relief des matériaux.

Les scripts [Snow V3](scripts/refine_snow_v3.py) et [Rain V3](scripts/refine_rain_v3.py) consignent ces adaptations. Ils produisent des fichiers séparés, en conservant les assets V2.

Ces adaptations constituent des études de rendu. Elles ne constituent pas des personnages prêts pour le jeu : le rig, l’animation, les collisions, les LOD et les budgets d’intégration Unity ne sont pas validés.

## Attribution à conserver avec les adaptations

**Personnages adaptés de Rain et Snow, par Blender Studio / Blender Foundation, sous [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/). Sources : [Rain](https://studio.blender.org/characters/rain/v2/) et [Snow](https://studio.blender.org/characters/snow/v4/). Modifications pour Arkya : poses figées, conversion en maillages statiques, ajustements de proportions, vêtements, armures articulées, accessoires, capes, broderies et matériaux.**

Conserver ces crédits, les liens vers les sources et la licence, ainsi que l’indication des modifications, lors du partage des modèles adaptés et de leurs rendus. Cette réutilisation n’implique aucun soutien de Blender Studio ou de Blender Foundation au projet Arkya.
