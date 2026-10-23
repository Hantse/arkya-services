# Sélection et création de personnage — 23 octobre 2026

L’écran d’entrée présente le personnage et sa liste à droite. Le bouton
« Créer un perso » reste en bas du panneau pendant le défilement de la liste.
Il ouvre une vue dédiée : apparence à gauche, aperçu Valkyrie au centre,
nom et création à droite. Sur les surfaces compactes, les réglages sont
répartis entre les onglets « Apparence » et « Nom & création ».

Le brouillon est séparé du personnage sélectionné. Annuler restaure ce
personnage ; le retour d’un essai sur l’île conserve le brouillon. Une
création confirmée par le service revient à la sélection.

## Vérifications

- Compilation réussie dans Unity **6000.6.6f1**, projet actif `Unity3D/Arkya`.
- **13 tests ciblés réussis**, aucun échec ni test ignoré, exécutés via MCP.
  Ils couvrent la navigation, les brouillons, les options d’interface et les
  parcours d’exploration avec serveur/client locaux.
- Liste de douze personnages testée sur des surfaces de 1600 × 900,
  980 × 700, 430 × 900 et 830 × 1736 points de panneau. La dernière entrée
  reste accessible par défilement et le bouton de création reste visible.
- Correction vérifiée du passage d’un ancien personnage de 196 cm à un
  nouveau brouillon de 175 cm : les événements différés du curseur ne
  remplacent plus la nouvelle taille.
- Captures réelles de la Game View à **1918 × 953 pixels**, puis retour à la
  sélection vérifié. Play arrêté ; scène `CharacterCreation` propre.
- Console finale : **zéro erreur**. Des avertissements existants de
  dépréciation des API Unity restent présents.

Le [rapport JSON](validation.json) conserve le résultat du job et les fichiers
modifiés. Les modifications d’interface concernent `CharacterCreation.uxml`,
`CharacterCreation.uss` et `CharacterCreationScreen.cs`, avec quatre fichiers
de tests adaptés. Le README du projet et le guide de création décrivent le
nouveau parcours.

Les trois personnages de la capture sont des données de démonstration
injectées uniquement en mémoire pendant Play. Aucun compte réel n’a été
modifié. L’authentification Discord et la création auprès des services réels
ne sont pas validées par cette passe. Aucun nouveau build autonome ou Web
n’a été produit.

## Captures

Sélection avec une liste de démonstration :

![Liste des personnages à droite et bouton de création en bas](selection-roster.png)

Vue dédiée de création :

![Apparence, aperçu Valkyrie et identité](creation.png)

État initial sans personnage connecté :

![Sélection sans personnage enregistré](selection-empty.png)
