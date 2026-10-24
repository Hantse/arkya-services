// Public, bilingual reference. Keep thresholds aligned with the Unity catalogs.
export const reviewedAt = '2026-10-24';

const copy = (fr, en = fr) => ({ fr, en });
const section = (frTitle, enTitle, frText, enText, list) => ({
  title: copy(frTitle, enTitle), text: copy(frText, enText), ...(list ? { list } : {}),
});
const fact = (frLabel, enLabel, frValue, enValue = frValue) => ({
  label: copy(frLabel, enLabel), value: copy(frValue, enValue),
});

export const categories = [
  { id: 'start', title: copy('Bien débuter', 'Getting started'), description: copy('Commandes, progression et premiers repères.', 'Controls, progression and your first steps.'), mark: '⌁' },
  { id: 'classes', title: copy('Classes', 'Classes'), description: copy('Les cinq voies de combat et leur identité.', 'The five combat paths and their identities.'), mark: '⚔' },
  { id: 'abilities', title: copy('Capacités', 'Abilities'), description: copy('Rangs, héritage et capacités de chaque classe.', 'Ranks, inheritance and each class’s abilities.'), mark: '✧' },
  { id: 'crafting', title: copy('Métiers', 'Professions'), description: copy('Écoles, maîtres et compétences artisanales.', 'Schools, trainers and crafting skills.'), mark: '⚒' },
  { id: 'recipes', title: copy('Recettes', 'Recipes'), description: copy('Ingrédients, ateliers et résultats de fabrication.', 'Ingredients, stations and crafting results.'), mark: '▱' },
  { id: 'resources', title: copy('Ressources', 'Resources'), description: copy('Où récolter et comment utiliser les matériaux.', 'Gathering and using materials.'), mark: '◇' },
  { id: 'world', title: copy('Monde', 'World'), description: copy('L’Île de l’Aube et les lieux à découvrir.', 'Dawn Island and places to explore.'), mark: '◎' },
];

const classSource = 'docs/CHARACTER_STATS_SKILLS.md';
const abilitySource = 'docs/GAMEPLAY_STARTER_SKILLS.md';
const craftSource = 'docs/CRAFTING.md';
const harvestSource = 'docs/HARVESTING.md';

const classes = [
  { id: 'warrior', name: 'Warrior', role: copy('Combat au contact et protection', 'Melee combat and protection'), description: copy('Le Warrior tient le front grâce à ses armes, sa résistance et ses outils de contrôle.', 'Warrior holds the front line through weapons, resilience and control.'), skills: ['powerful-strike', 'guard', 'taunt', 'hindering-strike', 'charge'] },
  { id: 'scout', name: 'Scout', role: copy('Précision et mobilité', 'Precision and mobility'), description: copy('Le Scout prépare ses tirs, choisit sa position et se déplace pour conserver l’avantage.', 'Scout prepares shots, chooses a position and moves to keep the advantage.'), skills: ['precise-shot', 'light-step', 'tracking-mark', 'hindering-shot', 'camouflage'] },
  { id: 'mage', name: 'Mage', role: copy('Magie offensive et contrôle', 'Offensive magic and control'), description: copy('Le Mage associe projectiles magiques, attaques de zone et contrôle des distances.', 'Mage combines magical projectiles, area attacks and control of distance.'), skills: ['arcane-bolt', 'arcane-barrier', 'flame-wave', 'frost-shard', 'short-teleport'] },
  { id: 'cleric', name: 'Cleric', role: copy('Soins directs et protection', 'Direct healing and protection'), description: copy('Le Cleric restaure la vie et protège ses alliés avec des soins directs et des boucliers.', 'Cleric restores health and protects allies with direct healing and shields.'), skills: ['heal', 'holy-bolt', 'holy-aegis', 'dispel', 'group-prayer'] },
  { id: 'druid', name: 'Druid', role: copy('Nature et soins dans la durée', 'Nature and healing over time'), description: copy('Le Druid anticipe les dégâts, soutient ses alliés dans la durée et entrave les déplacements ennemis.', 'Druid anticipates damage, sustains allies over time and hinders enemy movement.'), skills: ['bramble', 'renewal', 'entangling-roots', 'barkskin', 'bloom'] },
];

