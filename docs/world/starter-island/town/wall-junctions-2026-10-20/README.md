# Raccords des courtines — 20 octobre 2026

Le signalement montre une entaille verticale en V entre deux panneaux de
muraille. Le relevé dans Unity confirme que les panneaux sont toujours à
leur position prévue : leurs extrémités rectangulaires n'épousent pas les
changements de direction du tracé.

Chaque panneau dépassait son segment de 9 cm à chaque bout. Au coude 74,
l'angle de 29,425° et la demi-épaisseur de 2,20 m demandent un prolongement
extérieur d'environ 57,8 cm ; il manquait donc près de 48,8 cm. La continuité
du chemin au centre pouvait réussir les contrôles de déplacement tout en
laissant cette entaille visible sur la façade.

Le [relevé des jonctions](layout.json) conserve les positions et les écarts
géométriques. Les [captures avant](before/captures.json) montrent les trois
coudes les plus prononcés, 74, 93 et 46, ainsi que le chemin de ronde.

La [correction exécutée](repair.json) ajuste les bouts des murs, du pavage, des parapets, des
couronnements et des joints horizontaux sur les bissectrices des jonctions.
Elle emploie des copies des maillages regroupés afin de conserver les
ressources d'origine et les autres éléments de la ville.

Elle traite **102 jonctions et 928 primitives**, dans **18 maillages dérivés**.
Le nombre de triangles rendus, de renderers et de matériaux reste identique.
Les transforms, escaliers, guets, tours de porte et ouvertures des escaliers
sont conservés. Les 3 624 contrôles analytiques de bout confirment au moins
4,11 cm de recouvrement des faces planes après prise en compte du biseau.

| Vue | Avant | Après |
| --- | --- | --- |
| Coude 74 | [Image](before/74-outside-day.png) | [Image](after/74-outside-day.png) |
| Coude 93 | [Image](before/93-outside-day.png) | [Image](after/93-outside-day.png) |
| Coude 46 | [Image](before/46-outside-day.png) | [Image](after/46-outside-day.png) |
| Chemin de ronde | [Image](before/wall-walk-junction-day.png) | [Image](after/wall-walk-junction-day.png) |

Les [mesures avant](geometry-before.json) sont volontairement conservées en
échec : les rayons qui traversent les entailles trouvent la façade jusqu'à
1,92 m en retrait. Trois sondes d'appui extérieures échouent aussi, une par
coude examiné, à 1,10 m du centre du chemin. Le raccord physique de ces petits
vides est contrôlé séparément du rendu.

Les [14 petits appuis ajoutés](supports.json) remplissent uniquement les
volumes manquants entre les collisions existantes. Ces prismes statiques
comptent 168 triangles de collision au total, sans renderer. Les anciennes
collisions sont conservées ; les 125 sondes précédemment sans appui trouvent
désormais un support. L'audit indépendant vérifie les 14 volumes contre les
plans des murs et teste 126 points d'appui.

La [finition des pieds de mur](foot-contacts.json) raccorde trois paires de
bandes existantes : 24 sommets d'extrémité dans trois copies de maillages,
sans ajout de géométrie, de matériau ou de collision. Les interruptions
volontaires près des portes et des accès sont conservées.

## Vérification finale

- [Géométrie : 17/17 contrôles réussis](geometry-after.json), dont 81 rayons
  sur les façades à trois hauteurs, 81 sondes de pavage et 81 sondes de
  collision. Le retrait maximal aux raccords examinés passe de 1,92 m à
  moins de 0,01 mm, dans la précision du calcul.
- [Parcours serveur : 5/5 réussis](wall-circuit-traversal.json). Les deux
  circuits complets couvrent les deux portes et les deux escaliers en
  1 845 ticks ; trois allers-retours supplémentaires traversent les nouveaux
  appuis extérieurs en 48 ticks. Chaque appui ciblé est réellement touché
  à l'aller et au retour. Aucun tick en l'air ni blocage.
- Quatre vues après inspectées aux mêmes cadrages que la référence avant.
  Les états de caméra, météo et éclairage sont restaurés.
- Terrain, peinture, anciennes collisions et éclairage conservés. Aucun
  triangle rendu, renderer ou matériau supplémentaire. Les 35 nouveaux
  assets disposent de leur `.meta`.
- Scène enregistrée, retour en Edit Mode, aucune erreur ni avertissement
  dans la console Unity et compilation réussie. Relevés de
  [sauvegarde](../../../../../artifacts/wall-junctions-2026-10-20/final-save-command.json)
  et de [console](../../../../../artifacts/wall-junctions-2026-10-20/final-console-command.json).

## Fichiers

- [StarterIsland.unity](../../../../../Unity3D/Arkya/Assets/Arkya/World/StarterIsland/Scenes/StarterIsland.unity) : références des maillages dérivés et groupe d'appuis.
- [WallJunctions](../../../../../Unity3D/Arkya/Assets/Arkya/World/StarterIsland/Town/RampartUpgrade/WallJunctions) : copies de maillages et volumes d'appui.
- [RepairWallJunctions.cs](../../../../../Unity3D/Arkya/AgentScripts/World/RepairWallJunctions.cs), [RepairWallFootContacts.cs](../../../../../Unity3D/Arkya/AgentScripts/World/RepairWallFootContacts.cs) et [AddWallJunctionSupports.cs](../../../../../Unity3D/Arkya/AgentScripts/World/AddWallJunctionSupports.cs) : commandes natives ciblées, hors `Assets`, avec préflight et Undo.
- [SurveyWallJunctions.cs](../../../../../Unity3D/Arkya/AgentScripts/World/SurveyWallJunctions.cs), [ReviewWallJunctions.cs](../../../../../Unity3D/Arkya/AgentScripts/World/ReviewWallJunctions.cs), [ValidateWallJunctions.cs](../../../../../Unity3D/Arkya/AgentScripts/World/ValidateWallJunctions.cs) et [ValidateRepairedWallCircuit.cs](../../../../../Unity3D/Arkya/AgentScripts/World/ValidateRepairedWallCircuit.cs) : relevés, captures et contrôles.

Les générateurs historiques de l'enceinte ne doivent pas être relancés sur la
scène retouchée : ils remplaceraient l'ensemble de leurs objets. Cette
correction ne change ni le déplacement ni un contrat réseau. Les vérifications
ci-dessus portent sur l'éditeur et le serveur local ECS en Play Mode, sans
nouveau build distribué.
