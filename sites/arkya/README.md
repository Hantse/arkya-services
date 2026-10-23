# Arkya — site vitrine et atlas visuel

Vitrine bilingue français / anglais du projet et de ses directions artistiques. Les fichiers servis sont dans `dist/` : HTML, CSS, JavaScript et images WebP. La consultation publique ne demande aucun build, compte, backend ou dépendance JavaScript externe. La connexion Discord nécessite le service Identity. La page Installer le launcher distribue le launcher Windows sans exiger de session : le jeu se lance depuis le launcher.

## Lancer localement

Depuis ce dossier :

```powershell
node serve.mjs 51806
```

Ouvrir <http://127.0.0.1:51806>. Le serveur expose uniquement `dist/`, sur l’interface loopback. Ouvrir directement `index.html` en `file://` ne permet pas le chargement du catalogue JSON.

## Contenu et interactions

- Accueil avec trois ambiances issues des concepts Royaumes de l’Aube, Cités d’Éther et Terres des Anciens.
- Présentation de la référence arbre V3 avec ses trois matières.
- Atlas de **205 visuels** dans sept catégories, recherche tolérante aux accents, filtres de statut, pagination et liens de recherche conservés dans l’URL.
- Vue agrandie avec précédent/suivant, flèches du clavier, fermeture par Échap et restitution du focus.
- Personnages, trois thèmes HUD V3 et présentation de l’état réel du prototype Unity.
- Navigation mobile, libellés accessibles, réduction des mouvements et crédits Rain/Snow.
- Sélecteur FR / EN sur ordinateur et mobile : textes, métadonnées, libellés d’accessibilité, 205 titres et descriptions de l’atlas traduits. Les textes incorporés dans les œuvres originales restent ceux de ces images.
- Page [Classes & grimoire](https://arkya-atlas.easykrill33.chatgpt.site/classes.html?lang=fr), reliée aux menus et à l’accueil : cinq classes, 25 capacités FR/EN, exploration des rangs aux niveaux 1–10, exemple interactif de Heal et calculateur de rating.
- Page `dist/development.html` en FR/EN, reliée à l’accueil, aux classes et au compte : état du prototype, quêtes, inventaire/récolte, totems et services en préparation. Une rubrique technique repliable présente YARP, les images Docker et le déploiement k3s sans publier les adresses privées.

## Classes et gameplay

La page `dist/classes.html` reprend les règles de [classes et progression](../../docs/GAMEPLAY_CLASSES.md) et la [proposition de premières capacités](../../docs/GAMEPLAY_STARTER_SKILLS.md). Elle annonce explicitement que ce système n’est pas encore disponible en jeu. Les principes confirmés, les seuils/héritage de Heal et les propositions de capacités restent distincts ; aucun chiffre de dégâts, portée, coût ou délai n’est inventé.

`dist/classes-data.js` contient les cinq classes et leurs cinq capacités bilingues. `classes-copy.js` contient le texte de la page et étend les libellés partagés de `i18n.js` ; `classes.js` gère les interactions, `classes.css` leur présentation. Modifier ensemble les deux langues si la conception évolue. Les SVG des cinq symboles sont dessinés pour la navigation ; l’image d’en-tête réutilise le concept Cités d’Éther existant.

Le grimoire suppose les rangs accessibles appris ; il ne simule pas leur équipement ni leurs conditions de lancement. `?class=cleric&level=5&lang=fr` permet de partager une sélection. Le calculateur exclut les classes verrouillées et affiche un tiret quand aucune n’est débloquée. Sa plage 1–10 et son arrondi à deux décimales sont pédagogiques, pas des règles de niveau maximal ou d’affichage du jeu. Les exemples ne créent aucune progression persistante.

Les liens de page marqués `data-language-link` conservent explicitement la langue dans l’URL, même si le stockage du navigateur est désactivé.

## Pages juridiques et compte Discord

Les pages `dist/terms.html` et `dist/privacy.html` présentent les Conditions d’utilisation et la Politique de confidentialité en FR/EN, avec **Pyxion Sarl-S** comme éditeur/responsable et **support@arkya.online** comme contact. Le français est présent dans le HTML ; `legal-copy.js` fournit les traductions et `legal.js` les interactions. Les liens sont accessibles depuis les pieds de page et l’écran de connexion. Ces textes décrivent la vitrine actuelle et la future connexion séparément ; revoir la politique lors de l’activation d’Identity.

`dist/account.html`, `account-copy.js`, `account.js` et `account.css` composent l’écran de compte. La connexion Discord utilise le même onglet : `auth-client.mjs` conserve temporairement la preuve S256 dans `sessionStorage`, puis Identity renvoie le navigateur sur la page Compte. Le site reprend l’échange et valide `/me` avant d’afficher le compte ou de retourner à la page du launcher. La destination de retour conserve seulement la langue et l’éventuel `returnTo=play.html`. Si le stockage de session est indisponible, la connexion affiche une erreur avant de quitter le site. `common.js` restaure la session et met à jour la navigation. Le bearer Arkya reste lié à l’origine API et n’est jamais placé dans une URL. La déconnexion appelle le backend puis efface l’état local même si le serveur ne répond pas.

**Configuration de cette révision :** `dist/auth-config.js` pointe Identity et Characters sur `https://api.arkya.online` via YARP. Une panne DNS, HTTP ou CORS est une erreur visible ; aucun compte ni monde local n’est simulé. La disponibilité publique des services et le parcours Discord réel restent à vérifier avant de considérer la bêta utilisable. Le [guide de raccordement Discord](DISCORD_SETUP.md) fournit le callback actualisé et les contrôles d’activation. L’achat du domaine chez Gandi ne déplace pas la vitrine vers `arkya.online`.

## Sessions renouvelables

Le site demande désormais un cookie de renouvellement HttpOnly à Identity.
Le bearer reste dans sessionStorage (8 heures par défaut) ; le cookie permet de
retrouver la connexion pendant 30 jours absolus, y compris dans un nouvel onglet.
Les rotations et la déconnexion sont sérialisées entre onglets avec Web Locks.
La déconnexion révoque toute la famille ; aucun refresh token n’est accessible au
JavaScript. Le domaine `arkya.online` et l’API `api.arkya.online` sont compatibles
avec SameSite=Lax ; la vitrine historique sur un domaine tiers ne bénéficie pas
de cette persistance. [Contrats, migration et limites Unity](../../docs/AUTH_SESSIONS.md).

## Installer et lancer Arkya sur Windows

`dist/play.html` est la page de téléchargement du launcher, en français et en anglais.
Les liens **Installer le launcher** de l’accueil, des menus et du compte y mènent
en conservant la langue. Le bouton principal télécharge l’installateur Windows
`Arkya-Launcher-Setup.exe` depuis la release versionnée `launcher-v0.3.1` ; la page
explique l’assistant d’installation du launcher, sa connexion Discord dans le
navigateur, puis le choix du canal et du dossier du jeu dans **Installer le jeu**,
avant **Jouer**. Le dossier du jeu peut être choisi sur un
autre disque, indépendamment du launcher. Le téléchargement
reste disponible sans compte, sans JavaScript et lorsque Identity est indisponible.
La connexion Discord du launcher est distincte de celle du site. Le launcher
affiche sa page de connexion avant d’ouvrir son accueil.

La page ne contient plus de canvas, de contrôle plein écran ni de bootstrap Unity.
Elle charge seulement les traductions et la navigation de compte partagée.
Aucun manifest, payload WebGL, admission ou transfert de session vers Unity n’est
chargé depuis cette page, même si une session de site existe. Le renouvellement
du compte web continue dans `common.js` sans conditionner le téléchargement.

Le manifeste de la Bêta Windows reste servi par
`dist/downloads/windows/latest-win-x64.json`. Le canal Stable dispose de
`dist/downloads/windows/latest-stable-win-x64.json`, contenant explicitement
`{ "schemaVersion": 1, "channel": "stable", "available": false }` tant qu’aucune
Stable n’est publiée. Il n’existe aucun repli automatique de Stable vers Bêta.
Les adresses sont fixées dans le launcher. Les archives et leur publication
sont décrites dans le [guide du launcher](../../docs/WINDOWS_LAUNCHER.md).
Modifier le lien de téléchargement lorsque la release du launcher change ;
les mises à jour du jeu passent par le manifeste.

Les modules et tests du client WebGL historique restent conservés pour les
contrats et outils de build ; ils ne sont plus importés par les pages publiques.
Le scénario `tests/play-real.mjs` décrit ce parcours historique et ne s’applique
plus à `play.html`. Les captures et validations datées ci-dessous sont des
archives, pas le comportement actuel du site.

Vérification : `node --test tests/*.test.mjs`, puis `tests/play.browser.mjs`
avec Playwright CLI sur le serveur local. Ce scénario vérifie le téléchargement
sans session et en panne API, les deux langues, les formats mobile/bureau,
l’absence de chargement Unity et le retour de connexion d’un ancien lien
`account.html?returnTo=play.html` vers la nouvelle page. Les scénarios
`account.browser.mjs` et `refresh.browser.mjs` couvrent les sessions web.

## Vérification du passage au launcher — 23 octobre 2026

- **109 tests Node passent**, dont les contrats d’authentification et de refresh conservés.
- **123 contrôles Edge/Playwright passent** sur la page d’installation et ses liens :
  français/anglais, largeurs 320 à 1440 px, compte anonyme ou connecté, panne Identity,
  retour d’un ancien lien de connexion, téléchargement sans JavaScript, absence de
  canvas, de chargement Unity et d’admission depuis le site. Ils vérifient aussi
  le lien `.exe` 0.3.1, l’assistant du launcher, la connexion Discord du launcher
  avant l’accès à l’accueil, le choix du canal Bêta et du dossier du jeu, ainsi
  que le marqueur Stable explicitement indisponible ;
  l’installateur lui-même n’est pas téléchargé par ce scénario navigateur.
- **17 contrôles compte et 15 contrôles refresh passent** avec les fixtures isolées.
  Ils ne constituent pas une nouvelle connexion Discord réelle.
- Rendus inspectés sur bureau et mobile, dont l’accueil sans chevauchement du
  nouveau bouton et de la légende artistique. Captures dans
  `output/playwright/arkya-launcher-download-{desktop,mobile}.png` et
  `arkya-launcher-home-mobile.png` à la racine du dépôt.
- La vérification ne relance pas le binaire Windows ; son build et sa validation
  restent décrits dans le guide de distribution.

## Actualiser l’avancement

`dist/development-copy.js` fournit les textes FR/EN de la nouvelle page ; `development.html` contient aussi les textes français de repli. Actualiser les deux ensemble et conserver la distinction entre consultation publique, prototypes locaux, conception et déploiement préparé. Les sources sont les guides du projet (`docs/ARCHITECTURE.md`, `QUEST_CONTENT.md`, `GAMEPLAY_FOUNDATION.md`, `GAMEPLAY_TOTEMS.md`, `DISCORD_SOCIAL.md`) et `backend/deploy/gateway/README.md`. L’accueil reste centré sur la présentation ; les détails d’exploitation restent dans les guides du dépôt.

La configuration du client ne déploie pas les services : suivre `DISCORD_SETUP.md` pour valider leur accès et la connexion réelle. Les messages d’erreur restent visibles en cas d’indisponibilité. Aucun nouveau visuel n’a été ajouté pour cette actualisation ; l’atlas conserve ses 205 images et ses crédits.

## Langues

La langue suit cet ordre : paramètre valide `?lang=fr` ou `?lang=en`, préférence `arkya-language` mémorisée dans le navigateur, puis langue du navigateur (anglais pour `en`, français sinon). Le sélecteur conserve les filtres, la recherche, la pagination et les choix d’ambiance, de matière et de HUD. Les recherches acceptent les termes français et anglais, indépendamment de la langue affichée.

`dist/i18n.js` contient les textes statiques et le choix de langue. `dist/app.js` localise les interactions et les fiches à partir de `dist/gallery.en.json`, indexé par les identifiants stables du catalogue français. Une langue peut être partagée directement : [français](https://arkya-atlas.easykrill33.chatgpt.site/?lang=fr) ou [anglais](https://arkya-atlas.easykrill33.chatgpt.site/?lang=en).

Les traductions anglaises du catalogue se régénèrent sans toucher aux images avec `python scripts/translate-gallery.py`. Le générateur complet l’appelle aussi automatiquement. Une phrase nouvelle sans traduction provoque une erreur explicite, afin d’éviter une publication partiellement traduite.

Les **117 modèles de ressources approuvés** excluent les arbres. L’atlas comprend 124 références approuvées en ajoutant trois matières d’arbre V3 et quatre formes d’arbre, 64 propositions, neuf explorations et huit captures du prototype Unity. Les images des rigs sont des rendus Blender classés comme propositions. Le champ `origin` est distinct du statut artistique.

Les concepts générés sont identifiés comme tels. Les maquettes UI montrent des fonctionnalités illustratives. Le site ne promet pas de date de sortie et ne présente pas les prototypes comme un MMORPG terminé.

## Actualiser les images

Le générateur requiert Python et Pillow et doit être exécuté dans sa position `sites/arkya/` au sein du dépôt Arkya :

```powershell
python scripts/prepare-assets.py
```

Il lit les catalogues et les images de `docs/art-direction/` et `docs/world/`, produit les copies WebP, `dist/gallery.json` et `dist/asset-provenance.json`. Les images sources et les assets Unity restent intacts. Les archives d’anciennes itérations et les cartes de textures ne sont pas exposées dans la vitrine. Les visuels totalisent environ **8,9 Mio**, chargés progressivement.

Pour ajouter une famille, compléter le générateur et vérifier son statut dans la documentation artistique : ce statut n’est pas fiable s’il est déduit seulement du nom d’un fichier ou de son existence. Les crédits des bases de personnages doivent être conservés.

## Identité visuelle

Bleu nuit `#111D28`, fond profond `#0B151E`, bleu ardoise `#1B2B37`, ivoire `#F0EEE5`, or patiné `#CFB27B`, sauge `#9FB59F`. Titres Georgia et lecture Segoe UI, avec polices système de repli. L’iconographie de navigation et le signe Arkya sont en SVG/CSS ; les visuels restent ceux du projet.

## Hébergement

Le site Sites associé est identifié par `.openai/hosting.json`. Conserver cet identifiant lors des prochaines publications. Le répertoire statique publié est `dist`. Les sources Git et l’archive sont gérées par le workflow du plugin Sites ; aucun jeton n’est stocké ici. L’accès est public à la demande du propriétaire ; le préserver aux publications suivantes.

Adresse : <https://arkya-atlas.easykrill33.chatgpt.site>.

Sur Windows, le helper de publication appelle Bash puis GNU tar. Utiliser le Bash de Git for Windows dans le `PATH`, avec `TAR_OPTIONS=--force-local`, afin que le lecteur `D:` soit traité comme un chemin local plutôt qu’un hôte distant. Ces variables restent limitées au processus de publication.

## Vérification du lancement web — 18 octobre 2026

- **92 tests Node passent** (`node --test tests/*.test.mjs`) : 25 cas Identity, 19 cas du contrôle de session/manifest, 39 cas de métadonnées et d’intégrité des morceaux, quatre cas de préparation du build et cinq cas de durée de vie du loader. Ils couvrent notamment `/me` avant/après chargement, expiration, rejet/révocation, arrêt, corruption/troncature/réordonnancement, tailles bornées, SHA-256 des morceaux et fichiers, annulation pendant le hash, nettoyage des Blob et conservation du build précédent en cas d’échec de préparation.
- **61 contrôles Edge/Playwright passent** : navigation Jouer FR/EN, retours sûrs, écrans entre 320 et 1440 px, absence de chargement Unity sans session, panne API, import de session, refus Unity, ordre `ClearSiteSession` → `Quit`, build absent et absence d’exception JavaScript. Les fixtures API/loader sont confinées à `tests/play.browser.mjs`, hors `dist/` ; elles ne constituent pas une connexion Discord ni une exécution du vrai build Unity.
- Captures inspectées : `output/playwright/arkya-web-sign-in.png` et `arkya-web-mobile.png` à la racine du dépôt Arkya. Aucun asset Unity ni service de production modifié par ce contrôle.
- **Client WebGL réel vérifié localement** dans Edge avec les vrais services Identity/Characters/Realms, MongoDB isolé et le dedicated Windows : `/me` côté site et Unity, catalogue et sélection de personnage, admission, WebSocket, île rendue, marche/course/saut au clavier, sauvegarde et retour à la création. Une session de test enregistrée dans la base a été utilisée ; aucune réponse API ou Unity n'a été simulée. La connexion Discord publique n'est pas couverte par ce parcours.
- **Persistance vérifiée indépendamment dans MongoDB** : déplacement d'Aelys, sauvegarde automatique, sauvegarde finale et libération de la lease, puis nouvelle admission à la position exacte sauvegardée. La session principale a été conservée pour la validation Linux. [Rapport du premier parcours](../../artifacts/web-beta/browser-real-windows/observations.json).
- **Révision d'interface contrôlée sur le vrai build** : les rangées de personnalisation restent lisibles après le retour de l'île. Captures prises pendant les touches de marche/course et pendant le saut ; aucun chevauchement sur le retour contrôlé à 1600 × 1400. Le dernier retour confirme une position sauvegardée, révision 37, époque 6 et aucune lease active. [Captures, console et mesures](../../artifacts/web-beta/browser-final-windows/observations.json).
- **Mesures locales, sans garantie de performance générale** : première image à 6,505 s sur serveur local ; heap WASM maximal de 738,4 Mio réservé / 728,2 Mio utilisé, heap JavaScript utilisé maximal de 178,3 Mio. Sur l'île sous la pluie, médiane de 26,6 FPS sur cette session Edge headless avec une compilation Unity concurrente ; le studio approche 60 FPS. Ces métriques n'incluent pas toutes les allocations du navigateur ou du GPU.
- **Ressources de rendu revérifiées sur le build corrigé** : premier affichage du studio à 2,866 s en local, nouvelle admission sur le dedicated Windows, île rendue et retour à la création lisible. Aucune erreur console, aucune occurrence SSAO et aucune exception JavaScript non gérée. L'avertissement FSR de la passe non utilisée sous WebGL subsiste, ainsi que l'avertissement `Content-Length` du serveur local. [Rapport et captures de cette passe](../../artifacts/web-beta/browser-resources-windows/observations.json).
- **Dedicated Linux vérifié en exécution locale** avec le vrai client WebGL et les vrais services : admission sur l’île, puis arrêt du serveur par `SIGTERM` et retour automatique du navigateur à la création. Cette validation porte sur le processus Linux réel ; elle ne confirme pas un déploiement public.
- **Livraison en morceaux vérifiée dans Edge** : les deux payloads volumineux du dernier build réel sont remplacés par 23 morceaux, maximum 4 194 304 octets. Tailles et SHA-256 des 23 morceaux et des deux fichiers reconstitués sont revérifiés ; aucun gros payload complet ne reste sous `dist/game/`. Le parcours Blob affiche le studio, permet l’admission sur Linux et les déplacements/course/saut réels.
- **Révocation pendant la partie vérifiée** : un vrai `POST /v1/auth/logout` externe retourne 204. Le dedicated ferme la connexion et libère le bail avant la revalidation périodique du site ; le navigateur revient à la création avec le message de connexion interrompue. MongoDB conserve la dernière révision acceptée. [Observations de cette passe](../../artifacts/web-beta/browser-chunks-linux/observations.json).
- **Encore à vérifier** : connexion Discord publique, accès HTTPS/WSS public et publication de cette révision. Aucun échec mémoire pendant les parcours locaux ; cela ne vaut pas validation des services publics.

## Vérification — 16 octobre 2026

- Syntaxe JavaScript vérifiée avec `node --check`.
- Les 205 images du catalogue sont décodables ; dimensions, sources, identifiants uniques et provenance contrôlés.
- 18 contrôles navigateur réussis dans Microsoft Edge avec Playwright : catégories, recherche sans accents, filtres combinés, état vide, pagination, agrandissement, flèches/Échap et retour du focus, ambiance, matière, thème HUD, huit captures Unity, persistance des filtres et menu mobile.
- Rendu inspecté à 1440 × 1000 et 390 × 844, sans débordement horizontal mobile.
- Captures et scénario de vérification dans `output/playwright/arkya-*` à la racine du dépôt Arkya.
- Aucun code ou asset Unity modifié ; compilation Unity non concernée.
- Extension FR/EN : 26 contrôles navigateur réussis, dont traduction des métadonnées/fiches, recherche bilingue, filtres et pagination conservés, choix de langue mémorisé, priorité URL, menu mobile et absence de débordement/chevauchement à 390 et 320 pixels. Scénario : `output/playwright/arkya-i18n-qa.js` à la racine du dépôt.
- Quatre contrôles complémentaires réussis : messages d’erreur traduits, HUD traduit même si le catalogue est indisponible, reprise du chargement et changement de langue avec stockage navigateur désactivé (`arkya-i18n-error-qa.js`).
- Extension Classes : 39 contrôles navigateur réussis (sélection des cinq classes, déblocages aux niveaux 1/5/10, héritage Heal, rating et aucune classe débloquée, changement de langue sans perte d’état, liens FR/EN, rechargement direct, menu mobile/Échap, accueil et galerie conservés). Aucun message d’erreur navigateur ; pas de débordement horizontal sur l’accueil et la nouvelle page à 1440, 820, 390 et 320 pixels. Résultats et captures : `output/playwright/arkya-classes-*` à la racine du dépôt. Les textes ont aussi été recoupés avec les deux documents de conception.
- Extension juridique/Discord : 25 tests unitaires du client d’authentification (`node --test tests/auth-client.test.mjs`) et 52 contrôles Edge/Playwright réussis. FR/EN, liens, largeurs 1440/820/390/320, état sans backend, connexion simulée, restauration entre pages/rechargement, révocation, refus, annulation et panne du backend vérifiés. Les réponses 410/503 sont intentionnelles dans les scénarios d’erreur ; aucune exception JavaScript non gérée. Scénario et captures : `output/playwright/arkya-legal-auth-qa.js` et `arkya-{terms,privacy,account-mobile}.png` à la racine du dépôt. Le serveur simulé reste hors du site publié.
- Identity : 106 tests réussis, dont les nouveaux cas CORS ; trois tests MongoDB non exécutés faute de `ARKYA_TEST_MONGO`. Connexion réelle Discord non vérifiée : Identity public et ses secrets restent à configurer.
- Cinq contrôles complémentaires réussis après revue : quitter l’écran pendant l’autorisation, restaurer la page par Précédent/Suivant, effacer l’ancienne URL et relancer une connexion (`output/playwright/arkya-auth-history-qa.js`).
- Actualisation Avancement : 60 contrôles Edge/Playwright réussis sur l’accueil, les classes, le compte et la nouvelle page en FR/EN ; quatre contrôles supplémentaires valident l’architecture dépliée à 1440 et 320 pixels. Navigation mobile, conservation de la langue, métadonnées, état Discord désactivé et absence d’appels API vérifiés. Rendus bureau/mobile et page complète inspectés ; aucune exception JavaScript non gérée. Scénarios et captures : `output/playwright/arkya-progress-*` à la racine du dépôt. Les 25 tests du client d’authentification passent toujours.