// id, class, French/English names, R1/R2 levels, French/English design effects.
const abilities = [
  ['powerful-strike', 'warrior', 'Frappe puissante', 'Powerful Strike', 1, 5, 'Coup renforcé avec une arme de mêlée sur la cible devant soi.', 'A stronger melee weapon strike against the target in front of you.'],
  ['guard', 'warrior', 'Garde', 'Guard', 1, 5, 'Réduit les dégâts physiques venant de face. Demande une arme de mêlée ; attaquer ou lancer un sort termine la garde.', 'Reduces physical damage from the front. Requires a melee weapon; attacking or casting ends the guard.'],
  ['taunt', 'warrior', 'Provocation', 'Taunt', 3, null, 'Force temporairement un ennemi non joueur à se concentrer sur le Warrior. Ne contrôle pas les actions des autres joueurs.', 'Temporarily forces a non-player enemy to focus on Warrior. Does not control other players’ actions.'],
  ['hindering-strike', 'warrior', 'Coup entravant', 'Hindering Strike', 6, 10, 'Frappe de mêlée qui ralentit la cible.', 'A melee strike that slows the target.'],
  ['charge', 'warrior', 'Charge', 'Charge', 10, null, 'Rejoint rapidement une cible par un trajet praticable, sans traverser les obstacles, libérer d’une immobilisation ou ajouter d’étourdissement.', 'Quickly reaches a target along a traversable route, without crossing obstacles, breaking a root or adding a stun.'],
  ['precise-shot', 'scout', 'Tir précis', 'Precise Shot', 1, 5, 'Tir préparé sur une cible devant soi. Demande de rester immobile et une arme à distance compatible.', 'A prepared shot at a target in front of you. Requires standing still and a compatible ranged weapon.'],
  ['light-step', 'scout', 'Pas léger', 'Light Step', 1, 5, 'Accélération personnelle temporaire, utilisable en combat, sans invulnérabilité ni suppression d’entrave.', 'A temporary personal speed boost usable in combat, without invulnerability or removal of movement hindrances.'],
  ['tracking-mark', 'scout', 'Marque de traque', 'Tracking Mark', 3, null, 'Le prochain Tir précis du Scout qui a posé la marque reçoit un bonus et la consomme. Les alliés ne reçoivent pas ce bonus.', 'The next Precise Shot from the Scout who applied the mark gains a bonus and consumes it. Allies do not receive the bonus.'],
  ['hindering-shot', 'scout', 'Tir entravant', 'Hindering Shot', 6, 10, 'Tir qui ralentit une cible devant soi. Demande une arme à distance compatible.', 'A shot that slows a target in front of you. Requires a compatible ranged weapon.'],
  ['camouflage', 'scout', 'Camouflage', 'Camouflage', 10, null, 'Permet de se dissimuler hors combat. Une attaque, un soin ou des dégâts reçus rompent l’effet. Les règles de détection restent à définir.', 'Allows hiding outside combat. Attacking, healing or taking damage breaks the effect. Detection rules remain to be defined.'],
  ['arcane-bolt', 'mage', 'Projectile arcanique', 'Arcane Bolt', 1, 5, 'Incantation infligeant des dégâts magiques directs à la cible devant soi.', 'A cast dealing direct magical damage to the target in front of you.'],
  ['arcane-barrier', 'mage', 'Barrière arcanique', 'Arcane Barrier', 1, 5, 'Bouclier personnel absorbant une quantité limitée de dégâts magiques ; ne protège pas des dégâts physiques.', 'A personal shield absorbing a limited amount of magical damage; does not protect against physical damage.'],
  ['flame-wave', 'mage', 'Vague de flammes', 'Flame Wave', 3, null, 'Incantation touchant les ennemis dans un cône devant le Mage.', 'A cast hitting enemies in a cone in front of Mage.'],
  ['frost-shard', 'mage', 'Éclat de givre', 'Frost Shard', 6, 10, 'Impact magique qui frappe et ralentit une cible devant soi.', 'A magical impact that strikes and slows a target in front of you.'],
  ['short-teleport', 'mage', 'Téléportation courte', 'Short Teleport', 10, null, 'Repositionnement dans la direction du personnage vers une destination valide, sans traverser d’obstacle. Inutilisable pendant une immobilisation ; ne donne pas d’invulnérabilité.', 'Repositions you in the direction you face to a valid destination without crossing obstacles. Unusable while rooted; grants no invulnerability.'],
  ['heal', 'cleric', 'Soin / Heal', 'Heal', 1, 5, 'Incantation restaurant directement la vie d’un allié ou de soi-même. Le soin hérité conserve son incantation.', 'A cast directly restoring an ally’s health or your own. Inherited Heal keeps its cast time.'],
  ['holy-bolt', 'cleric', 'Trait sacré', 'Holy Bolt', 1, 5, 'Incantation offensive frappant une cible ennemie devant soi.', 'An offensive cast striking an enemy in front of you.'],
  ['holy-aegis', 'cleric', 'Égide sacrée', 'Holy Aegis', 3, null, 'Bouclier absorbant des dégâts physiques ou magiques sur un allié ou soi-même.', 'A shield absorbing physical or magical damage on an ally or yourself.'],
  ['dispel', 'cleric', 'Dissipation', 'Dispel', 6, 10, 'Retire une malédiction dissipable sur un allié ou soi-même, sans effet sur les poisons ou blessures physiques. R2 améliore le coût, pas les catégories dissipables.', 'Removes a dispellable curse from an ally or yourself, with no effect on poison or physical wounds. R2 improves cost, not the dispellable categories.'],
  ['group-prayer', 'cleric', 'Prière de groupe', 'Group Prayer', 10, null, 'Canalisation soignant progressivement les membres du groupe proches du Cleric. Empêche de lancer un autre sort simultanément.', 'A channeled prayer gradually healing nearby group members. Prevents casting another spell at the same time.'],
  ['bramble', 'druid', 'Ronces', 'Bramble', 1, 5, 'Impact naturel puis dégâts progressifs sur une cible ennemie devant soi. Ces dégâts rompent aussi Enracinement.', 'A nature impact followed by damage over time against an enemy in front of you. This damage also breaks Entangling Roots.'],
  ['renewal', 'druid', 'Renouveau', 'Renewal', 1, 5, 'Soin progressif sur un allié ou soi-même, sans soin immédiat au lancement. Son héritage ne donne pas Floraison.', 'Healing over time on an ally or yourself, without immediate healing when applied. Inheritance does not grant Bloom.'],
  ['entangling-roots', 'druid', 'Enracinement', 'Entangling Roots', 3, null, 'Immobilise une cible ennemie. De nouveaux dégâts, y compris progressifs, rompent l’effet.', 'Roots an enemy in place. Further damage, including damage over time, breaks the effect.'],
  ['barkskin', 'druid', 'Peau d’écorce', 'Barkskin', 6, 10, 'Réduit temporairement les dégâts physiques subis par le lanceur tout en lui permettant de continuer à agir.', 'Temporarily reduces physical damage taken by the caster while allowing them to keep acting.'],
  ['bloom', 'druid', 'Floraison', 'Bloom', 10, null, 'Termine les Renouveaux du lanceur sur les membres du groupe proches et convertit leurs soins restants en soins immédiats, sans les dupliquer.', 'Ends the caster’s Renewals on nearby group members and converts their remaining healing into immediate healing without duplicating it.'],
];

