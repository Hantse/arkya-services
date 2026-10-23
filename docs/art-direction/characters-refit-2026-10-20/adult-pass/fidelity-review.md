# Revue — visage adulte, cheveux, matières et doubles lames

État final : revue visuelle ciblée terminée sur le lot natif du 20 octobre
2026 à 22 h 16 UTC. Les quatre défauts signalés restent corrigés après
triangulation et réglage des prises/lames à 0,94 radian. Les limites de
l’échantillonnage et de la validation artistique sont précisées ci-dessous.

La livraison précédente reste trop enfantine, notamment au visage. Cette
nouvelle passe compare les références 01, 02 et 06 au portrait Unity
`../after/human-b-portrait.png`. Elle doit modifier la construction du visage,
pas uniquement réduire les yeux sur les mêmes joues.

## Corrections vérifiables

| Élément | Correction demandée | Preuve visuelle |
|---|---|---|
| Joues et mâchoire | Pommette haute sous le coin externe de l’œil ; creux vers la commissure ; mandibule oblique distincte et menton légèrement plus long. Réduire d’environ 10–15 % la largeur de la joue basse en préservant le volume de pommette. | Silhouette moins sphérique en trois-quarts et ligne pommette–joue–mâchoire lisible en gris. |
| Orbites et paupières | Fentes en amande, approximativement trois fois plus longues que hautes ; paupière supérieure modelée recouvrant 20–30 % du haut de l’iris ; moins de blanc sous l’iris. Orbite en retrait sous l’arcade. | Paupière qui produit une vraie ombre, regard tenu et deux yeux lisibles sans dépendre du trait noir. |
| Nez et milieu du visage | Allonger d’environ 10 % la zone entre ligne des yeux et bouche ; ailes du nez environ 15 % plus étroites ; arête et pointe distinctes, profil moins retroussé. | Transition front–arête–pointe construite et narines moins rondes en profil et trois-quarts. |
| Bouche et menton | Arc de Cupidon, ligne de bouche nette et creux sous la lèvre ; réduire le gonflement du museau. Une commissure discrètement relevée peut donner de l’assurance. | Lèvres intégrées au visage, sans effet de bouche en moue ; menton distinct. |
| Sourcils | Base épaisse, corps franc suivant l’arcade, angle situé au tiers externe puis pointe fine. | Expression assurée ; éviter sourcil flottant, arc étonné ou barre uniformément épaisse. |
| Frange et queue | Raie latérale et deux ou trois grandes mèches en S, de largeurs inégales, libérant les deux yeux. Lobes de queue différenciés, mèches torsadées et goulots nets sous les bagues. | Visage dégagé, contour de frange sculpté ; sillons directionnels et reflets qui suivent les masses. |

Les chiffres sont des cibles d’itération à vérifier à cadrage constant, pas des
mesures anatomiques exactes extraites des photographies de référence.

## Matières

- Peau : variations chaudes aux joues, au nez et aux lèvres ; reflet large
  discret concentré sur les volumes, creux doux. Éviter un aplat beige plastifié.
- Cheveux : creux bordeaux, demi-teintes magenta, quelques reflets directionnels
  chauds ; pas de hausse uniforme de brillance sur toute la chevelure.
- Émail jade : variations de pigment de grande échelle et reflets localisés
  plus étroits, tout en conservant la lecture du volume.
- Or : faces chaudes sombres et arêtes claires ; les biseaux doivent produire
  des changements de valeur francs, sans damier de triangulation.
- Ivoire et cuir : surfaces majoritairement mates, plis et coutures lisibles,
  contrastes plus modestes que le métal et les lames.

## Doubles lames

La planche 03 décrit les silhouettes isolées ; l’image 02 précise poignées,
épaisseurs et découpes ; l’image 06 fixe les matières. Les deux croissants
asymétriques ont une arête turquoise effilée, une structure d’or large percée
d’un oculus près de la poignée, une prise cuir sombre et des extrémités en
crochet. Éviter deux lunes plates génériques. Vérifier la prise des doigts,
la place des armes près des jambes et les collisions visuelles en mouvement.

## Première preuve attendue

Human B uniquement : portrait trois-quarts en gris avec lumière latérale,
profil gris, puis mêmes vues en couleur. Le profil du nez, de la bouche et du
menton doit changer réellement ; les deux yeux restent lisibles. Comparer ces
preuves avant toute propagation aux quatre bases. Les rendus de cette passe
sont destinés au bureau ; aucune nouvelle capture mobile n’est demandée.

## Première étude des matières sur l’ancienne géométrie

