# Diagnostic de la tête du loup V3 — rejetée

Inspection du profil, de la vue trois-quarts et de `wolf_shape`. La V3 ne doit
pas servir de référence visuelle approuvée. Aucun fichier de production n'est
modifié par cette analyse.

## Causes géométriques

- Le crâne est comprimé à 70 % en hauteur et élargi à 128 % ; son rapport
  profondeur/largeur devient environ 55 % de celui de la source. Le volume
  crânien est remplacé par un plateau. La réduction verticale locale des yeux
  s'ajoute à cette compression.
- La largeur du museau augmente de 144 à 180 % vers la truffe. Ce traitement
  conserve le nez plat de la source tout en l'étalant : bec large, sans bloc
  nasal ni transition lisible entre front et chanfrein.
- Le bord de joue pointu du renard est conservé et élargi. Les transformations
  différentes du crâne, du museau et du cou prolongent ce bord en tablette.
  L'ombre noire sous cette tablette correspond à une vraie concavité ; la
  peinture ne la résout pas.
- La mandibule ne reçoit qu'un déplacement local de 3 mm avant échelle. Elle
  n'acquiert pas la masse et la courbure nécessaires sous le museau.
- Le cou reste issu du port vertical du renard. Son remplissage local ne
  reconstruit pas la transition oblique entre nuque, gorge et thorax.

## Reconstruction proposée

Privilégier une véritable base de loup licenciée si elle est disponible. Sinon,
remplacer dans un draft séparé toute la tête **et le haut de l'encolure**, plutôt
que réappliquer des facteurs à la V3. Conserver le corps et ses chaînes de pattes.

Construire une cage continue à sections ovales : nuque inclinée, boîte crânienne
volumique, arcades avec yeux latéraux enfoncés, chanfrein, museau projeté de section
ovale, truffe compacte et mandibule distincte. La largeur doit décroître des
pommettes vers le nez ; le dessous de la joue doit rejoindre la gorge par une
courbe, sans rebord horizontal. Petites oreilles triangulaires attachées au
crâne, sans les utiliser pour masquer un volume crânien insuffisant.

Une nouvelle surface ne peut pas garantir les anciens UV au point près :
transférer leurs coordonnées par projection barycentrique puis rebaker la
peinture, ou refaire l'îlot de tête. Préserver les UV ne doit plus imposer la
forme. La tête rigide peut recevoir `Head`, les oreilles leurs os, et le raccord
du cou une transition `Chest` / `Neck` / `Head` normalisée à quatre influences.

Première livraison : tête/cou gris intégrés au corps, sans magie ni variante,
profil + face + trois-quarts à taille normale et en vignette. Vérifier le volume
du crâne, une truffe pleine, la profondeur du museau et l'absence de joue en aile
avant toute peinture, animation ou série d'exports.