const classEntries = classes.map(value => ({
  id: value.id, category: 'classes', title: copy(value.name), summary: value.description, status: 'prototype',
  sections: [
    section('Identité de la classe', 'Class identity', value.description.fr, value.description.en),
    section('Progression', 'Progression', 'Chaque classe possède ses niveaux et son XP. Une seule classe est active à la fois. Les seuils du catalogue actuel vont du niveau 1 au niveau 10.', 'Each class has its own levels and XP. Only one class is active at a time. The current catalog covers levels 1 to 10.'),
    section('État actuel', 'Current state', 'La classe, les niveaux et les rangs font partie du profil et de la fiche personnage. Les capacités de combat ci-dessous décrivent le catalogue prévu ; leur lancement et leurs effets restent à intégrer.', 'The class, levels and ranks are part of the profile and character sheet. The combat abilities below describe the intended catalog; casting and combat effects remain to be integrated.'),
  ],
  facts: [fact('Rôle prévu', 'Intended role', value.role.fr, value.role.en), fact('Capacités au catalogue', 'Catalog abilities', '5')],
  related: [...value.skills, 'progression', 'inheritance'], source: classSource,
}));

const abilityEntries = abilities.map(([id, origin, fr, en, rank1, rank2, frEffect, enEffect]) => {
  const originName = classes.find(value => value.id === origin).name;
  return {
    id, category: 'abilities', title: copy(fr, en), summary: copy(frEffect, enEffect), status: 'design',
    sections: [
      section('Effet prévu', 'Intended effect', frEffect, enEffect),
      section('Apprentissage et héritage', 'Learning and inheritance',
        rank2 ? `R1 est accessible au niveau ${rank1} de ${originName}, puis R2 au niveau ${rank2}. Apprendre R2 rend R1 disponible dans les autres classes. R2 reste réservé à ${originName}.` : `R1 est accessible au niveau ${rank1} de ${originName}. Cette capacité reste réservée à sa classe d’origine. Aucun R2 n’est défini.`,
        rank2 ? `R1 is available at ${originName} level ${rank1}, then R2 at level ${rank2}. Learning R2 makes R1 available to other classes. R2 remains exclusive to ${originName}.` : `R1 is available at ${originName} level ${rank1}. This ability remains exclusive to its original class. No R2 is defined.`),
      section('État actuel', 'Current state', 'Les noms, seuils et règles d’héritage sont intégrés au catalogue et à la fiche. Le lancement, les effets de combat et le choix des capacités équipées restent à développer ; coûts et délais ne sont pas définitifs.', 'Names, thresholds and inheritance rules are integrated into the catalog and character sheet. Casting, combat effects and equipped ability choices remain to be developed; costs and cooldowns are not final.'),
    ],
    facts: [fact('Classe', 'Class', originName), fact('Rang 1', 'Rank 1', `Niveau ${rank1}`, `Level ${rank1}`), fact('Rang 2', 'Rank 2', rank2 ? `Niveau ${rank2}` : 'Non défini', rank2 ? `Level ${rank2}` : 'Not defined'), fact('Héritage', 'Inheritance', rank2 ? `R1 après R2 · niveau ${rank2}` : 'Réservé à la classe', rank2 ? `R1 after R2 · level ${rank2}` : 'Class exclusive')],
    related: [origin, 'inheritance', 'progression'], source: abilitySource,
  };
});

const schools = [
  { id: 'miner', title: copy('Mineur', 'Miner'), summary: copy('Récolter pierre et minerais, puis tailler les premières briques.', 'Gather stone and ores, then cut your first bricks.'), skills: ['mining', 'quarrying', 'stonecrafting', 'smelting'], tools: copy('Pioche de mineur et pic de carrier', 'Mining pickaxe and quarry pick'), related: ['gathering', 'stone-brick', 'sandstone-brick'] },
  { id: 'blacksmith', title: copy('Forgeron', 'Blacksmith'), summary: copy('Une école dédiée aux armes et aux armures ; ses recettes restent à définir.', 'A school dedicated to weapons and armor; its recipes remain to be defined.'), skills: ['armorcraft', 'weaponsmith'], tools: copy('Marteau de forge', 'Smithing hammer'), related: ['iron-ore', 'copper-ore'] },
  { id: 'logger', title: copy('Bûcheron', 'Logger'), summary: copy('Récolter le bois et faire progresser les compétences de cette école.', 'Gather wood and advance this school’s skills.'), skills: ['logging', 'woodworking'], tools: copy('Hache de bûcheron', 'Logging axe'), related: ['wood', 'carpenter', 'gathering'] },
  { id: 'carpenter', title: copy('Charpentier', 'Carpenter'), summary: copy('Une école qui se débloque au niveau 10 de Bûcheron. Ses compétences et recettes restent à définir.', 'A school unlocked at Logger level 10. Its skills and recipes remain to be defined.'), skills: [], tools: null, related: ['logger', 'wood'] },
];

const schoolEntries = schools.map(value => ({
  id: value.id, category: 'crafting', title: value.title, summary: value.summary, status: 'prototype',
  sections: [
    section('Accès à l’école', 'School access', value.id === 'carpenter' ? 'Atteindre Bûcheron niveau 10 débloque automatiquement Charpentier au niveau 1. Aucun maître charpentier ni formule de charpenterie n’est encore ajouté.' : 'L’école est accessible au niveau 1, sans prérequis. Approchez le maître correspondant à Aubefont et appuyez sur E pour choisir cette école.', value.id === 'carpenter' ? 'Reaching Logger level 10 automatically unlocks Carpenter at level 1. No Carpenter trainer or carpentry formula has been added yet.' : 'The school is available at level 1 with no prerequisite. Approach its trainer in Aubefont and press E to select this school.'),
    section('Gagner de l’expérience', 'Earning experience', 'Une seule école est active à la fois. Chaque école conserve ses niveaux et son XP quand vous en changez. Les compétences progressent ensemble avec le niveau de leur école.', 'Only one school is active at a time. Each school keeps its levels and XP when you switch. Its skills advance together with the school’s level.'),
    ...(value.tools ? [section('Outils de départ', 'Starter tools', `Le maître fournit gratuitement une seule dotation : ${value.tools.fr.toLocaleLowerCase('fr')}. Il faut assez de place et de capacité de portage pour recevoir l’ensemble.`, `The trainer grants one free starter kit: ${value.tools.en.toLowerCase()}. You need enough bag space and carrying capacity for the whole kit.`)] : []),
    ...(value.id === 'miner' ? [section('Formules à apprendre', 'Formulas to learn', 'Le maître mineur enseigne séparément Brique de pierre et Brique de grès. Un nouveau personnage ne connaît pas ces formules.', 'The Miner trainer teaches Stone Brick and Sandstone Brick separately. A new character does not know these formulas.')] : []),
  ],
  facts: [fact('Prérequis', 'Prerequisite', value.id === 'carpenter' ? 'Bûcheron niveau 10' : 'Aucun', value.id === 'carpenter' ? 'Logger level 10' : 'None'), fact('Niveau maximal actuel', 'Current maximum level', '100')],
  related: [...value.skills, ...value.related, 'crafting-progression'], source: craftSource,
}));

