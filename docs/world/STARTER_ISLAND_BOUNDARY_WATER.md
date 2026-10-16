# Île de l’Aube — couronne d’eau dangereuse

La [scène de l’île](../../Unity3D/Arkya/Assets/Arkya/World/StarterIsland/Scenes/StarterIsland.unity) est entourée d’une couronne d’eau violette bouillonnante avant la limite du plan d’océan. Elle signale une zone mortelle : **cinq secondes d’exposition continue mettent la vie du personnage à zéro côté serveur**.

| Paramètre | Valeur |
| --- | --- |
| Centre horizontal, X/Z | `(768, 768)` m |
| Début de l’eau dangereuse | Rayon de **835 m** |
| Largeur de la bande bouillonnante | **65 m**, jusqu’au rayon 900 m |
| Au-delà de la bande | Eau violette plus sombre |
| Niveau de la mer | **Y = 28 m** |
| Premier contact avec l’eau | Pieds à **Y ≤ 28,3 m**, dans la zone extérieure |
| Délai mortel | **5 secondes continues** |

La mesure complète des échantillons du Terrain au-dessus de **Y = 28 m** trouve un rayon sec maximal de **801,90 m** : le seuil de 835 m laisse environ **33,10 m** de marge après cette terre. L’océan fait 4 200 m de côté ; la couronne apparaît avant son bord et ne coupe ni les terres ni les routes de l’île.

## Règle d’exposition

Entrer dans la zone extérieure en touchant l’eau démarre le compteur. Avant ce premier contact, passer au-dessus de l’eau ne déclenche pas l’exposition. Une fois exposé, sauter ou remonter au-dessus du seuil vertical ne remet pas le compteur à zéro : tant que le personnage reste au-delà du rayon intérieur, les cinq secondes continuent. Revenir à l’intérieur du rayon de 835 m annule l’exposition et remet le délai à zéro ; une nouvelle sortie exige un nouveau contact avec l’eau.

Le seuil radial est inclusif : **rayon ≥ 835 m** est dangereux, **rayon < 835 m** remet le compteur à zéro. Le danger continue au-delà de la bande de 65 m : traverser toute la couronne ne mène pas à une seconde zone sûre.

La mort utilise le champ existant `CharacterStats.Health` de la [base de gameplay ECS](../GAMEPLAY_FOUNDATION.md). [BoundaryWaterSystem](../../Unity3D/Arkya/Assets/Arkya/Gameplay/BoundaryWater/Runtime/BoundaryWaterSystem.cs) s’exécute uniquement en `ServerSimulation`, ajoute l’état d’exposition par Entity Command Buffer et applique le délai sur le temps de simulation du serveur. Les effets visuels et la caméra de revue ne modifient pas la vie. Cette passe n’ajoute pas de résurrection, de perte d’inventaire ou de pénalité persistante.

Les personnages concernés possèdent `CharacterStats` et un `LocalTransform` racine, sans `Parent`. La position de ce transform est interprétée comme la position des pieds en coordonnées du monde. [BoundaryWaterExposure](../../Unity3D/Arkya/Assets/Arkya/Gameplay/BoundaryWater/Runtime/BoundaryWaterComponents.cs) conserve seulement l’armement et l’heure de premier contact ; il ne duplique pas la vie du personnage.

## Configuration et portée

Dans la hiérarchie, **Violet boundary water - five second exposure** porte [BoundaryWaterAuthoring](../../Unity3D/Arkya/Assets/Arkya/Gameplay/BoundaryWater/Authoring/BoundaryWaterAuthoring.cs), qui expose `CenterXZ`, `InnerRadius`, `SurfaceHeight` et `DeathDelaySeconds` dans l’Inspector. Avec `InstallInServerWorlds`, son bootstrap installe une configuration manquante dans les mondes `GameServer`, puis recherche toutes les 0,5 seconde les mondes créés plus tard. Il conserve les configurations préexistantes et ne retire à sa désactivation que celles qu’il possède. Un Baker fournit le même contrat pour une conversion en SubScene. La scène actuelle reste une scène ordinaire.

Il faut un seul `BoundaryWaterSettings` valide par monde serveur. Une configuration absente, ambiguë ou invalide n’applique pas de dégâts. Le [shader de l’océan](../../Unity3D/Arkya/Assets/Arkya/World/StarterIsland/Shaders/StarterIslandWater.shader) et les effets de bouillonnement utilisent les mêmes limites visuelles.

[BoundaryWaterPresentation](../../Unity3D/Arkya/Assets/Arkya/World/Environment/Rendering/BoundaryWaterPresentation.cs) ajoute en Play Mode bulles, écume et vapeur violettes près de la caméra. Son budget total est de **320 particules** ; les effets s’arrêtent à distance et ne projettent pas d’ombres. Le [script d’installation et de capture](../../Unity3D/Arkya/AgentScripts/World/AddIslandBoundaryWater.cs) configure ensemble la règle, la présentation et le matériau d’océan. Les captures rapprochées nécessitent le Play Mode pour montrer les particules réelles.

Le contenu de l’île conserve ses copies prévues par realm. Cette passe n’ajoute ni admission aux instances, ni pont avec les personnages du prototype d’areas, ni réplication supplémentaire : la règle ECS s’applique aux personnages de gameplay présents dans le monde serveur configuré. La caméra libre de revue reste un outil de présentation et ne constitue pas un personnage soumis à la mort.

## Vérification

La [passe de vagues](STARTER_ISLAND_WAVES.md) ajoute une houle plus marquée dans la couronne. Ses bulles et son écume suivent désormais ce relief visuel ; la configuration de danger décrite ici conserve le niveau moyen de l’eau et le même délai serveur.

Captures en Play Mode : [couronne autour de l’île](starter-island/boundary-water/overview.png), [bouillonnement de jour](starter-island/boundary-water/boiling-day.png), [bouillonnement de nuit](starter-island/boundary-water/boiling-night.png).

Les [24 tests Edit Mode](starter-island/boundary-water/editmode-tests.json) et les [deux tests Play Mode](starter-island/boundary-water/playmode-tests.json) passent dans Unity. Ils couvrent les seuils radial et vertical, l’absence de mort à 4,999 s et la mort à 5 s, le saut après contact, le retour vers l’île, les paramètres invalides et la propriété de la configuration.

Les tests Play Mode créent de vrais mondes ECS serveur et client et injectent les temps de simulation : la vie serveur reste à 100 à 4,999 s puis passe à zéro à 5 s, tandis que la vie cliente reste à 100. Ils vérifient aussi le bootstrap de scène ordinaire et son nettoyage. Ce contrôle exerce les systèmes ECS, sans connexion réseau entre joueurs.

La [validation de présentation](starter-island/boundary-water/presentation-validation.json) réussit ses **sept contrôles** : limites visuelles et serveur concordantes, shader compilé et supporté, toutes les terres sèches préservées, **198 particules actives près de la couronne sur un budget de 320**, zéro à distance, nettoyage à la désactivation puis recréation des trois systèmes d’effets à la réactivation.

Le [relevé final de console](starter-island/boundary-water/console-validation.json) trouve zéro erreur et deux avertissements externes de rafraîchissement de jeton Unity.AI Toolkit. La scène est enregistrée en Edit Mode, avec caméra et aperçu du refuge restaurés ; les réglages globaux Graphics/Quality conservent leurs empreintes de référence.

