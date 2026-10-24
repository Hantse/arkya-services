# Garde-Vase — direction artistique et génération 3D

Le **Garde-Vase** est une proposition de monstre pour Arkya, créée le
25 octobre 2026. Cet amphibien bipède trapu porte une carapace de pierres
de rivière et de grands bras griffus. Sa direction artistique reste
**à examiner et à approuver par l'utilisateur**.

**État : DA créée ; génération 3D Meshy bloquée par l'offre du compte.**
Seule l'image de concept est disponible à ce stade. Aucun modèle 3D ni
aucune texture de modèle n'a été généré ou livré.

![Concept du Garde-Vase](reference/concept.png)

## Direction artistique

Le Garde-Vase évoque un habitant territorial des berges et des mares.
Sa silhouette repose sur une tête large presque sans cou, un ventre lourd,
des cuisses puissantes et deux longs bras. Les mains généreuses et leurs
griffes épaisses lui donnent de la portée, tandis que ses pieds écartés
et sa courte queue assurent un appui crédible. La carapace reste basse et
proche du dos : des plaques épaisses se chevauchent jusqu'aux épaules,
sans former une haute crête.

Le visage concentre l'expression : yeux ambrés sous des arcades minérales,
narines modelées, bouche fermée et large gorge claire. Une légère asymétrie
des plaques évite une construction trop régulière. Le ventre, les bras,
les cuisses et la queue doivent conserver des volumes continus et des
transitions souples.

La finition recherche des surfaces peintes, des plans arrondis et des
arêtes adoucies. Les taches de peau accompagnent les volumes plutôt que
de les masquer. Les pierres possèdent de grandes variations colorées,
des creux teintés et des rebords éclaircis avec mesure. Leur matière
reste distincte de la peau. Le modèle doit rester lisible en petite vue,
avec des détails de taille moyenne ; un facettage grossier ou une
accumulation de microdétails photoréalistes affaibliraient cette cible.

| Zone | Palette et traitement |
| --- | --- |
| Peau | Bleu pétrole, ardoise et touches turquoise désaturées ; surface mate peinte |
| Gorge et ventre | Gris chaud tirant vers l'ivoire, transitions douces vers les flancs |
| Carapace | Jade minéral et gris-vert de rivière ; rebords ocre et rouille |
| Griffes | Ivoire patiné, masses épaisses et extrémités arrondies |
| Yeux | Ambre concentré, avec une petite pupille sombre et des paupières modelées |

La palette froide du corps est équilibrée par le ventre chaud et les
accents minéraux. Aucune lumière magique ni matière émissive n'est
nécessaire pour identifier la créature.

## Rôle des références

- Le [concept du Garde-Vase](reference/concept.png) fixe la silhouette,
  les proportions, la pose de repos, la répartition des matières et la
  palette pour la génération 3D.
- La [référence arbre peint V3](../../references/arbre-peint-v3/README.md)
  guide les variations peintes, les creux colorés et la construction des
  grandes masses. Son motif d'écorce ne doit pas être copié sur la peau
  ou la pierre.
- Les [références de personnages stylisés](../../references/personnages-stylises-2026-10-20/README.md)
  guident le soin du modelé, les transitions anatomiques et la finition
  des matières. Elles n'imposent pas une anatomie humaine à l'amphibien.
- Le [Brise-Sève](../brise-seve/README.md) situe cette proposition dans le
  bestiaire existant. Le Garde-Vase apporte une silhouette bipède et une
  palette minérale froide, avec sa propre identité.

## Pipeline demandé

Le pipeline prévu utilise **Meshy7, en mode standard**, à partir de
l'unique image `reference/concept.png` : une créature complète, avec
sa géométrie et ses textures PBR. La sortie demandée est un **GLB autonome
avec textures incorporées en 4K**.

La création de tâche Meshy a été refusée avec **HTTP 402,
`NoMorePendingTasks`** : le service indique que la création de tâches
n'est plus disponible avec l'offre gratuite du compte. Le solde affiché
de **3 230 crédits** ne lève pas cette restriction. Aucun identifiant de
tâche n'a été reçu ; il n'y a donc aucun résultat 3D à attendre ou à
télécharger pour cette tentative.

La génération nécessite une offre Meshy autorisant la création de tâches.
Après résolution de ce blocage, les fichiers réellement obtenus, le coût
et les mesures devront être consignés après réception et inspection.
Cette notice conserve la cible et le périmètre prévus.

Le [suivi de génération](generation.json) conserve les prompts exacts,
les paramètres, l'identifiant d'opération locale et le refus du service.
Le planificateur CLI estimait cette génération texturée à **30 crédits**.
Le solde est resté à 3 230 après le refus ; aucun coût réel n'a été
rapporté par une tâche, puisqu'aucune n'a été créée.

La vue de référence est un trois-quarts. La construction du dos, des
zones cachées et des interfaces entre carapace et peau devra donc être
examinée sur le modèle obtenu. Un rendu d'aperçu permet de juger une
partie de la forme et de la couleur ; il ne prouve pas la qualité de la
topologie, des UV ou des futures déformations.

## Mouvement envisagé

Une future animation pourrait alterner une respiration lourde au repos,
une marche lente avec transfert marqué du poids et un balayage d'un bras.
La queue courte accompagnerait l'équilibre et les plaques resteraient
proches du dos. **Ces mouvements sont uniquement une intention artistique :
aucun rig ni aucune animation ne sont demandés ou implémentés ici.**

## Périmètre et limites

Cette étape porte sur la direction artistique et un modèle texturé
généré par Meshy. Les LOD, le rig, les animations, les colliders et
l'optimisation pour des groupes de monstres restent à préparer si une
intégration est ensuite demandée. Aucun budget de triangles, de matériaux
ou d'os n'est considéré comme validé par cette proposition.

Aucune intégration ni validation dans Unity n'est comprise dans cette
étape. Aucun comportement d'IA, déplacement autoritaire, dégât, apparition,
loot, quête ou donnée réseau n'est ajouté. La validation artistique et
la vérification technique en jeu resteront distinctes de la réception
du fichier généré.
