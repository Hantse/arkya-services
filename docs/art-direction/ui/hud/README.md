# Arkya — HUD V3 et thèmes au choix

16 octobre 2026. Les trois premières pistes plaisent à l'utilisateur ; cette
seconde exploration les applique à un HUD complet. La V2 répond aux six
corrections de l'utilisateur : plusieurs barres, davantage de place pour le
jeu, icônes carte et journal sur la minimap, suppression du bouton Échap et
suivi de quêtes sans fond ni bord. Les précisions suivantes conservent de
petits avatars, les jauges et les buffs du groupe, sans cadre commun, et
retirent les libellés des deux icônes de minimap. La V3 conserve cette base
et place les récoltes sous le groupe, les petits buffs sous chaque jauge de
mana, la vie et les dégâts au-dessus des ennemis. Les deux boutons de minimap
deviennent ronds. **Maquettes artistiques ; HUD et sélecteur de thème
à intégrer.**

[Ouvrir le comparateur](index.html) · [Prompts V3](PROMPTS-V3.md) · [Prompts V2](PROMPTS-V2.md) · [Prompts V1](PROMPTS.md)

Le comparateur permet de changer de maquette et mémorise le dernier thème dans
ce navigateur. Il ouvre la V3 par défaut et permet de revenir aux V2 et V1 pour
comparer l'encombrement dans le même thème. Il simule le choix visuel ; les
widgets dessinés dans l'image ne sont pas interactifs.

Pour le rouvrir depuis la racine du dépôt :

```powershell
python -m http.server 51816 --bind 127.0.0.1 --directory docs/art-direction/ui/hud
```

Puis ouvrir `http://127.0.0.1:51816/`. Le fichier HTML peut aussi être ouvert
directement dans un navigateur ; la mémorisation dépend alors de ses règles
de stockage pour les fichiers locaux.

## Trois vues

### 01 — Encre & cuivre

![HUD Encre et cuivre V3](01-encre-cuivre-v3.png)

Fonds encre, cadres de cuivre patiné, titres ivoire et petits accents jade.
C'est la base sombre polyvalente de la première recherche.

### 02 — Carnet d'exploration