Les captures `material-study/human-b-*` améliorent nettement l’or : faces
chaudes, ombres et arêtes se distinguent. Deux corrections ciblées sont
demandées avant la géométrie finale : réduire le reflet et le liseré blancs
sur les cheveux, avec un reflet rosé ; diminuer d’environ un tiers le vernis
blanc de l’émail, en conservant sa localisation et la saturation turquoise.
La lumière latérale aide déjà à lire le visage. Le tissu reste correctement
mat. Ne pas augmenter globalement la brillance de la peau avant la revue
des nouveaux plans du visage.

La seconde étude `material-refinement/human-b-*` atténue les reflets blancs
des cheveux en préservant le magenta, et garde une transition cyan localisée
sur l’émail. L’or reste lisible. Ces réglages constituent la base retenue pour
la géométrie adulte ; aucun nouvel ajustement n’est demandé sur l’ancien visage.

## Première étude des lames

La première forme possède une épaisseur et des poignées, mais reste trop proche
d’un U rond relié à deux anneaux. Les corrections transmises sont : creux du U
réduit, grand côté plus tendu ; âme d’or continue et ajourée autour des deux
ouvertures ; plan et biseau distincts jusqu’à une arête cyan fine. La taille
et le dégagement autour des jambes sont cohérents sur la vue entière. La prise
des doigts reste à contrôler après la nouvelle pose des mains.

## Première preuve Human B — `adult-01`

Le portrait couleur, le portrait gris et le profil gris montrent un changement
d’âge réel : pommettes hautes séparées de la joue basse, mandibule et menton
construits, milieu du visage plus long et profil nez–lèvres–menton distinct.
La frange laisse désormais les deux yeux lisibles. Cette construction peut
être conservée ; aucune nouvelle réduction globale des yeux, aucun allongement
ni creusement supplémentaire du visage ne sont demandés.

Deux corrections connues restent nécessaires avant propagation : la géométrie
de l’iris laisse apparaître une zone rose dans un œil et un goulot supérieur
de la queue est trop séparé de sa bague. Une finition légère des extrémités
des sourcils peut réduire leur section rectangulaire sans modifier l’expression.
Le profil montre aussi des bandes et pointes détachées sous le col ; ce point
sera vérifié sur le prefab natif, séparément de la validation des proportions.

## Seconde preuve Human B — `adult-02`

Les vues couleur, grise et de profil conservent la construction adulte retenue.
La sclère est continue et la zone rose parasite de l’iris n’apparaît plus. Le
goulot supérieur rejoint sa bague sans la séparation de la première preuve.
La forme du visage et des cheveux est validée pour propagation aux quatre
bases ; aucune nouvelle transformation globale n’est demandée. La vérification
du col et des bandes en profil reste à effectuer dans Unity.

## Lames corrigées et prise Human B

La preuve `arkya-grip-closed-b.png` montre une âme d’or continue percée, un grand
côté nettement plus long et des plans de lame séparés du biseau cyan. Les
anneaux indépendants et la silhouette de U uniforme ont été corrigés. Les
doigts entourent le manche et le pouce se pose devant eux sans traversée
visible dans ce cadrage. Cette forme et cette prise sont validées pour
intégration ; les deux mains et les dégagements en mouvement restent à
contrôler sur le prefab natif.

## Revue technique des matières

Lecture seule de `PaintedCharacter.shader`, de `ConfigurePaintedMaterial` et
des sources URP 17.6 installées : noms des propriétés, plages de paramètres,
conversion rugosité/lissage, AO et boucle Forward+ cohérents. Un écart transmis
à l’intégrateur concerne les variantes de mélange et de projection des probes
de réflexion, absentes du shader alors qu’activées dans les pipelines du
projet. Sans elles, URP force ces options à zéro et le nouveau reflet
d’environnement ne suit pas ces réglages. Cette revue de code ne remplace
pas la compilation et les captures natives du lot adulte.

Les variantes `_REFLECTION_PROBE_BLENDING`, `_REFLECTION_PROBE_BOX_PROJECTION`
et `_REFLECTION_PROBE_ATLAS` ont ensuite été ajoutées ; leur présence est
confirmée par lecture du shader. Leur compilation dans l’éditeur reste en
attente. Le délai d’attente de l’éditeur ne constitue pas un diagnostic
d’erreur du shader.

## Contrôle source final — 20 octobre, 21 h 38 UTC

Vues examinées dans `resources/personnages/game/previews` : les quatre
`{human,elf}-{a,b}-adult-final.png`, `human-b-adult-04-profile.png` et
`human-a-adult-04-color.png`. Le cadrage entier Human B reprend la preuve
`human-b-adult-weapons-body.png`.

