# Atelier du Brise-Sève

Vue locale du GLB animé, avec comparaison au deuxième concept, caméra orbitale,
rotation automatique, choix d’animation, lecture/pause, vitesse et position dans
le cycle. Les boutons d’animation sont créés uniquement à partir de
`model.availableAnimations` après le chargement du GLB.

Depuis la racine du dépôt :

```powershell
node docs/art-direction/resources/serve.mjs 51805
```

Ouvrir [l’atelier local](http://127.0.0.1:51805/monsters/brise-seve/viewer/index.html).
Le fichier `model-viewer.min.js` est servi par la route locale `/vendor/` du
serveur commun ; aucun CDN ni nouvelle dépendance n’est nécessaire.

## Fichiers attendus

Les chemins sont relatifs au dossier parent `brise-seve/` :

- `models/brise-seve.glb` : modèle, matériaux, squelette et animations.
- `assets/brise-seve.blend` : source éditable.
- `assets/brise-seve.fbx` : export pour Unity.
- `reference/concept-v2.png` : concept retenu.
- `manifest.json` : métadonnées facultatives pour l’atelier.

Champs facultatifs du manifeste : `revision`,
`dimensions: { heightM, lengthM, widthM }`, `rig: { bones }`, `triangles` et
`files: { glb, blend, fbx, concept }`. Les chemins de `files` sont relatifs au
dossier parent. Une absence de manifeste ne masque pas un GLB valide.

Les noms de clips `Idle`, `Walk`, `ChargeAnticipation`, `Charge`, `Attack`, `Hit`
et `Death` reçoivent des
libellés français ; tout autre clip présent est également proposé sous son nom
réel. Les clips de préparation, d’attaque, d’impact et de mort sont lus une seule
fois et conservent leur pose finale ; le bouton Lecture permet de les relancer.
Repos, marche et charge tournent en boucle. Le curseur permet d’arrêter la lecture pour examiner une pose. Les
animations sont présentées sur place ; cette page n’ajoute aucun déplacement ni
comportement de gameplay.

## Accessibilité

Les boutons et champs utilisent les contrôles HTML natifs. La vue 3D est
manipulable au clavier via les flèches, à la souris et au toucher. Le concept
possède un texte alternatif. La préférence système de réduction des animations
démarre la lecture en pause ; l’utilisateur peut ensuite la lancer. La rotation
automatique est désactivée par défaut. Les échecs de chargement affichent un
message, les liens des sources et une action pour recharger la page.

La vue navigateur sert à la revue artistique. Elle ne constitue pas une
validation des imports, matériaux, transitions ou performances dans Unity.
