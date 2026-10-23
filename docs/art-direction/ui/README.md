# Arkya — Exploration de la direction artistique UI

16 octobre 2026. **Les trois pistes plaisent à l'utilisateur ; elles restent
des propositions visuelles à éprouver en jeu.**

La [seconde exploration présente un HUD complet dans les trois thèmes](hud/README.md),
avec un [comparateur interactif](hud/index.html) et une proposition de choix
du thème par le joueur.

![Trois propositions de direction artistique UI](ui-directions-01.png)

Cette planche générée compare des intentions de matière et de forme. Ce n'est
pas une capture Unity, un écran fonctionnel ou un kit d'assets prêt à intégrer.
Les valeurs, objets, actions et raccourcis sont illustratifs ; ils ne définissent
ni l'équilibrage ni les fonctionnalités du jeu. Les douze cases visibles sont un
échantillon de composants, pas le format final de l'inventaire.

## Point de départ

Les [références d'environnement approuvées](../resources/README.md) donnent la
ligne : surfaces peintes, volumes soignés, ombres colorées, arêtes éclaircies avec
mesure et magie contenue. Pour l'UI, cela se traduit par des icônes peintes et
des cadres travaillés autour de surfaces de lecture calmes.

La [création de personnage actuelle](../../../Unity3D/Arkya/Assets/Screenshots/Characters/creation-active.png)
utilise déjà crème et vert forêt. Ses panneaux sont assez plats ; les petits
textes secondaires et certains états discrets mériteraient une passe de
lisibilité. Le HUD IMGUI du prototype d'areas sert à vérifier la simulation et
le réseau ; il ne constitue pas une référence artistique.

## Trois pistes

| Piste | Matières et formes | Intérêt | Point à surveiller |
|---|---|---|---|
| **01 — Encre & cuivre** | Fonds bleu-vert profonds, cuivre patiné, texte ivoire, accent jade ; cadres fins aux coins façonnés | Base polyvalente pour HUD dense, sacs, équipement et création | Garder le cuivre discret et les fonds suffisamment opaques |
| **02 — Carnet d'exploration** | Parchemin chaud, vert forêt, bois miel ; coins souples, quelques attaches de cuir | Aventure accueillante, continuité avec la création actuelle | Les grandes surfaces claires attirent beaucoup le regard en combat |
| **03 — Pierre de mana** | Ardoise bleutée, pierre violette, texte pâle, petits accents de mana | Identité minérale et magique, cohérente avec cristaux et mares | Limiter les lueurs pour préserver la hiérarchie des alertes |

**Recommandation initiale : Encre & cuivre comme base commune.** Les fonds
sombres laissent les icônes peintes et le monde porter la couleur ; le cuivre
introduit une présence artisanale. Le parchemin pourrait ensuite servir aux
cartes et au journal, en conservant les mêmes règles de composants. Ce choix
reste une préférence de conception. Après examen, l'utilisateur apprécie les
trois pistes et propose de laisser le joueur choisir ; l'exploration HUD
développe cette possibilité.

## Règles communes proposées

- **Matière autour du contenu.** Variations peintes sur les bords, titres et
  icônes ; fonds de texte presque unis. Ornements concentrés aux coins.
- **Deux registres typographiques.** Titres à empattements sobres, texte courant
  sans empattements et chiffres faciles à distinguer. Police exacte à choisir
  après la direction ; vérifier accents français et licence.
- **Lecture en jeu.** Point de départ à 1080p : texte courant 16–18 px, secondaire
  au moins 14 px ; échelle UI réglable et vérification réelle à plusieurs
  résolutions. Ces tailles sont des cibles, pas une validation sur le concept.
- **États explicites.** Survol par éclaircissement du bord ; sélection par double
  contour et repère ; focus clavier visible ; indisponibilité avec motif ou
  libellé utile. Un cooldown garde son chiffre en plus du masque.
- **Couleurs fonctionnelles stables.** Vie rouge brique, stamina distincte,
  sélection jade ; rareté, état enchanté et erreurs doivent aussi avoir un
  signe ou un texte. Les couleurs des matières n'imposent pas celles du gameplay.
- **Mouvement contenu.** Réponses brèves aux interactions, aucune pulsation
  permanente autour de tous les éléments ; option de réduction des mouvements.

### Palette de départ pour Encre & cuivre

| Rôle | Couleur |
|---|---|
| Fond de panneau | `#192C30` |
| Texte principal | `#F1E4CB` |
| Cuivre / action principale | `#B78B59` |
| Jade / sélection | `#81B8A4` |

Contrastes calculés sur aplats sRGB : ivoire sur encre **11,56:1** ; jade sur
encre **6,46:1** ; texte encre sur bouton cuivre **4,74:1**. Ce sont des mesures
des couleurs proposées, pas des pixels de l'image générée. Les textures,
survols, états désactivés et fonds de scène devront être contrôlés lors de
l'intégration.

## Mise à l'épreuve après choix artistique

1. **HUD sur l'île de départ** : vie, stamina, cible, quêtes, minimap et actions ;
   garder le centre du jeu dégagé, comparer jour et nuit.
2. **Inventaire et équipement** : vérifier la densité avec jusqu'à 150 places,
   les quantités, le poids porté, les variantes normale/enchantée et les
   emplacements de pierres. Les données existantes sont décrites dans la
   [base de gameplay](../../GAMEPLAY_FOUNDATION.md).
3. **Création et sélection** : appliquer les mêmes cadres, boutons, textes et
   états à l'[écran existant](../../CHARACTER_CREATION.md), avec une distinction
   claire entre aperçu local, opération en cours et résultat serveur.

La recherche du 16 octobre est conservée comme référence visuelle. La
[première intégration Unity du 17 octobre](IMPLEMENTATION.md) applique les
trois thèmes à la création, au social et à la structure du HUD d'exploration
locale. La composition complète V3 dispose maintenant d'un aperçu Editor ;
son raccordement aux données de gameplay reste à intégrer.

## Livraison et contrôle

- `ui-directions-01.png` : planche comparative générée avec l'outil intégré
  `image_gen`.
- [Prompt exact](PROMPT.md) : provenance reproductible de l'intention.
- Inspection visuelle de la planche et contrôle des chemins locaux à la livraison.
- La livraison documentaire du 16 octobre ne modifiait aucun code, scène,
  prefab ou asset du client Unity et ne nécessitait pas de test Unity.
  L'[intégration actuelle](IMPLEMENTATION.md) est une étape distincte ;
  la lisibilité en jeu reste à vérifier.

