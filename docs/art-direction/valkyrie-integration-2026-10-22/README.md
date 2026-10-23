# Intégration Valkyrie — 22 octobre 2026

Le catalogue des personnages utilise désormais la variante Arkya de `valkyrie_2019`
dans la création, l’exploration locale et la présentation des personnages distants.
Les assets fournisseur restent isolés sous `Assets/Valkyrie/`.

## Fichiers principaux

Dans `Unity3D/Arkya/Assets/Arkya/Characters/` :

- `Valkyrie/` : prefab, Avatar Generic, six clips, contrôleur, matériaux et copies linéaires des textures de données.
- `Editor/ValkyrieCharacterBuilder.cs`, `ValkyrieMaterialBuilder.cs`, `ValkyrieAnimationBuilder.cs` : reconstruction ciblée, conservant les GUID des assets déjà créés.
- `Runtime/ValkyrieAppearanceBinding.cs`, `Shaders/ValkyrieCharacter.shader` : personnalisation des couleurs conservant les textures peintes.
- `Runtime/CharacterVisualCatalog.cs`, `CharacterPreviewPresenter.cs`, `Runtime/UI/CharacterCreationScreen.cs` et `Settings/CharacterVisuals.asset` : sélection et présentation de la base disponible.
- `Tests/Editor/` : couverture Valkyrie, adaptation des tests de présentation et conservation des tests des prototypes historiques.

Les guides de création, README, architecture, consignes artistiques et crédits
décrivent cette base et la compatibilité des profils historiques.

## Vérifications

Unity **6000.6.6f1**, URP **17.6.0**, Editor actif contrôlé par CoplayDev Unity MCP.
Compilation réussie ; aucune erreur de shader et aucun fichier `.meta` manquant
dans les nouveaux assets. Le relevé final de console ne contient pas d’erreur.

La sélection de tests donne **67 cas : 66 réussis, aucun échec, un ignoré**.
Elle couvre apparences historiques et nouvelles, déformation du squelette,
cadence et transitions, cadrage, création, déplacements serveur/client,
saut, atterrissage, nage et retour à la création. Le test d’admission avec les
services réels exige `ARKYA_WEB_BETA_TICKETS_FILE` et reste ignoré.
Le [rapport JSON](validation.json) conserve le bilan et l’identifiant du job.

La revue visuelle a corrigé deux défauts du premier assemblage : la cornée
opaque masquait les iris, et le clip de saut contenait déjà une élévation du
bassin. La cornée conserve désormais ses reflets avec sa transparence ; le
clip conserve sa pose sans ajouter une seconde trajectoire au saut serveur.
L’essai dans l’île a atteint une connexion locale effective. La scène de création
est laissée hors Play, sans modification non enregistrée ni prefab ouvert.

## Limites

La création propose une base humaine féminine aux cheveux longs. Les quatre
couples historiques Humain/Elfe et silhouettes A/B se chargent sur cette même
base sans modifier leurs données enregistrées. Les variantes artistiques et
coiffures supplémentaires restent à produire. Les deux animations de nage
restent provisoires.

Cette passe n’effectue pas de nouveau build autonome/web ni de mesure des
performances en foule ou des LOD. Des messages Netcode de regroupement des
ticks serveur sont apparus pendant l’essai de capture dans l’Editor ; ces
captures ne constituent pas une validation des performances serveur.

## Captures du Game View

![Création avec la base Valkyrie](creation.png)

![Portrait et transparence des yeux](portrait.png)

![Personnage connecté au serveur local dans l’île](island.png)
