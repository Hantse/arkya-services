# Humains et Elfes — atelier d’apparence

Proposition artistique pour la création de personnage d’Arkya. Depuis le
**20 octobre 2026**, la cible est la
[référence personnages fournie par l’utilisateur](../references/personnages-stylises-2026-10-20/README.md) :
proportions élancées, volumes sculptés, visages expressifs, cheveux en grandes
mèches et matières distinctes, avec un alignement très fidèle demandé.
Les modèles de cet atelier restent des prototypes ; leur validation artistique
contre cette référence reste à faire.

## Ouvrir l’atelier

Depuis la racine du dépôt :

```powershell
node docs/art-direction/resources/serve.mjs 51805
```

Ouvrir [l’atelier d’apparence](http://127.0.0.1:51805/personnages/viewer/index.html).
Il est aussi accessible par **Créer une apparence** dans la bibliothèque DA.

![Humain dans l’atelier](humain-atelier.png)

![Elfe dans l’atelier](elfe-atelier.png)

## Choix proposés

| Option | Choix |
|---|---|
| Race | Humain, Elfe |
| Silhouette | Masculine, féminine pour chaque race |
| Taille humaine | 160 à 195 cm |
| Taille elfique | 170 à 205 cm |
| Cheveux | Courts, longs, tressés |
| Couleurs | Peau, cheveux, yeux et tissu |
| Observation | Vue complète, portrait, rotation et zoom |

Les teintes de peau et de cheveux sont accessibles aux deux races. Les plages
de taille sont des choix de cette proposition, modifiables dans le manifeste.
La taille correspond ici à la hauteur totale du modèle, coiffure comprise ;
elle applique une mise à l’échelle proportionnelle et conserve les pieds au sol.
Il ne s’agit pas encore d’un réglage osseux ni d’une morphologie indépendante
des membres.

Les Humains ont des oreilles arrondies et des proportions plus compactes.
Les Elfes ont des oreilles pointues et une silhouette affinée. Les tenues
de présentation conservent les volumes et les accessoires des études fantasy
existantes ; elles ne constituent pas un système d’équipement.

L’enregistrement conserve un préréglage **dans ce navigateur**. L’export JSON
transporte les identifiants des choix et leur version ; l’export GLB conserve
l’apparence affichée. Aucun compte ni personnage persistant n’est créé sur
le backend.

## Fichiers et provenance

- `manifest.json` : choix, palettes, tailles et chemins des modèles.
- `models/` : variantes GLB autonomes.
- `assets/` : sources Blender éditables.
- `scripts/generate_characters.py` : génération reproductible depuis les études V3.
- `scripts/validate_characters.py` : contrôle des géométries et des contrats d’export.
- `viewer/` : interface et gestion des préréglages locaux.
- [Crédits](CREDITS.md) : bases Rain et Snow de Blender Studio, sous CC BY 4.0,
  modifications pour Arkya et attribution à conserver.

Les sources V3 et les références DA approuvées sont conservées. Les fichiers
de cette proposition restent dans leur propre dossier, hors des packs Scinty.

La branche [personnages de jeu](game/README.md) contient les quatre FBX riggés
utilisés dans Unity. Sa passe peinte V2 conserve davantage de détail au visage
et dans les cheveux, enrichit les creux et arêtes des tenues et distingue les
réponses de matière dans le shader Unity. Les études GLB de cet atelier sont
conservées séparément. Voir le [guide de création en jeu](../../../CHARACTER_CREATION.md)
pour les contrats d'apparence et la persistance déjà intégrés.

## Génération et vérification

Les douze modèles contiennent entre **74 749 et 94 704 triangles**. Les surfaces
utilisent les couleurs de sommets `Painted` : la peau conserve les variations
du visage après neutralisation du teint d’origine. Le GLB garde ses quatre
matières personnalisables neutres ; le fichier Blender montre une apparence
d’exemple, modifiable dans les matériaux.

```powershell
& 'D:/Tools/BlenderPortable/blender-4.5.14-windows-x64/blender.exe' --background --disable-autoexec --python 'docs/art-direction/resources/personnages/scripts/generate_characters.py'
python docs/art-direction/resources/personnages/scripts/validate_characters.py
node --test docs/art-direction/resources/personnages/viewer/appearance.test.mjs
```

Le générateur relit les sources V3 et remplace les livrables de cette proposition.
Ses fichiers intermédiaires sont dans `artifacts/characters/staging/`.

Les [contrôles des douze modèles](validation.json) vérifient les buffers,
indices, couleurs, normales, dimensions, pieds au sol, variantes de coiffure et
différences des oreilles. Les quatre tests de logique couvrent les préréglages
invalides, les bornes de taille et les choix aléatoires.

Les [essais dans Edge](viewer-verification.json) couvrent les douze variantes,
les quatre couleurs, la taille au clavier, la sauvegarde/restauration après
rechargement, le refus d’une ancienne version et les exports JSON/GLB. Un GLB
personnalisé a été relu indépendamment : hauteur de **2,05 m**, pieds à zéro,
quatre teintes conservées. Les vues ordinateur et téléphone ont été examinées,
ainsi que les portraits des deux silhouettes elfiques. Aucune validation Unity
n’est revendiquée pour cet atelier hors du client.

## Contrat et suite de l’intégration

Le préréglage versionné contient `race`, `silhouette`, `heightCm`, `skin`,
`hairStyle`, `hairColor`, `eyeColor` et `outfitColor`. Les valeurs sont bornées
par le catalogue ; une sauvegarde incompatible doit être refusée.

Ces modèles sont des propositions statiques pour examiner les races et les
choix d’apparence. Le rig, les animations, les déformations, les LOD et les
budgets définitifs du jeu restent à réaliser. L’atelier n’ajoute pas d’asset
au projet Unity et ne modifie pas la simulation DOTS.

L’intégration dans la création de personnage du jeu devra faire valider les
identifiants et la taille par Characters avant persistance, conserver une
version du catalogue et appliquer une apparence par défaut aux anciens
personnages. Les statistiques et collisions de gameplay restent sous
autorité serveur ; la taille visuelle ne leur donne aucun effet implicite.
L’authentification, l’admission et la réplication réseau restent distinctes.

Le viewer utilise le composant local `model-viewer` déjà présent dans Arkya.
Ses API de couleur, de transformation et d’export sont documentées dans les
[exemples officiels](https://modelviewer.dev/examples/scenegraph/).