const craftSkills = [
  ['mining', 'miner', 'Mining', 'Extraction minière', 'Ore mining', 'La compétence appartient au Mineur et suit le niveau de cette école. Récolter du fer ou du cuivre donne de l’XP uniquement si Mineur est actif.', 'This skill belongs to Miner and follows the school’s level. Gathering iron or copper grants XP only while Miner is active.'],
  ['quarrying', 'miner', 'Quarrying', 'Extraction de pierre', 'Quarrying stone', 'La compétence appartient au Mineur. Pierre et grès alimentent les premières formules du tailleur de pierre.', 'This skill belongs to Miner. Stone and sandstone supply the first stone cutter formulas.'],
  ['stonecrafting', 'miner', 'Stonecrafting', 'Taille de pierre', 'Stonecrafting', 'Cette compétence réduit les matériaux nécessaires aux briques : 4 à 0, 3 à 100 et 2 à 200. Une fabrication réussie donne 10 XP lorsque Mineur est actif.', 'This skill reduces materials needed for bricks: 4 at 0, 3 at 100 and 2 at 200. Successful crafting grants 10 XP while Miner is active.'],
  ['smelting', 'miner', 'Smelting', 'Fonte', 'Smelting', 'La compétence existe dans l’école Mineur et progresse avec elle. Les recettes de fonte ne sont pas encore définies.', 'This skill exists in the Miner school and advances with it. Smelting recipes are not defined yet.'],
  ['armorcraft', 'blacksmith', 'Armorcraft', 'Fabrication d’armures', 'Armor crafting', 'La compétence existe dans l’école Forgeron et progresse avec elle. Les recettes d’armures ne sont pas encore définies.', 'This skill exists in the Blacksmith school and advances with it. Armor recipes are not defined yet.'],
  ['weaponsmith', 'blacksmith', 'Weaponsmith', 'Fabrication d’armes', 'Weapon crafting', 'La compétence existe dans l’école Forgeron et progresse avec elle. Les recettes d’armes ne sont pas encore définies.', 'This skill exists in the Blacksmith school and advances with it. Weapon recipes are not defined yet.'],
  ['logging', 'logger', 'Logging', 'Bûcheronnage', 'Logging', 'Terminer la récolte d’un arbre et recevoir le bois donne 5 XP lorsque Bûcheron est l’école active. Les coups intermédiaires ne donnent pas d’XP.', 'Finishing a tree and receiving its wood grants 5 XP while Logger is the active school. Intermediate strikes grant no XP.'],
  ['woodworking', 'logger', 'Woodworking', 'Travail du bois', 'Woodworking', 'La compétence existe dans l’école Bûcheron et progresse avec elle. Les formules de travail du bois restent à définir.', 'This skill exists in the Logger school and advances with it. Woodworking formulas remain to be defined.'],
];

const craftSkillEntries = craftSkills.map(([id, school, name, frTopic, enTopic, frText, enText]) => ({
  id, category: 'crafting', title: copy(name), summary: copy(frText, enText), status: 'prototype',
  sections: [section(frTopic, enTopic, frText, enText), section('Progression de la compétence', 'Skill progression', 'La valeur vaut 5 × (niveau de l’école − 1). Elle commence à 0 ; niveau 21 donne 100, niveau 41 donne 200. Il n’y a pas d’XP ni d’achat de rang séparé pour chaque compétence.', 'The value is 5 × (school level − 1). It starts at 0; level 21 gives 100 and level 41 gives 200. Each skill has no separate XP counter or purchased ranks.')],
  related: [school, 'crafting-progression', ...(id === 'stonecrafting' ? ['stone-brick', 'sandstone-brick'] : [])], source: craftSource,
}));