- Les quatre bases conservent les nouveaux plans du visage et les yeux
  continus ; aucun retour du défaut rose de l’iris n’est visible.
- Le col Human A couvre son attache. Le profil Human B ne présente plus les
  pointes ni le faisceau de bandes détachées sous le menton visibles dans
  `adult-02`.
- Les deux mains ferment leur prise autour des manches. Les lames ont un
  dégagement lisible autour des jambes dans cette pose fixe, sans traversée
  critique visible sur les cadrages examinés.

Aucun nouveau remodelage n’est demandé sur cette base. Cette conclusion
concerne les rendus source Blender, et ne valide pas leur import Unity.
Les quatre FBX ont été copiés avec leurs `.meta` préservés, mais l’import des
matériaux, la compilation native du shader, les portraits du nouveau lot et
les croisements en mouvement restent à contrôler. L’éditeur minimisé ne
répondant plus, les captures natives de la passe précédente et les études
matières sur l’ancienne géométrie ne servent pas de preuve pour ce lot final.
Aucune nouvelle capture mobile n’a été utilisée.

## Première revue native du lot adulte — 21 h 49 UTC

Les 31 PNG du premier lot, désormais archivés dans `iterations/native-01/`,
et leur rapport `reference-refit-capture.json` ont été examinés. Le rapport
indique une capture terminée à
`2026-10-20T21:49:33.3171784Z`, quatre avatars valides, aucun matériau manquant
et aucune erreur de shader. Le délai de l’appel CLI n’a pas empêché cette
exécution Unity de terminer. L’import et le shader ne sont donc plus en
attente ; les défauts visuels ci-dessous empêchent toutefois de considérer
la revue du lot comme terminée.

Les portraits conservent les nouveaux plans adultes, la frange dégagée et les
sclères continues. Le défaut rose de l’iris n’est pas revenu. L’or, l’émail,
les cheveux et l’ivoire se distinguent dans le rendu natif. Les dix poses
Human B maintiennent le drapé dorsal sur le torse, sans retour des anciennes
ailes. Ces constats ne constituent pas une approbation artistique définitive
par l’utilisateur.

### Défauts localisés transmis

| Priorité | Observation | Preuves |
|---|---|---|
| Haute | Deux excroissances de peau dépassent de l’arrière de la jupe B et restent aux hanches lorsque les bras sont levés. La nage les expose très nettement. Cause géométrique ou sélection de sommets à confirmer ; ne pas les confondre avec les mains. | `human-b-turnaround-color.png`, `human-b-walk-1.png`, `human-b-jump-1.png`, `human-b-swim-1.png` |
| Haute | Les pointes des lames traversent les panneaux de jupe en Idle. La prise reste fermée, mais l’orientation et le dégagement des armes doivent être corrigés. | `human-b-grip-left.png`, `human-b-grip-right.png`, corps Human B et Elf B |
| Haute | Le contour arrière du crâne A présente une grande échancrure dentelée entre oreille, nuque et casque. Le profil B est continu au même endroit. | `human-a-face-profile.png`, `elf-a-face-profile.png` |
| Capture à reprendre | La vue entière Human A omet visuellement tissu, or et parties du corps alors que ses portraits et Elf A sont complets. Une compilation asynchrone au premier rendu est une hypothèse, pas une cause établie. | `human-a-body.png` |

L’épaulière B possède une surface jade sombre à l’intérieur du contour ; les
portraits et l’argile ne permettent donc pas de conclure à une face absente.
La coque bombée en arrière et le contour frontal se lisent néanmoins comme
un cadre en vue entière. Ce point a été transmis pour un contrôle local de
surface/bordure, sans nouvelle demande de silhouette.

La validation visuelle doit être renouvelée sur ces zones après correction.
Les poses sont deux échantillons par clip ; elles n’attestent ni l’absence de
croisements à tout instant, ni le timing, les contacts ou les transitions.

## Contrôle des corrections natives — 20 octobre, 22 h 07 UTC

Le nouveau rapport `after/reference-refit-capture.json` est daté du
`2026-10-20T22:07:33.398345Z` : 31 images produites, quatre sujets, aucun
matériau manquant et aucune erreur de shader. Ce lot remplace les preuves
de `iterations/native-01/` pour le contrôle courant.

