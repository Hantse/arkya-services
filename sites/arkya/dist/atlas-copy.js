(() => {
  'use strict';

  const copy = {
    fr: {
      'page.title': 'Arkya — Atlas visuel',
      'meta.title': 'Arkya — Atlas visuel',
      'meta.description': 'Explorez l’atlas visuel d’Arkya : 205 créations, matières peintes, personnages et recherches artistiques du MMORPG en développement.',
      'meta.ogDescription': 'Les créations et la direction artistique d’Arkya : univers, personnages, ressources et interfaces.',
    },
    en: {
      'page.title': 'Arkya — Visual atlas',
      'meta.title': 'Arkya — Visual atlas',
      'meta.description': 'Explore Arkya’s visual atlas: 205 creations, painted materials, characters and artistic studies for the MMORPG in development.',
      'meta.ogDescription': 'Arkya’s creations and art direction: worlds, characters, resources and interfaces.',
    },
  };

  for (const language of ['fr', 'en']) {
    window.ArkyaPageTranslations[language] = { ...window.ArkyaPageTranslations[language], ...copy[language] };
  }
})();