const recipeEntries = [
  { id: 'stone-brick', title: copy('Brique de pierre', 'Stone Brick'), resource: 'stone', material: copy('pierre', 'stone'), item: 'resource:stone-brick' },
  { id: 'sandstone-brick', title: copy('Brique de grès', 'Sandstone Brick'), resource: 'sandstone', material: copy('grès', 'sandstone'), item: 'resource:sandstone-brick' },
].map(value => ({
  id: value.id, category: 'recipes', title: value.title,
  summary: copy(`Transformer ${value.resource === 'stone' ? 'de la pierre' : 'du grès'} en une brique au tailleur de pierre. Coût : de 4 à 2 matériaux selon Stonecrafting.`, `Turn ${value.material.en} into one brick at the stone cutter. Cost: 4 to 2 materials depending on Stonecrafting.`), status: 'prototype',
  sections: [
    section('Apprendre la formule', 'Learn the formula', 'Parlez au maître mineur à Aubefont avec E et apprenez cette formule. Elle rejoint une seule fois le grimoire Formules, accessible avec L.', 'Speak to the Miner trainer in Aubefont with E and learn this formula. It appears once in the Formulas grimoire, opened with L.'),
    section('Fabriquer', 'Crafting', `Approchez du tailleur de pierre, puis appuyez sur V. Chaque clic réussi consomme les matériaux et produit 1 ${value.title.fr.toLocaleLowerCase('fr')}. Il faut être vivant, à moins de 3 mètres de la station et disposer des matériaux, de place et de capacité de portage.`, `Approach the stone cutter and press V. Each successful click consumes the materials and produces 1 ${value.title.en.toLowerCase()}. You must be alive, within 3 metres of the station, and have the materials, bag space and carrying capacity.`),
    section('Coût et expérience', 'Cost and experience', 'Le coût diminue de 4 matériaux à Stonecrafting 0 à 3 à 100, puis 2 à 200 et au-delà. Une fabrication donne 10 XP si Mineur est actif. La formule apprise reste utilisable dans une autre école, sans gain d’XP.', 'The cost falls from 4 materials at Stonecrafting 0 to 3 at 100, then 2 at 200 and above. A craft grants 10 XP if Miner is active. A learned formula remains usable in another school without granting XP.'),
  ],
  facts: [fact('Station', 'Station', 'Tailleur de pierre', 'Stone cutter'), fact('Prérequis', 'Requirements', 'Mineur 1 · Stonecrafting 0 · formule apprise', 'Miner 1 · Stonecrafting 0 · learned formula'), fact('Matériaux à 0 / 100 / 200', 'Materials at 0 / 100 / 200', '4 / 3 / 2'), fact('Quantité produite', 'Output', '1'), fact('Poids de la brique', 'Brick weight', '2 kg'), fact('XP', 'XP', '10 · Mineur actif', '10 · Miner active')],
  related: ['miner', 'stonecrafting', value.resource, 'crafting-progression'], source: craftSource,
}));

const resources = [
  { id: 'wood', title: copy('Bois', 'Wood'), school: 'logger', origin: copy('Arbres', 'Trees'), hits: 4, quantity: 8, weight: '500 g', respawn: '120 s', related: ['logging', 'woodworking'], use: copy('Le bois se récolte sur les arbres. Les formules de travail du bois restent à définir.', 'Wood is gathered from trees. Woodworking formulas remain to be defined.') },
  { id: 'stone', title: copy('Pierre', 'Stone'), school: 'miner', origin: copy('Rochers', 'Rocks'), hits: 5, quantity: 6, weight: '1 kg', respawn: '180 s', related: ['stone-brick', 'quarrying'], use: copy('La pierre sert à la formule Brique de pierre, apprise auprès du maître mineur.', 'Stone is used in the Stone Brick formula, learned from the Miner trainer.') },
  { id: 'sandstone', title: copy('Grès', 'Sandstone'), school: 'miner', origin: copy('Rochers à teinte sable', 'Sand-colored rocks'), hits: 5, quantity: 6, weight: '1 kg', respawn: '180 s', related: ['sandstone-brick', 'quarrying'], use: copy('Le grès sert à la formule Brique de grès et reste distinct de la pierre dans les sacs.', 'Sandstone is used in the Sandstone Brick formula and remains separate from stone in your bags.') },
  { id: 'iron-ore', title: copy('Minerai de fer', 'Iron Ore'), school: 'miner', origin: copy('Rochers aux teintes froides', 'Cool-toned rocks'), hits: 8, quantity: 3, weight: '750 g', respawn: '180 s', related: ['mining', 'smelting', 'blacksmith'], use: copy('Le fer est une ressource distincte du cuivre. Les formules de fonte et de forge restent à définir.', 'Iron is a separate resource from copper. Smelting and smithing formulas remain to be defined.') },
  { id: 'copper-ore', title: copy('Minerai de cuivre', 'Copper Ore'), school: 'miner', origin: copy('Rochers aux teintes cuivrées', 'Copper-colored rocks'), hits: 6, quantity: 4, weight: '750 g', respawn: '180 s', related: ['mining', 'smelting', 'blacksmith'], use: copy('Le cuivre est une ressource distincte du fer. Les formules de fonte et de forge restent à définir.', 'Copper is a separate resource from iron. Smelting and smithing formulas remain to be defined.') },
];

const resourceEntries = resources.map(value => ({
  id: value.id, category: 'resources', title: value.title, summary: value.use, status: 'prototype',
  sections: [
    section('Récolter', 'Gathering', 'Approchez de la ressource et maintenez G. Le dernier coup attribue toute la récolte au joueur qui le donne. Il doit pouvoir recevoir la totalité dans ses sacs.', 'Approach the resource and hold G. The final strike awards the whole harvest to the player who makes it. Their bags must be able to receive the entire yield.'),
    section('Utilisation', 'Uses', value.use.fr, value.use.en),
    section('Quantités de référence', 'Base quantities', 'Les valeurs ci-dessous correspondent à l’outil de départ, avant bonus d’outil, cristaux et taux du royaume. Les piles contiennent au maximum 100 unités.', 'The figures below use a starter tool, before tool bonuses, crystals and realm rates. Stacks hold up to 100 units.'),
  ],
  facts: [fact('Source', 'Source', value.origin.fr, value.origin.en), fact('Coups avec outil de départ', 'Starter tool strikes', String(value.hits)), fact('Quantité de base', 'Base yield', String(value.quantity)), fact('Poids unitaire', 'Unit weight', value.weight), fact('Retour après épuisement', 'Respawn after depletion', value.respawn), fact('XP de récolte', 'Gathering XP', '5 · école correspondante active', '5 · matching school active')],
  related: [value.school, 'gathering', ...value.related], source: harvestSource,
}));