| Réserve précédente | Résultat du contrôle ciblé |
|---|---|
| Peau déformée sous la jupe B | Corrigée visuellement : aucune excroissance dans le dos du turnaround ni dans les dix échantillons Walk, Run, Jump, SwimIdle et Swim. |
| Lames traversant la jupe en Idle | Corrigée visuellement : les deux vues de prise montrent la lame séparée du panneau de jupe ; la silhouette entière B ne présente plus les pointes cyan traversant le tissu. |
| Crâne A ouvert derrière l’oreille | Corrigé : surface arrière continue sur les profils Human A et Elf A, sans l’échancrure dentelée précédente. |
| Corps Human A incomplet | Corrigé : corps, tissu, or et deux lames sont entièrement rendus sur `human-a-body.png`. |

Le contrôle local transmis par le modeleur confirme que l’épaulière est une
coque fermée avec une surface frontale et un revers ; sa lecture sombre de
face ne correspond pas à une face absente. Aucun remodelage supplémentaire
n’est demandé sur ce point. Le drapé dorsal reste attaché au torse dans les
dix poses examinées.

Après ce contrôle visuel, l’intégrateur a signalé quatre petites arêtes en
intersection dans son test géométrique bidirectionnel de SwimIdle à 75 %.
Elles n’avaient pas été distinguées dans les captures. La correction ciblée
de l’orientation des prises/lames de 0,90 à 0,94 radian doit faire l’objet
d’un dernier contrôle natif avant de clore cette réserve technique. Les
autres formes sont figées.

## Dernier contrôle natif — 20 octobre, 22 h 16 UTC

Le rapport final `after/reference-refit-capture.json`, daté du
`2026-10-20T22:16:35.3454833Z`, identifie les 31 captures du lot triangulé
et quatre avatars valides, sans matériau manquant ni erreur de shader. Les
empreintes source/cible des quatre FBX correspondent dans
`source-copy-report.json` ; leurs fichiers `.meta` ont été conservés.

Relecture ciblée des deux prises, de SwimIdle à 75 %, de Swim et Jump de dos,
des corps A/B, du profil A et du visage B : la correction à 0,94 radian
maintient les lames hors des panneaux de jupe sur les vues examinées. Les
excroissances de peau ne sont pas revenues, le crâne A reste fermé et la vue
entière A est complète. Le visage B, ses yeux et le drapé restent cohérents
avec le lot accepté. Aucune nouvelle correction visuelle bloquante n’est
identifiée dans ce contrôle ciblé.

Le nouveau `weapon-study/final/native-clearance-validation.json` utilise
l’export natif de `2026-10-20T22:16:03.4342855Z` : douze poses testées,
zéro intersection armes/tissu dans les deux sens, réglage à 0,94 radian.
La réserve technique ouverte après le lot de 22 h 07 est donc levée pour
ces échantillons. La peau est exclue du BVH ; ce résultat ne prouve pas
l’absence de collision sur tout le corps, tout le cycle ou les transitions.

Les 48 invocations directes de fixtures ont de nouveau réussi à
22 h 16 min 42 s UTC, sans échec. Le relevé
`verification/console-final-import.json` ne rapporte aucun nouvel
avertissement depuis le dernier import ; la compilation n’est pas en échec.
Ces résultats sont des vérifications techniques, distinctes de l’approbation
de la fidélité artistique par l’utilisateur.

Budgets définitifs avec queue segmentée : 203 506 triangles pour A,
208 975 pour Human B et 208 985 pour Elf B. Les vues natives A utilisent
la coiffure courte, soit 167 330 triangles visibles. Les LOD de foule,
l’équipement dynamique et les animations de combat ne font pas partie de
cette passe.

## Clôture de la revue de création — 20 octobre, 22 h 19 UTC

Les captures desktop finales
`desktop/creation-body-1600x900-221759189.png` et
`desktop/creation-portrait-1600x900-221812083.png` ont été examinées : le
personnage entier et le portrait sont cadrés dans l’interface, avec le
casque et la queue segmentée visibles. Aucun nouveau défaut bloquant n’est
identifié sur ces deux preuves.

`desktop/creation-live-review.json` est clos : 152 contrôles pendant la
revue, puis quatre contrôles de restauration, tous réussis. Les contrôles
couvrent deux formats desktop et 24 apparences ; le callback d’exploration
intercepté vérifie le retour UI, sans prouver un chargement d’île ni du
réseau. La restauration de l’éditeur est confirmée par
`verification/editor-restoration.json` et `verification/editor-state-final.json`.
La scène StarterIsland est restaurée avec ses 18 racines, Play est arrêté.
La console finale ne rapporte aucune erreur ni avertissement.

Cette revue est terminée dans son périmètre : défauts visuels signalés,
échantillons de poses, import et création desktop. Elle ne certifie ni une
fidélité artistique absolue aux références, ni les cycles complets, ni la
performance de foules ou le fonctionnement d’un système de combat.