![HUD Carnet d'exploration V3](02-carnet-exploration-v3.png)

Parchemin chaud, bois miel, attaches de cuir et titres vert forêt.
Les surfaces claires restent limitées aux panneaux utiles ; le centre du jeu
conserve son dégagement.

### 03 — Pierre de mana

![HUD Pierre de mana V3](03-pierre-mana-v3.png)

Ardoise bleutée, pierre sculptée, texte pâle et petits accents de cristal.
La lumière des ornements doit rester plus discrète que les alertes de gameplay.

## Une disposition commune

Les vues sont composées à partir de la
[capture de forêt existante](../../../world/starter-island/environment/forest-day.png).
Le modèle humain de la [création actuelle](../../../../Unity3D/Arkya/Assets/Screenshots/Characters/creation-active.png)
sert de référence d'apparence. Le HUD, le personnage dans cette forêt et le
sanglier sont ajoutés par génération d'image. Ces compositions ne sont donc
pas des captures d'un HUD exécuté dans Unity.

| Zone | Contenu proposé |
|---|---|
| Haut gauche | Petit portrait, niveau, deux jauges et effets compacts |
| Gauche | Trois petits avatars empilés, noms, jauges vie/mana et petits buffs **sous la mana** ; sans cadre commun |
| Sous le groupe | Gains de récolte : petite icône de ressource et quantité, sans fond ni bord |
| Haut centre | Nom, niveau et vie de la cible ; portrait retiré |
| Au-dessus de l'ennemi | Fine barre de vie rouge et nom ; nombres de dégâts flottants à proximité |
| Haut droit | Minimap réduite ; **deux boutons ronds**, icône carte et icône journal sous le bord, sans libellé |
| Droite | Objectifs en texte clair avec ombre sur les glyphes ; **aucun fond ni bord** |
| Bas gauche | Deux lignes de conversation hors saisie, sans cadre permanent ; récolte déplacée sous le groupe |
| Bas centre | **Deux barres de 12 emplacements**, raccourcis, quantité et cooldown ; expérience fine |
| Bas droit | Petit sac **I** seulement ; poids et places réservés à l'inventaire |

### Principes d'allègement

Retirer les surfaces inutiles avant de réduire la typographie. Les blocs de
groupe et de chat deviennent plus courts et moins décorés. Le groupe garde
trois petits avatars, avec le nom, deux fines jauges et des buffs miniatures
placés sous la barre de mana de chaque membre, l'un sous l'autre à gauche.
Aucun cadre global ni carte de fond n'entoure
ces lignes. Les icônes carte et journal quittent le bas de l'écran pour
rejoindre la minimap. Les mots « Map » et « Quest », ainsi que les lettres de
raccourci, ne sont pas inscrits sur ces boutons ; les info-bulles pourront
indiquer « Carte — M » et « Journal — J » lors de l'intégration. Le bouton
Échap et son engrenage disparaissent du HUD ; l'accès clavier au menu reste
le comportement prévu. Les boutons de carte et de journal sont circulaires.

Le gain « Bois × 3 » quitte le chat et rejoint une petite zone de notifications
juste sous le dernier membre du groupe. À l'intégration, les gains confirmés
par le serveur pourront s'y regrouper par ressource puis disparaître, sans
panneau permanent.

La vie du sanglier apparaît directement au-dessus de lui, en complément de
la cible sélectionnée en haut. Les chiffres de dégâts sont montrés près de
l'ennemi ; leur montée et leur disparition progressive sont une intention
d'animation, représentée ici par une image fixe. La future présentation doit
limiter les chiffres simultanés pour garder le combat lisible et respecter
le réglage de réduction des mouvements. Les valeurs de vie et de dégâts
restent issues de la simulation faisant autorité.

Le suivi de quêtes reste sans panneau dans les trois habillages, y compris
Carnet d'exploration. Sa couleur de texte claire et son ombre sont communes
aux thèmes pour conserver la lisibilité devant le feuillage. Le contraste
devra encore être éprouvé dans les différentes ambiances du jeu.

Deux barres de douze cases sont une proposition pour cette maquette, et non
une limite du système de sorts. Le nombre de barres visibles, leur placement,
leur échelle et leurs raccourcis doivent pouvoir se régler indépendamment du
thème. Les raccourcis dessinés restent illustratifs et remappables, avec une
validation prévue sur clavier français. Les cases, quantités et cooldowns
devront conserver une taille lisible après réglage.

Le chat montré est hors saisie ; son champ et ses contrôles pourront se
présenter lorsqu'il est activé. Aucun panneau ouvert supplémentaire n'est
nécessaire pour montrer le HUD d'exploration.

Les [trois vues V2](01-encre-cuivre-v2.png) restent conservées, avec les variantes
[Carnet V2](02-carnet-exploration-v2.png) et [Pierre V2](03-pierre-mana-v2.png).
Les [vues V1](01-encre-cuivre.png) sont également conservées, avec les variantes
[Carnet](02-carnet-exploration.png) et [Pierre](03-pierre-mana.png), et accessibles
dans le comparateur pour juger la place récupérée.

Les personnages, nombres, quêtes, compétences, progression, discussion et
fonctionnalités sociales sont illustratifs. Leur présence ne change pas le
périmètre de gameplay actuellement implémenté.

La première vue sert de composition de référence pour les deux autres. La
génération conserve l'intention et la disposition générale ; les détails
dessinés ne constituent pas une garantie d'identité pixel à pixel. La future
UI doit réutiliser les mêmes composants, icônes et données dans chaque thème.

## Choix laissé au joueur

Parcours proposé : **Options → Interface → Thème**.

Trois vignettes donnent un aperçu, puis le changement s'applique immédiatement.
Le choix est mémorisé localement et restauré au lancement suivant. Encre &
cuivre peut être la valeur initiale, sans imposer un thème à la classe, à la
race ou au personnage.

| Réglage | Responsabilité |
|---|---|
| Thème | Matières, cadres, palette décorative, ornements ; titres à métriques compatibles |
| Disposition | Position des blocs, taille, densité et panneaux visibles |
| Accessibilité | Échelle du texte, contraste, couleurs fonctionnelles et réduction des mouvements |

Changer de thème conserve les positions, raccourcis, données, tailles
interactives et icônes. Les couleurs de la vie, de la stamina, des alertes et
de la progression restent stables ; une préférence d'accessibilité peut
les adapter indépendamment. Les icônes et libellés accompagnent la couleur.

La police de lecture et les chiffres restent communs. Une éventuelle variante
des titres ne doit pas provoquer de débordement ou déplacer les boutons.
Les états de survol, sélection, focus, indisponibilité et cooldown doivent
exister dans les trois habillages.

### Traduction technique envisagée

La création utilise déjà UI Toolkit avec UXML, USS et TSS. La future structure
peut partager une arborescence de composants, une feuille de disposition,
les styles de gameplay et une feuille d'habillage par thème. Des identifiants
stables tels que `ink-copper`, `explorer-journal` et `mana-stone` permettent
d'enregistrer la préférence.

La préférence visuelle appartient au client ; elle ne demande pas de
réplication Netcode. Une synchronisation entre appareils pourrait être
ajoutée plus tard via les préférences du compte. Elle n'est pas nécessaire
pour un premier sélecteur local.

Le HUD actuel du prototype d'areas utilise encore IMGUI. Il faut construire
le HUD de présentation commun et y brancher les données observées avant
d'appliquer ces thèmes en jeu. Modifier uniquement le style de la création
ne suffirait pas.

## Vérification

Les trois images sont inspectées comme propositions visuelles, leurs copies
et liens locaux contrôlés. Le comparateur est vérifié avec changement de
thème et restauration du choix après rechargement. Le passage V1/V2/V3 garde le
même thème, et un rechargement revient à la V3 par défaut.
Le plein écran et son retour ont également été vérifiés dans le navigateur
intégré. La [capture V1](comparateur-verifie.jpg) reste conservée ; la V2 dispose
de sa [capture de vérification](comparateur-v2-verifie.jpg), et la V3 de sa
[capture de vérification](comparateur-v3-verifie.jpg).

La validation de production reste à faire dans Unity : textes et interactions
réels, passage jour/nuit, résolutions et échelle UI, langues longues, inventaire
chargé, contrastes et lisibilité des alertes. Aucun test Unity n'est exécuté
pour cette livraison d'images et de documentation.