const guides = [
  {
    id: 'skills-and-abilities', category: 'start', title: copy('Skills et abilities', 'Skills and abilities'), summary: copy('Un skill est une compétence comme Nature. Une ability est une capacité individuelle, comme le sort Thunder.', 'A skill is a proficiency such as Nature. An ability is an individual action, such as the Thunder spell.'), status: 'design',
    sections: [
      section('Skill : Nature', 'Skill: Nature', 'Nature est un exemple de skill. Les règles de progression et les effets de ce skill restent à définir.', 'Nature is an example of a skill. Its progression rules and effects remain to be defined.'),
      section('Ability : Thunder', 'Ability: Thunder', 'Thunder est un sort : c’est une ability. Les 25 capacités de classe de ce wiki sont également des abilities. Les rangs et l’héritage décrits dans leur catalogue concernent ces capacités.', 'Thunder is a spell: it is an ability. The 25 class abilities in this wiki are also abilities. Ranks and inheritance in their catalog apply to those individual abilities.'),
      section('État de la conception', 'Design status', 'Ces exemples fixent le vocabulaire. Le lien éventuel entre Nature et Thunder, les conditions d’apprentissage et les caractéristiques de Thunder restent à préciser.', 'These examples establish the terminology. Any relationship between Nature and Thunder, learning requirements and Thunder’s spell details remain to be defined.'),
    ], related: ['progression', 'inheritance'], source: 'docs/GAMEPLAY_CLASSES.md',
  },
  {
    id: 'first-steps', category: 'start', title: copy('Vos premiers pas', 'Your first steps'), summary: copy('Accès à la bêta, déplacement et fenêtres utiles pour commencer.', 'Beta access, movement and useful windows to get started.'), status: 'prototype',
    sections: [
      section('Rejoindre Arkya', 'Joining Arkya', 'Connectez votre compte Discord sur le site et consultez votre accès bêta. Le launcher Windows est disponible depuis Installer le launcher. L’admission en jeu dépend d’un accès accordé et d’un realm compatible.', 'Connect your Discord account on the website and check your beta access. The Windows launcher is available from Install the launcher. Entering the game requires granted access and a compatible realm.'),
      section('Se déplacer', 'Movement', 'ZQSD, WASD ou les flèches font courir. Maintenez Maj pour marcher. Espace fait sauter au sol ; en nage, Espace remonte et Ctrl fait plonger. Maintenez le clic gauche pour regarder autour du personnage, ou le clic droit pour orienter caméra et déplacement. La molette ajuste la distance.', 'Use ZQSD, WASD or the arrow keys to run. Hold Shift to walk. Space jumps on land; while swimming, Space ascends and Ctrl dives. Hold the left mouse button to look around your character, or the right button to turn the camera and movement direction. The wheel adjusts distance.'),
      section('Ouvrir les bonnes fenêtres', 'Useful windows', 'Les fenêtres regroupent les informations et actions disponibles dans le prototype.', 'Windows group the information and actions available in the prototype.', [copy('C : fiche personnage et capacités.', 'C: character sheet and abilities.'), copy('K : écoles d’artisanat.', 'K: crafting schools.'), copy('L : formules apprises.', 'L: learned formulas.'), copy('I : sacs · M : carte · J : journal.', 'I: bags · M: map · J: journal.'), copy('E : parler à un PNJ proche · G maintenu : récolter.', 'E: speak to a nearby NPC · hold G: gather.'), copy('V près du tailleur de pierre : recettes de la machine.', 'V near the stone cutter: station recipes.'), copy('Tab : sélectionner un ennemi proche · F ou 1 : attaque au contact.', 'Tab: select a nearby enemy · F or 1: melee attack.'), copy('Échap : fermer la fenêtre active ou ouvrir le menu.', 'Escape: close the active window or open the menu.')]),
      section('Commencer un métier', 'Start a profession', 'Votre profil artisanal commence en Mineur. À Aubefont, un maître vous permet de choisir son école, recevoir ses outils de départ et apprendre ses formules disponibles. Choisissez Bûcheron pour gagner de l’XP en récoltant du bois.', 'Your crafting profile starts as Miner. In Aubefont, a trainer lets you choose their school, receive starter tools and learn available formulas. Choose Logger to earn XP from gathering wood.'),
    ], related: ['starter-island', 'encounters', 'gathering', 'miner', 'progression'], source: 'docs/CRAFTING.md',
  },
  {
    id: 'progression', category: 'start', title: copy('Niveaux et rating', 'Levels and rating'), summary: copy('Comprendre les niveaux de classe, le rating et leur séparation avec les métiers.', 'Understand class levels, rating and their separation from professions.'), status: 'prototype',
    sections: [
      section('Une progression par classe', 'Progression for each class', 'Warrior, Scout, Mage, Cleric et Druid possèdent chacun leurs niveaux et leur XP. Une seule classe est active. Le profil initial commence en Warrior avec ses deux rangs 1. Il n’y a pas encore de choix de classe à la création.', 'Warrior, Scout, Mage, Cleric and Druid each have their own levels and XP. Only one class is active. The initial profile starts as Warrior with its two rank 1 abilities. Class selection at character creation is not available yet.'),
      section('Calculer le rating', 'Calculating rating', 'Rating = somme des niveaux des classes débloquées ÷ nombre de classes débloquées. Warrior 10 et Cleric 5 donnent un rating de 7,5. Une classe verrouillée ne compte pas ; en débloquer une de bas niveau peut faire baisser la moyenne.', 'Rating = sum of unlocked class levels ÷ number of unlocked classes. Warrior 10 and Cleric 5 give a rating of 7.5. Locked classes do not count; unlocking a low-level class can lower the average.'),
      section('Gagner de l’expérience', 'Earning experience', 'Les rencontres de l’île donnent 20 XP à la classe active au joueur qui porte le dernier coup à un ennemi. Les objectifs de camp suivent les éliminations, sans bonus de complétion. La récolte et la fabrication font progresser les métiers, sans modifier le rating.', 'Island encounters grant 20 XP to the active class of the player who lands the final blow on an enemy. Camp objectives track kills without a completion bonus. Gathering and crafting advance professions without changing rating.'),
      section('État des capacités', 'Ability status', 'Le prototype conserve les niveaux, XP et rangs et les affiche dans la fiche. Le lancement des 25 capacités de classe, le choix des capacités équipées et le changement de classe depuis cette fiche restent à intégrer.', 'The prototype stores levels, XP and ranks and displays them in the character sheet. Casting the 25 class abilities, choosing ability loadouts and changing class from this sheet remain to be integrated.'),
    ], facts: [fact('Plafond de classe du prototype', 'Prototype class cap', '10'), fact('XP au niveau suivant', 'XP to the next level', '100 × niveau courant', '100 × current level')], related: ['inheritance', 'warrior', 'encounters', 'crafting-progression'], source: classSource,
  },
  {
    id: 'inheritance', category: 'abilities', title: copy('Héritage des capacités', 'Ability inheritance'), summary: copy('Apprendre R2 dans une classe rend R1 disponible ailleurs pour les capacités transmissibles.', 'Learning R2 in a class makes R1 available elsewhere for inheritable abilities.'), status: 'prototype',
    sections: [
      section('Un rang appris reste acquis', 'A learned rank stays learned', 'Pour les capacités transmissibles du catalogue, apprendre R2 dans la classe d’origine rend R1 disponible dans les autres classes. Atteindre le niveau requis autorise l’apprentissage ; le rang doit ensuite être appris explicitement.', 'For inheritable catalog abilities, learning R2 in the original class makes R1 available to other classes. Reaching the required level permits learning; the rank must then be learned explicitly.'),
      section('Exemple : Soin', 'Example: Heal', 'Cleric niveau 1 peut apprendre Soin R1. Au niveau 5, apprendre Soin R2 libère Soin R1 pour les autres classes. Un Warrior peut ainsi avoir accès à Soin R1, sans obtenir Soin R2.', 'Cleric level 1 can learn Heal R1. At level 5, learning Heal R2 unlocks Heal R1 for other classes. A Warrior can therefore access Heal R1 without gaining Heal R2.'),
      section('Capacités réservées', 'Exclusive abilities', 'Chaque classe garde ses capacités réservées. Le catalogue comprend 25 capacités et 40 rangs. L’équipement des capacités, leur lancement et le nombre d’emplacements hérités restent à intégrer ou à définir.', 'Each class keeps its exclusive abilities. The catalog contains 25 abilities and 40 ranks. Ability loadouts, casting and the number of inherited slots remain to be integrated or defined.'),
    ], related: ['heal', 'cleric', 'warrior', 'progression', 'skills-and-abilities'], source: classSource,
  },
  {
    id: 'crafting-progression', category: 'crafting', title: copy('Apprendre un métier', 'Learning a profession'), summary: copy('Une école active, quatre progressions conservées et des formules à apprendre auprès des maîtres.', 'One active school, four retained progressions and formulas learned from trainers.'), status: 'prototype',
    sections: [
      section('Choisir son école', 'Choose a school', 'Mineur, Forgeron et Bûcheron sont accessibles sans prérequis. Charpentier se débloque à Bûcheron 10. Appuyez sur E près du maître pour changer d’école ; vos niveaux et XP restent conservés.', 'Miner, Blacksmith and Logger have no prerequisite. Carpenter unlocks at Logger 10. Press E near a trainer to switch schools; your levels and XP are retained.'),
      section('Faire progresser ses compétences', 'Advance your skills', 'Seule l’école active gagne de l’XP. Récolter donne 5 XP à l’école correspondante ; fabriquer une brique donne 10 XP en Mineur actif. Les compétences valent 5 × (niveau − 1), sans compteur d’XP séparé.', 'Only the active school earns XP. Gathering grants 5 XP to the matching school; crafting a brick grants 10 XP while Miner is active. Skills equal 5 × (level − 1), without separate XP counters.'),
      section('Conserver ses formules', 'Keep your formulas', 'Les formules s’apprennent séparément auprès du maître et apparaissent dans L. Une formule apprise reste utilisable dans une autre école si ses prérequis sont remplis, mais n’y accorde pas d’XP. La forge, la fonte, le travail du bois et la charpenterie n’ont pas encore de recettes.', 'Formulas are learned separately from the trainer and appear in L. A learned formula remains usable in another school if its requirements are met, but grants no XP there. Smithing, smelting, woodworking and carpentry have no recipes yet.'),
    ], facts: [fact('École initiale', 'Starting school', 'Mineur', 'Miner'), fact('Plafond actuel', 'Current cap', '100'), fact('XP au niveau suivant', 'XP to the next level', '100 × niveau courant', '100 × current level')], related: ['miner', 'blacksmith', 'logger', 'carpenter', 'stone-brick', 'sandstone-brick'], source: craftSource,
  },
  {
    id: 'gathering', category: 'resources', title: copy('Guide de récolte', 'Gathering guide'), summary: copy('Maintenir G, choisir la bonne école et prévoir assez de place dans les sacs.', 'Hold G, choose the right school and leave enough room in your bags.'), status: 'prototype',
    sections: [
      section('Donner des coups', 'Striking a resource', 'Maintenez G près d’un arbre ou rocher. La portée est de 3 mètres depuis la surface de la ressource ; les coups sont espacés d’au moins 0,85 seconde. L’outil de départ fournit 20 unités de travail par coup.', 'Hold G near a tree or rock. Reach is 3 metres from the resource’s surface; strikes are at least 0.85 seconds apart. A starter tool provides 20 work units per strike.'),
      section('Recevoir la récolte et l’XP', 'Receive the harvest and XP', 'Le travail de la ressource est partagé. Seul le dernier joueur à frapper reçoit tout le butin. Il gagne 5 XP si Bûcheron est actif pour le bois, ou Mineur pour la pierre, le grès, le fer et le cuivre. Une récolte avec une autre école donne les matériaux sans XP.', 'Work on a resource is shared. Only the player making the final strike receives the whole yield. They gain 5 XP if Logger is active for wood, or Miner for stone, sandstone, iron and copper. Gathering with another school grants materials without XP.'),
      section('Prévoir le portage', 'Plan your carrying capacity', 'La capacité initiale est de 40 kg et les ressources s’empilent par 100. Un manque de place ou de capacité refuse le dernier coup sans perte de ressource. Outils, cristaux et taux du royaume peuvent modifier le rendement ; les compétences de métier ne renforcent pas les coups.', 'Initial carrying capacity is 40 kg and resources stack to 100. Insufficient space or carrying capacity rejects the final strike without consuming the resource. Tools, crystals and realm rates can change yield; profession skills do not strengthen strikes.'),
    ], related: ['wood', 'stone', 'sandstone', 'iron-ore', 'copper-ore', 'miner', 'logger'], source: harvestSource,
  },
  {
    id: 'encounters', category: 'world', title: copy('Camps et premières rencontres', 'Camps and first encounters'), summary: copy('Trois camps de loups-garous et de revenants, des objectifs d’élimination et une attaque au contact.', 'Three werewolf and revenant camps, kill objectives and a melee attack.'), status: 'prototype',
    sections: [
      section('Sélectionner et attaquer', 'Select and attack', 'Tab sélectionne un ennemi proche. F ou 1 déclenche une attaque au contact. Restez à portée et gardez une ligne de vue libre. Le cercle au sol et les barres de vie identifient votre cible.', 'Tab selects a nearby enemy. F or 1 triggers a melee attack. Stay within reach and keep a clear line of sight. The ground ring and health bars identify your target.'),
      section('Les trois camps', 'The three camps', 'Les objectifs sont acceptés automatiquement et demandent chacun trois éliminations dans leur zone.', 'Objectives are accepted automatically and each requires three kills in its zone.', [copy('Lisière des Crocs : quatre loups-garous · Sécuriser la lisière.', 'Lisière des Crocs: four werewolves · Secure the woodland edge.'), copy('Guet oublié : quatre revenants · Faire taire les revenants.', 'Guet oublié: four revenants · Silence the revenants.'), copy('Creux des Murmures : deux loups-garous et deux revenants · Repousser la meute corrompue.', 'Creux des Murmures: two werewolves and two revenants · Repel the corrupted pack.')]),
      section('Récompenses et reprise', 'Rewards and recovery', 'Le joueur qui porte le dernier coup reçoit 20 XP dans sa classe active. Les compteurs d’objectif s’arrêtent à trois ; leur complétion n’accorde pas de bonus supplémentaire. Les rencontres ne distribuent pas encore de loot. Le personnage récupère progressivement hors combat et revient en vie après six secondes, lorsqu’un point sûr est disponible.', 'The player landing the final blow receives 20 XP in their active class. Objective counters stop at three; completing them grants no extra bonus. Encounters do not distribute loot yet. The character gradually recovers outside combat and returns to life after six seconds when a safe location is available.'),
      section('Portée du prototype', 'Prototype scope', 'Les ennemis restent dans leur partition ; les combats à travers une frontière ne sont pas encore pris en charge. L’attaque au contact est distincte des 25 capacités de classe, dont les effets restent à intégrer.', 'Enemies stay within their partition; combat across a boundary is not supported yet. The melee attack is separate from the 25 class abilities, whose effects remain to be integrated.'),
    ], facts: [fact('Camps', 'Camps', '3'), fact('Objectif par camp', 'Objective per camp', '3 éliminations', '3 kills'), fact('XP par ennemi vaincu', 'XP per defeated enemy', '20 · dernier coup', '20 · final blow'), fact('Portée de l’attaque', 'Attack reach', '3,2 m', '3.2 m')], related: ['starter-island', 'first-steps', 'progression'], source: 'docs/world/STARTER_ISLAND_ENCOUNTERS.md',
  },
  {
    id: 'starter-island', category: 'world', title: copy('Île de l’Aube', 'Dawn Island'), summary: copy('L’île de départ, son arrivée à l’Anse de l’Éveil et la ville d’Aubefont.', 'The starting island, its arrival at Awakening Cove and the town of Aubefont.'), status: 'prototype',
    sections: [
      section('Explorer les premières terres', 'Explore the first lands', 'L’île de départ est conçue pour les niveaux 1 à 6. L’arrivée se fait à l’Anse de l’Éveil. Les routes conduisent vers le refuge et les secteurs de l’île, avec Aubefont comme repère pour rencontrer les maîtres de métier.', 'The starting island is designed for levels 1 to 6. You arrive at Awakening Cove. Roads lead to the refuge and island regions, with Aubefont as a place to meet profession trainers.'),
      section('Se repérer', 'Finding your way', 'M ouvre la carte. Le monde comprend un cycle jour et nuit, de la pluie, du vent et un océan violet à sa limite. Course, marche, saut et nage font partie de l’exploration.', 'M opens the map. The world includes a day and night cycle, rain, wind and a violet ocean at its boundary. Running, walking, jumping and swimming are part of exploration.'),
      section('Rencontrer les ennemis', 'Meeting enemies', 'La Lisière des Crocs, le Guet oublié et le Creux des Murmures abritent les trois camps du prototype. Des objectifs automatiques suivent vos éliminations de loups-garous et de revenants.', 'Lisière des Crocs, Guet oublié and Creux des Murmures host the prototype’s three camps. Automatic objectives track your werewolf and revenant kills.'),
      section('Contenu en évolution', 'Evolving content', 'L’île, les rencontres, la récolte et les ateliers font partie du prototype. Les 25 capacités de classe et la progression complète restent en cours d’intégration. Les fonctions présentes dans le dépôt peuvent différer du build disponible sur votre realm.', 'The island, encounters, gathering and workshops are part of the prototype. The 25 class abilities and the full progression loop remain in development. Features present in the repository may differ from the build available on your realm.'),
    ], related: ['first-steps', 'encounters', 'gathering', 'miner', 'logger'], source: 'docs/world/STARTER_ISLAND.md',
  },
];

export const entries = [
  ...guides,
  ...classEntries,
  ...abilityEntries,
  ...schoolEntries,
  ...craftSkillEntries,
  ...recipeEntries,
  ...resourceEntries,
];
