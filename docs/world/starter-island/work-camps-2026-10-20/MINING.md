# Camp minier

`BuildMiningWorkCamp.Main()` a été exécuté dans l'Editor. La racine indépendante
`Miners work camp - painted quarry and shelter` est placée en
**(700 ; 51,477753 ; 540)**, orientée vers le sud. Elle se trouve à **151,33 m**
de la référence de village `(550 ; 520)` en distance horizontale.

L'abri possède un plancher, une entrée de 2,15 m, des fenêtres ouvertes dans les
murs, une table et un banc. La charpente et le toit d'ardoises géométriques
reprennent les matériaux du village. Après la première revue des captures,
`CloseMiningShelterGables.Main()` a fermé les deux pignons et les jours sous
les rives avec un maillage supplémentaire de 56 triangles, un Renderer et le
matériau de chaux existant. Le [rapport de fermeture](mining-roof-infill.json)
confirme que les anciens maillages et colliders sont conservés.
La petite galerie est construite
au-dessus du Terrain : sol, plafond, murs de soutènement, quatre cadres en bois
et véritable fond de taille. Elle conserve au moins 3,41 m entre les supports
et 2,99 m sous les poutres. Les marches sont distinctes, sans main courante,
et rejoignent exactement les planchers.

Les roches et les minerais proviennent des préfabs optimisés approuvés et
conservent leurs trois LOD. Le chantier comprend une voie courte et son
wagonnet à l'écart du passage, une table de tri, des caisses, des pioches
décoratives et trois tonneaux issus de la version améliorée du village.
Aucun système de récolte, PNJ, lumière ou comportement par image n'est ajouté.

L'authoring a créé **69 colliders statiques, 40 groupes LOD et 137 Renderer
tous niveaux confondus**, avec 14 matériaux existants. Le total est de
317 703 triangles pour tous les niveaux, dont 208 423 si chaque objet utilise
son LOD0. La fermeture des pignons porte ces chiffres à **138 Renderer,
317 759 triangles tous niveaux et 208 479 triangles au LOD0**.
Ce budget de contenu ne constitue pas une mesure de temps GPU.
Les nouveaux maillages sont isolés sous
`Assets/Arkya/World/StarterIsland/WorkCamps/Mining`.

Le volume visible s'étend de X 686,30 à 713,46 et de Z 529,14 à 558,21.
Il préserve la route existante au sud de Z 520. Le Terrain et la ville
n'ont pas été modifiés par ce script, et aucune instance de végétation n'a dû
être désactivée. L'outil fournit Undo, refuse de reconstruire un camp existant
et ne sauvegarde pas lui-même la scène.

Les marqueurs `Review route ...` décrivent l'approche, la cour, les pieds de
marches, les centres de marches, l'abri et la galerie. Le
[rapport d'authoring](mining-authoring.json) enregistre leurs positions exactes.
La validation de circulation serveur et la revue des captures sont réalisées
séparément avec les outils communs aux deux camps.
