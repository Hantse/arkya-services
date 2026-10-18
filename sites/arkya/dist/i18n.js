'use strict';

(() => {
  const translations = {
  "fr": {
    "meta.description": "Découvrez Arkya, un MMORPG en construction. Univers, matières peintes, personnages et directions artistiques : entrez dans les coulisses d’un monde qui prend forme.",
    "meta.title": "Arkya — Un monde prend forme",
    "meta.ogDescription": "Un atlas visuel du MMORPG Arkya. Explorez ses univers, ses créations et sa direction artistique.",
    "page.title": "Arkya — Un monde prend forme",
    "nav.skip": "Aller au contenu",
    "nav.home": "Arkya, accueil",
    "nav.main": "Navigation principale",
    "nav.mobile": "Navigation mobile",
    "nav.vision": "La vision",
    "nav.creations": "Les créations",
    "nav.interfaces": "Les interfaces",
    "nav.project": "Le projet",
    "nav.development": "Avancement",
    "nav.classes": "Les classes",
    "nav.account": "Connexion Discord",
    "nav.play": "Jouer",
    "footer.terms": "Conditions d’utilisation",
    "footer.privacy": "Politique de confidentialité",
    "gameplay.eyebrow": "LES VOIES D’ARKYA",
    "gameplay.title": "Cinq classes.<br>Votre <em>propre voie.</em>",
    "gameplay.description": "Découvrez les classes, le multiclassage et les premiers sorts imaginés pour Arkya. Un grimoire pour comprendre les choix qui se dessinent.",
    "gameplay.cta": "Découvrir les classes",
    "nav.atlas": "Ouvrir l’atlas",
    "nav.language": "Choisir la langue",
    "hero.eyebrow": "MMORPG · CARNET DE CRÉATION",
    "hero.title": "Un monde<br>prend <em>forme.</em>",
    "hero.description": "Des terres à découvrir. Des histoires à partager.<br>Entrez dans l’univers en construction d’Arkya.",
    "hero.explore": "Explorer les créations",
    "hero.captionNote": "Concept généré · exploration artistique",
    "hero.journey": "LE VOYAGE COMMENCE ICI",
    "hero.vision": "Découvrir la vision",
    "hero.development": "PROJET EN DÉVELOPPEMENT",
    "directions.eyebrow": "LES PREMIÈRES EXPLORATIONS",
    "directions.title": "Trois regards.<br>Un même monde à imaginer.",
    "directions.description": "Changez d’horizon. Ces pistes explorent les ambiances d’Arkya ; elles ne fixent pas encore son histoire.",
    "directions.choose": "Choisir l’ambiance de la vitrine",
    "directions.dawnMood": "CHALEUR & AVENTURE",
    "directions.dawn": "Royaumes de l’Aube",
    "directions.aetherMood": "MAGIE & ARTISANAT",
    "directions.aether": "Cités d’Éther",
    "directions.ancientsMood": "NATURE & MYSTÈRE",
    "directions.ancients": "Terres des Anciens",
    "vision.approved": "Référence validée",
    "vision.artLabel": "ARBRE · FINITION PEINTE V3",
    "vision.eyebrow": "LA DIRECTION QUI PREND RACINE",
    "vision.title": "La matière raconte<br>déjà <em>une histoire.</em>",
    "vision.lead": "Des silhouettes qui se reconnaissent.<br>Des surfaces qui semblent peintes à la main.",
    "vision.detail": "Une écorce noueuse, le bord doux d’une pierre, une lueur dans un cristal. L’identité d’Arkya se construit dans ces détails, avec des volumes soignés et des couleurs lisibles, même à distance.",
    "vision.reference": "L’arbre peint V3 est la référence retenue pour les objets d’environnement. Son langage se prolonge dans dix familles de ressources validées.",
    "vision.material": "Matière de l’arbre",
    "vision.moss": "Ancien moussu",
    "vision.mystic": "Mystique",
    "vision.burnt": "Brûlé",
    "vision.resources": "modèles de ressources validés<br><small>hors arbres de référence</small>",
    "vision.seeApproved": "Voir les ressources validées",
    "collection.eyebrow": "L’ATLAS VISUEL",
    "collection.title": "Chaque création,<br>un morceau <em>d’Arkya.</em>",
    "collection.description": "Des premières intentions aux objets du monde.<br>Parcourez les familles, les matières et les variantes.",
    "collection.filter": "Filtrer par famille",
    "category.all": "Tout découvrir",
    "category.world": "Univers",
    "category.vegetation": "Végétation",
    "category.minerals": "Minéraux",
    "category.magic": "Magie & outils",
    "category.characters": "Personnages",
    "category.architecture": "Architecture",
    "category.interfaces": "Interfaces",
    "collection.search": "Rechercher une création",
    "collection.placeholder": "Un arbre, du mana, une matière…",
    "collection.show": "Afficher",
    "filterStatus.all": "Tous les statuts",
    "filterStatus.approved": "Références validées",
    "filterStatus.proposal": "Propositions",
    "filterStatus.exploration": "Explorations",
    "filterStatus.prototype": "Captures du prototype",
    "collection.creations": "Créations d’Arkya",
    "collection.emptyTitle": "Aucune création pour cette recherche.",
    "collection.emptyDescription": "Essayez une autre matière ou explorez toutes les familles.",
    "collection.reset": "Réinitialiser les filtres",
    "collection.errorTitle": "L’atlas n’a pas pu être chargé.",
    "collection.errorDescription": "Rechargez la page pour retrouver les créations.",
    "collection.retry": "Réessayer",
    "collection.more": "Voir plus de créations",
    "characters.eyebrow": "LES VISAGES D’ARKYA",
    "characters.title": "L’aventure commence<br>avec <em>vous.</em>",
    "characters.description": "Humains et Elfes donnent corps aux premières recherches de personnages. Silhouettes, coiffures, teintes et vêtements composent un atelier d’apparence. Ces recherches se prolongent dans une création de personnage déjà intégrée à Unity.",
    "characters.note": "Leur direction artistique reste une proposition en cours de validation.",
    "characters.discover": "Découvrir les personnages",
    "characters.alt": "Portrait 3D de la piste Terres des Anciens, adapté de Rain",
    "characters.caption": "Études de personnages · proposition artistique",
    "interfaces.eyebrow": "UNE FENÊTRE SUR LE MONDE",
    "interfaces.title": "Trois façons<br>de vivre <em>l’interface.</em>",
    "interfaces.description": "Cuivre patiné, carnet de voyage ou pierre lumineuse.<br>Trois habillages proposés pour un même HUD d’exploration.",
    "interfaces.choose": "Choisir un thème d’interface",
    "interfaces.copper": "Encre & cuivre",
    "interfaces.journal": "Carnet d’exploration",
    "interfaces.mana": "Pierre de mana",
    "interfaces.note": "Maquette générée · fonctionnalités illustratives",
    "project.alt": "L’île de départ dans le prototype Unity d’Arkya",
    "project.caption": "CAPTURE UNITY · PROTOTYPE EN DÉVELOPPEMENT",
    "project.eyebrow": "DU DESSIN AU MONDE JOUABLE",
    "project.title": "Les premières terres<br>existent <em>déjà.</em>",
    "project.description": "Arkya est un projet de MMORPG en développement. Une île de départ, des personnages personnalisables et les premières fondations multijoueurs prennent place dans Unity.",
    "project.environmentTitle": "Un environnement vivant",
    "project.environment": "Terrain éditable, cycle jour et nuit, pluie, vent et océan violet dessinent l’atmosphère de l’île.",
    "project.multiplayerTitle": "Des fondations multijoueurs",
    "project.multiplayer": "Les prototypes explorent le déplacement, le combat et la continuité entre plusieurs zones serveur.",
    "project.accountsTitle": "Les comptes se préparent",
    "project.accounts": "Le parcours Discord et les services de compte sont développés. Leur déploiement et leur activation publique restent à vérifier.",
    "project.updates": "Voir l’avancement du projet",
    "project.futureTitle": "Une aventure encore à construire",
    "project.future": "Le monde persistant et le parcours complet du joueur restent à relier. Aucune date de sortie n’est annoncée.",
    "closing.eyebrow": "LE CARNET RESTE OUVERT",
    "closing.title": "Un univers se construit.<br><em>Création après création.</em>",
    "closing.atlas": "Retourner dans l’atlas",
    "footer.description": "MMORPG en construction.<br>Univers, recherches et créations.",
    "footer.credits": "Crédits & images",
    "credits.title": "Origine des visuels et crédits",
    "credits.sources": "Les illustrations d’univers, simulations de gameplay et maquettes d’interface sont des concepts générés, conservés dans le carnet artistique d’Arkya. Elles ne sont pas des captures du jeu. Les rendus d’objets proviennent des études Blender ; les captures du prototype Unity sont identifiées séparément.",
    "credits.characters": "Les bases anatomiques Rain et Snow sont créées par <strong>Blender Studio / Blender Foundation</strong> sous <a href=\"https://creativecommons.org/licenses/by/4.0/\" target=\"_blank\" rel=\"noopener noreferrer\">CC BY 4.0</a>. Adaptations Arkya : simplification des maillages, proportions et oreilles elfiques, coiffures, vêtements fantasy, surfaces peintes et matériaux colorables. <a href=\"https://studio.blender.org/characters/rain/v2/\" target=\"_blank\" rel=\"noopener noreferrer\">Rain</a> · <a href=\"https://studio.blender.org/characters/snow/v4/\" target=\"_blank\" rel=\"noopener noreferrer\">Snow</a>. Cette réutilisation n’implique aucun soutien de leurs auteurs à Arkya.",
    "credits.registry": "Consulter le registre des sources visuelles",
    "footer.atlas": "ARKYA · ATLAS VISUEL",
    "footer.edition": "ÉDITION OCTOBRE 2026",
    "footer.top": "Revenir en haut ↑",
    "dialog.close": "Fermer la vue agrandie",
    "dialog.previous": "Création précédente",
    "dialog.next": "Création suivante",
    "dialog.original": "Ouvrir l’image ↗"
  },
  "en": {
    "meta.description": "Discover Arkya, an MMORPG in the making. Worlds, painted materials, characters and art directions: step behind the scenes of a world taking shape.",
    "meta.title": "Arkya — A world takes shape",
    "meta.ogDescription": "A visual atlas of the MMORPG Arkya. Explore its worlds, creations and art direction.",
    "page.title": "Arkya — A world takes shape",
    "nav.skip": "Skip to content",
    "nav.home": "Arkya, home",
    "nav.main": "Main navigation",
    "nav.mobile": "Mobile navigation",
    "nav.vision": "The vision",
    "nav.creations": "The creations",
    "nav.interfaces": "The interfaces",
    "nav.project": "The project",
    "nav.development": "Progress",
    "nav.classes": "Classes",
    "nav.account": "Sign in with Discord",
    "nav.play": "Play",
    "footer.terms": "Terms of Use",
    "footer.privacy": "Privacy Policy",
    "gameplay.eyebrow": "THE PATHS OF ARKYA",
    "gameplay.title": "Five classes.<br>Your <em>own path.</em>",
    "gameplay.description": "Explore the classes, multiclassing and first spells imagined for Arkya. A spellbook to understand the choices taking shape.",
    "gameplay.cta": "Explore the classes",
    "nav.atlas": "Open the atlas",
    "nav.language": "Choose language",
    "hero.eyebrow": "MMORPG · CREATION JOURNAL",
    "hero.title": "A world<br>takes <em>shape.</em>",
    "hero.description": "Lands to discover. Stories to share.<br>Step into the world taking shape in Arkya.",
    "hero.explore": "Explore the creations",
    "hero.captionNote": "Generated concept · artistic exploration",
    "hero.journey": "THE JOURNEY STARTS HERE",
    "hero.vision": "Discover the vision",
    "hero.development": "PROJECT IN DEVELOPMENT",
    "directions.eyebrow": "THE FIRST EXPLORATIONS",
    "directions.title": "Three perspectives.<br>One world to imagine.",
    "directions.description": "Change your horizon. These directions explore Arkya’s atmosphere; they do not yet define its story.",
    "directions.choose": "Choose the showcase atmosphere",
    "directions.dawnMood": "WARMTH & ADVENTURE",
    "directions.dawn": "Realms of Dawn",
    "directions.aetherMood": "MAGIC & CRAFT",
    "directions.aether": "Aether Cities",
    "directions.ancientsMood": "NATURE & MYSTERY",
    "directions.ancients": "Lands of the Ancients",
    "vision.approved": "Approved reference",
    "vision.artLabel": "TREE · PAINTED FINISH V3",
    "vision.eyebrow": "AN ART DIRECTION TAKES ROOT",
    "vision.title": "Every material<br>tells <em>a story.</em>",
    "vision.lead": "Silhouettes you recognise.<br>Surfaces that feel hand-painted.",
    "vision.detail": "Knotted bark, the soft edge of a stone, a glow within a crystal. Arkya’s identity grows from these details, with carefully shaped volumes and colours that remain clear at a distance.",
    "vision.reference": "The painted V3 tree is the approved reference for environment objects. Its visual language extends across ten approved resource families.",
    "vision.material": "Tree material",
    "vision.moss": "Ancient moss",
    "vision.mystic": "Mystic",
    "vision.burnt": "Burnt",
    "vision.resources": "approved resource models<br><small>excluding reference trees</small>",
    "vision.seeApproved": "View approved resources",
    "collection.eyebrow": "THE VISUAL ATLAS",
    "collection.title": "Every creation,<br>a piece of <em>Arkya.</em>",
    "collection.description": "From the first ideas to objects in the world.<br>Browse the families, materials and variations.",
    "collection.filter": "Filter by family",
    "category.all": "Explore everything",
    "category.world": "World",
    "category.vegetation": "Vegetation",
    "category.minerals": "Minerals",
    "category.magic": "Magic & tools",
    "category.characters": "Characters",
    "category.architecture": "Architecture",
    "category.interfaces": "Interfaces",
    "collection.search": "Search for a creation",
    "collection.placeholder": "A tree, mana, a material…",
    "collection.show": "Show",
    "filterStatus.all": "All statuses",
    "filterStatus.approved": "Approved references",
    "filterStatus.proposal": "Proposals",
    "filterStatus.exploration": "Explorations",
    "filterStatus.prototype": "Prototype screenshots",
    "collection.creations": "Arkya’s creations",
    "collection.emptyTitle": "No creations match this search.",
    "collection.emptyDescription": "Try another material or explore all families.",
    "collection.reset": "Reset filters",
    "collection.errorTitle": "The atlas could not be loaded.",
    "collection.errorDescription": "Reload the page to see the creations.",
    "collection.retry": "Try again",
    "collection.more": "See more creations",
    "characters.eyebrow": "THE FACES OF ARKYA",
    "characters.title": "The adventure begins<br>with <em>you.</em>",
    "characters.description": "Humans and Elves bring the first character studies to life. Silhouettes, hairstyles, colours and clothing form an appearance workshop. These studies continue into a character creator already integrated into Unity.",
    "characters.note": "Their art direction remains a proposal awaiting approval.",
    "characters.discover": "Discover the characters",
    "characters.alt": "3D portrait for the Lands of the Ancients direction, adapted from Rain",
    "characters.caption": "Character studies · art proposal",
    "interfaces.eyebrow": "A WINDOW ON THE WORLD",
    "interfaces.title": "Three ways<br>to experience <em>the interface.</em>",
    "interfaces.description": "Weathered copper, a travel journal or a glowing stone.<br>Three proposed looks for the same exploration HUD.",
    "interfaces.choose": "Choose an interface theme",
    "interfaces.copper": "Ink & copper",
    "interfaces.journal": "Explorer’s journal",
    "interfaces.mana": "Mana stone",
    "interfaces.note": "Generated mockup · illustrative features",
    "project.alt": "The starter island in Arkya’s Unity prototype",
    "project.caption": "UNITY SCREENSHOT · PROTOTYPE IN DEVELOPMENT",
    "project.eyebrow": "FROM DRAWING TO A PLAYABLE WORLD",
    "project.title": "The first lands<br>are <em>already here.</em>",
    "project.description": "Arkya is an MMORPG project in development. A starter island, customisable characters and the first multiplayer foundations are taking shape in Unity.",
    "project.environmentTitle": "A living environment",
    "project.environment": "Editable terrain, a day and night cycle, rain, wind and a violet ocean shape the island’s atmosphere.",
    "project.multiplayerTitle": "Multiplayer foundations",
    "project.multiplayer": "The prototypes explore movement, combat and continuity across multiple server areas.",
    "project.accountsTitle": "Account services are taking shape",
    "project.accounts": "The Discord sign-in flow and account services are developed. Deployment and public activation still need to be verified.",
    "project.updates": "See the project’s progress",
    "project.futureTitle": "An adventure still to build",
    "project.future": "The persistent world and the complete player journey still need to be connected. No release date has been announced.",
    "closing.eyebrow": "THE JOURNAL STAYS OPEN",
    "closing.title": "A world is being built.<br><em>One creation at a time.</em>",
    "closing.atlas": "Return to the atlas",
    "footer.description": "MMORPG in the making.<br>Worlds, studies and creations.",
    "footer.credits": "Credits & images",
    "credits.title": "Visual sources and credits",
    "credits.sources": "World illustrations, gameplay simulations and interface mockups are generated concepts kept in Arkya’s art journal. They are not screenshots of the game. Object renders come from Blender studies; Unity prototype screenshots are identified separately.",
    "credits.characters": "The Rain and Snow anatomical bases were created by <strong>Blender Studio / Blender Foundation</strong> under <a href=\"https://creativecommons.org/licenses/by/4.0/\" target=\"_blank\" rel=\"noopener noreferrer\">CC BY 4.0</a>. Arkya adaptations: mesh simplification, proportions and Elven ears, hairstyles, fantasy clothing, painted surfaces and tintable materials. <a href=\"https://studio.blender.org/characters/rain/v2/\" target=\"_blank\" rel=\"noopener noreferrer\">Rain</a> · <a href=\"https://studio.blender.org/characters/snow/v4/\" target=\"_blank\" rel=\"noopener noreferrer\">Snow</a>. This reuse does not imply any endorsement of Arkya by their creators.",
    "credits.registry": "View the visual source registry",
    "footer.atlas": "ARKYA · VISUAL ATLAS",
    "footer.edition": "OCTOBER 2026 EDITION",
    "footer.top": "Back to top ↑",
    "dialog.close": "Close enlarged view",
    "dialog.previous": "Previous creation",
    "dialog.next": "Next creation",
    "dialog.original": "Open image ↗"
  }
};

  const supportedLanguages = new Set(['fr', 'en']);
  const storageKey = 'arkya-language';
  let language = initialLanguage();

  function initialLanguage() {
    const requested = new URLSearchParams(location.search).get('lang');
    if (supportedLanguages.has(requested)) return requested;
    try {
      const saved = localStorage.getItem(storageKey);
      if (supportedLanguages.has(saved)) return saved;
    } catch {
      // Language switching also works when browser storage is unavailable.
    }
    return navigator.language?.toLowerCase().startsWith('en') ? 'en' : 'fr';
  }

  function applyLanguage() {
    const messages = { ...translations[language], ...window.ArkyaPageTranslations?.[language] };
    document.documentElement.lang = language;
    document.querySelectorAll('[data-i18n]').forEach((node) => {
      const value = messages[node.dataset.i18n];
      if (value !== undefined) node.textContent = value;
    });
    // Only trusted, fixed copy uses markup; interactive controls keep their DOM.
    document.querySelectorAll('[data-i18n-html]').forEach((node) => {
      const value = messages[node.dataset.i18nHtml];
      if (value !== undefined) node.innerHTML = value;
    });
    for (const attribute of ['aria-label', 'alt', 'placeholder', 'content']) {
      document.querySelectorAll(`[data-i18n-${attribute}]`).forEach((node) => {
        const value = messages[node.getAttribute(`data-i18n-${attribute}`)];
        if (value !== undefined) node.setAttribute(attribute, value);
      });
    }
    document.querySelector('meta[property="og:locale"]').content = language === 'en' ? 'en_GB' : 'fr_FR';
    document.querySelectorAll('[data-language]').forEach((button) => {
      const selected = button.dataset.language === language;
      button.classList.toggle('active', selected);
      button.setAttribute('aria-pressed', String(selected));
    });
    document.querySelectorAll('[data-language-link]').forEach((link) => {
      const target = new URL(link.getAttribute('href'), location.href);
      target.searchParams.set('lang', language);
      link.setAttribute('href', target.pathname + target.search + target.hash);
    });
  }

  function setLanguage(nextLanguage) {
    if (!supportedLanguages.has(nextLanguage)) return false;
    language = nextLanguage;
    try { localStorage.setItem(storageKey, language); } catch { /* Optional preference storage. */ }
    const url = new URL(location.href);
    url.searchParams.set('lang', language);
    history.replaceState(null, '', url);
    applyLanguage();
    window.dispatchEvent(new CustomEvent('arkya:languagechange', { detail: { language } }));
    return true;
  }

  window.ArkyaI18n = Object.freeze({ get language() { return language; }, setLanguage });
  document.querySelectorAll('[data-language]').forEach((button) => {
    button.addEventListener('click', () => setLanguage(button.dataset.language));
  });
  applyLanguage();
})();
