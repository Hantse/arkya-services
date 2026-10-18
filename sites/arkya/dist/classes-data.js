'use strict';

window.ArkyaClasses = [
  {
    id: 'warrior',
    name: 'Warrior',
    role: { fr: 'Tenir le front', en: 'Hold the front line' },
    description: {
      fr: 'Une identité proposée autour du combat au contact, de la résistance et de la protection. Le Warrior choisit quand tenir sa position et quand avancer vers sa cible.',
      en: 'A proposed identity built around melee combat, resilience and protection. The Warrior chooses when to hold position and when to close in on a target.',
    },
    note: {
      fr: 'Garde demande une arme de mêlée, sans imposer de bouclier. Attaquer ou lancer un sort, y compris Soin hérité, met fin à la garde. Provocation et Charge restent réservées au Warrior dans cette proposition.',
      en: 'Guard requires a melee weapon, but no shield. Attacking or casting a spell, including inherited Heal, ends the guard. Taunt and Charge remain exclusive to Warrior in this proposal.',
    },
    spells: [
      {
        id: 'powerful-strike',
        name: { fr: 'Frappe puissante', en: 'Powerful Strike' },
        level: 1,
        rank2: 5,
        confirmed: false,
        description: {
          fr: 'Un coup renforcé porté avec une arme de mêlée à la cible devant soi.',
          en: 'A stronger melee weapon strike against the target in front of you.',
        },
      },
      {
        id: 'guard',
        name: { fr: 'Garde', en: 'Guard' },
        level: 1,
        rank2: 5,
        confirmed: false,
        description: {
          fr: 'Une garde maintenue réduit les dégâts physiques venant de face. Elle demande une arme de mêlée ; attaquer ou lancer un sort y met fin.',
          en: 'A held guard reduces physical damage from the front. It requires a melee weapon and ends when you attack or cast a spell.',
        },
      },
      {
        id: 'taunt',
        name: { fr: 'Provocation', en: 'Taunt' },
        level: 3,
        rank2: null,
        confirmed: false,
        description: {
          fr: 'Force temporairement un ennemi non joueur à se concentrer sur le Warrior. Ne contrôle pas les actions des autres joueurs.',
          en: 'Temporarily forces a non-player enemy to focus on the Warrior. It does not control other players’ actions.',
        },
      },
      {
        id: 'hindering-strike',
        name: { fr: 'Coup entravant', en: 'Hindering Strike' },
        level: 6,
        rank2: 10,
        confirmed: false,
        description: {
          fr: 'Une frappe de mêlée ralentit la cible pour mieux contrôler la distance.',
          en: 'A melee strike slows the target, helping you control the distance.',
        },
      },
      {
        id: 'charge',
        name: { fr: 'Charge', en: 'Charge' },
        level: 10,
        rank2: null,
        confirmed: false,
        description: {
          fr: 'Rejoint rapidement une cible ennemie par un trajet praticable. Ne traverse pas les obstacles, ne libère pas d’une immobilisation et n’ajoute pas d’étourdissement.',
          en: 'Quickly closes in on an enemy along a traversable route. It cannot pass through obstacles or break a root, and adds no stun.',
        },
      },
    ],
  },
  {
    id: 'scout',
    name: 'Scout',
    role: { fr: 'Choisir sa distance', en: 'Choose your distance' },
    description: {
      fr: 'Une identité proposée fondée sur la précision, la mobilité et le repérage. Le Scout prépare ses tirs et se replace pour garder l’avantage.',
      en: 'A proposed identity focused on precision, mobility and scouting. The Scout prepares each shot and repositions to keep the advantage.',
    },
    note: {
      fr: 'Les tirs demandent une arme à distance compatible : les hériter ne donne pas le droit de l’équiper. La Marque de traque bénéficie uniquement au Scout qui l’a posée. La détection du Camouflage reste à concevoir.',
      en: 'Shots require a compatible ranged weapon: inheriting them does not grant permission to equip one. Tracking Mark benefits only the Scout who applied it. Camouflage detection still needs to be designed.',
    },
    spells: [
      {
        id: 'precise-shot',
        name: { fr: 'Tir précis', en: 'Precise Shot' },
        level: 1,
        rank2: 5,
        confirmed: false,
        description: {
          fr: 'Un tir préparé sur une cible devant soi. Demande de rester immobile et d’utiliser une arme à distance compatible.',
          en: 'A prepared shot at a target in front of you. Requires standing still and using a compatible ranged weapon.',
        },
      },
      {
        id: 'light-step',
        name: { fr: 'Pas léger', en: 'Light Step' },
        level: 1,
        rank2: 5,
        confirmed: false,
        description: {
          fr: 'Accélère temporairement le déplacement, y compris en combat. N’accorde ni invulnérabilité ni suppression d’entrave.',
          en: 'Temporarily increases your movement speed, including during combat. It grants no invulnerability and removes no movement hindrance.',
        },
      },
      {
        id: 'tracking-mark',
        name: { fr: 'Marque de traque', en: 'Tracking Mark' },
        level: 3,
        rank2: null,
        confirmed: false,
        description: {
          fr: 'Marque une proie : le prochain Tir précis du Scout qui l’a posée reçoit un bonus et consomme la marque. Les alliés n’en reçoivent pas le bonus.',
          en: 'Marks a quarry: the next Precise Shot from the Scout who applied it gains a bonus and consumes the mark. Allies do not receive the bonus.',
        },
      },
      {
        id: 'hindering-shot',
        name: { fr: 'Tir entravant', en: 'Hindering Shot' },
        level: 6,
        rank2: 10,
        confirmed: false,
        description: {
          fr: 'Un tir ralentit une cible devant soi. Demande une arme à distance compatible.',
          en: 'A shot slows a target in front of you. Requires a compatible ranged weapon.',
        },
      },
      {
        id: 'camouflage',
        name: { fr: 'Camouflage', en: 'Camouflage' },
        level: 10,
        rank2: null,
        confirmed: false,
        description: {
          fr: 'Permet de se dissimuler hors combat. Une action offensive, un soin ou des dégâts reçus rompent l’effet ; il ne permet pas d’effacer un combat en cours.',
          en: 'Allows you to hide outside combat. An offensive action, healing or taking damage breaks the effect; it cannot end an ongoing fight.',
        },
      },
    ],
  },
  {
    id: 'mage',
    name: 'Mage',
    role: { fr: 'Maîtriser l’espace', en: 'Control the space' },
    description: {
      fr: 'Une identité proposée mêlant magie offensive et contrôle des distances. Le Mage alterne projectiles, attaques en cône et repositionnement.',
      en: 'A proposed identity combining offensive magic with control of distance. The Mage alternates projectiles, cone attacks and repositioning.',
    },
    note: {
      fr: 'Barrière arcanique protège uniquement des dégâts magiques. Téléportation courte reste propre au Mage ; elle respecte les obstacles et ne peut pas être utilisée pendant une immobilisation.',
      en: 'Arcane Barrier protects only against magical damage. Short Teleport stays exclusive to Mage; it respects obstacles and cannot be used while rooted.',
    },
    spells: [
      {
        id: 'arcane-bolt',
        name: { fr: 'Projectile arcanique', en: 'Arcane Bolt' },
        level: 1,
        rank2: 5,
        confirmed: false,
        description: {
          fr: 'Une incantation inflige des dégâts magiques directs à la cible devant soi.',
          en: 'A cast deals direct magical damage to the target in front of you.',
        },
      },
      {
        id: 'arcane-barrier',
        name: { fr: 'Barrière arcanique', en: 'Arcane Barrier' },
        level: 1,
        rank2: 5,
        confirmed: false,
        description: {
          fr: 'Un bouclier personnel absorbe une quantité limitée de dégâts magiques. Il ne protège pas des dégâts physiques.',
          en: 'A personal shield absorbs a limited amount of magical damage. It does not protect against physical damage.',
        },
      },
      {
        id: 'flame-wave',
        name: { fr: 'Vague de flammes', en: 'Flame Wave' },
        level: 3,
        rank2: null,
        confirmed: false,
        description: {
          fr: 'Une incantation touche les ennemis dans un cône devant le Mage.',
          en: 'A cast hits enemies in a cone in front of the Mage.',
        },
      },
      {
        id: 'frost-shard',
        name: { fr: 'Éclat de givre', en: 'Frost Shard' },
        level: 6,
        rank2: 10,
        confirmed: false,
        description: {
          fr: 'Un impact magique frappe et ralentit une cible devant soi.',
          en: 'A magical impact strikes and slows a target in front of you.',
        },
      },
      {
        id: 'short-teleport',
        name: { fr: 'Téléportation courte', en: 'Short Teleport' },
        level: 10,
        rank2: null,
        confirmed: false,
        description: {
          fr: 'Se repositionne dans la direction du personnage vers une destination valide, sans traverser d’obstacle. N’accorde ni invulnérabilité ni dissipation de contrôle ; inutilisable pendant une immobilisation.',
          en: 'Repositions you in the direction you face, to a valid destination without crossing an obstacle. Grants no invulnerability or removal of control effects; cannot be used while rooted.',
        },
      },
    ],
  },
  {
    id: 'cleric',
    name: 'Cleric',
    role: { fr: 'Soigner et protéger', en: 'Heal and protect' },
    description: {
      fr: 'Une identité proposée centrée sur les soins directs et la protection des alliés. Le Cleric restaure la vie, absorbe les dégâts et veille sur son groupe.',
      en: 'A proposed identity centered on direct healing and protection for allies. The Cleric restores health, absorbs damage and watches over the group.',
    },
    note: {
      fr: 'Les seuils de Soin sont confirmés : R1 au niveau 1, puis R2 au niveau 5 libère R1 pour les autres classes. Son effet détaillé reste proposé. Soin hérité conserve son incantation ; la résurrection sera étudiée plus tard.',
      en: 'Heal’s thresholds are confirmed: R1 at level 1, then learning R2 at level 5 unlocks R1 for other classes. Its detailed effect remains proposed. Inherited Heal keeps its cast time; resurrection will be explored later.',
    },
    spells: [
      {
        id: 'heal',
        name: { fr: 'Soin / Heal', en: 'Heal' },
        level: 1,
        rank2: 5,
        confirmed: true,
        description: {
          fr: 'Effet proposé : une incantation restaure directement la vie d’un allié ou de soi-même. Seuls les niveaux 1 et 5 et l’héritage de R1 après l’apprentissage de R2 sont confirmés.',
          en: 'Proposed effect: a cast directly restores an ally’s health or your own. Only the level 1 and 5 thresholds and inheritance of R1 after learning R2 are confirmed.',
        },
      },
      {
        id: 'holy-bolt',
        name: { fr: 'Trait sacré', en: 'Holy Bolt' },
        level: 1,
        rank2: 5,
        confirmed: false,
        description: {
          fr: 'Une incantation offensive frappe une cible ennemie devant soi, pour permettre au Cleric de progresser seul.',
          en: 'An offensive cast strikes an enemy in front of you, allowing the Cleric to progress alone.',
        },
      },
      {
        id: 'holy-aegis',
        name: { fr: 'Égide sacrée', en: 'Holy Aegis' },
        level: 3,
        rank2: null,
        confirmed: false,
        description: {
          fr: 'Un bouclier absorbe des dégâts physiques ou magiques sur un allié ou soi-même.',
          en: 'A shield absorbs physical or magical damage on an ally or yourself.',
        },
      },
      {
        id: 'dispel',
        name: { fr: 'Dissipation', en: 'Dispel' },
        level: 6,
        rank2: 10,
        confirmed: false,
        description: {
          fr: 'Retire une malédiction dissipable sur un allié ou soi-même, sans effet sur les poisons ni les blessures physiques. R2 améliore le coût, sans ajouter de catégorie dissipable.',
          en: 'Removes a dispellable curse from an ally or yourself, with no effect on poison or physical wounds. R2 improves the cost without adding another dispellable category.',
        },
      },
      {
        id: 'group-prayer',
        name: { fr: 'Prière de groupe', en: 'Group Prayer' },
        level: 10,
        rank2: null,
        confirmed: false,
        description: {
          fr: 'Une canalisation soigne progressivement les membres du groupe proches du Cleric. Elle empêche de lancer un autre sort simultanément.',
          en: 'A channeled prayer gradually heals nearby group members. You cannot cast another spell while channeling it.',
        },
      },
    ],
  },
  {
    id: 'druid',
    name: 'Druid',
    role: { fr: 'Soutenir dans la durée', en: 'Sustain over time' },
    description: {
      fr: 'Une identité proposée autour de la nature, des soins dans la durée et des entraves. Le Druid anticipe les dégâts et choisit quand immobiliser ou attaquer.',
      en: 'A proposed identity built around nature, healing over time and hindrances. The Druid anticipates damage and chooses when to root or attack.',
    },
    note: {
      fr: 'Les dégâts progressifs de Ronces rompent aussi Enracinement. Floraison consomme uniquement les Renouveaux du lanceur, sans dupliquer les soins. Les formes animales restent une piste pour plus tard.',
      en: 'Bramble’s damage over time also breaks Entangling Roots. Bloom consumes only the caster’s own Renewals, without duplicating healing. Animal forms remain a possibility for later.',
    },
    spells: [
      {
        id: 'bramble',
        name: { fr: 'Ronces', en: 'Bramble' },
        level: 1,
        rank2: 5,
        confirmed: false,
        description: {
          fr: 'Une incantation produit un impact naturel, puis des dégâts progressifs sur une cible ennemie devant soi. Ces dégâts rompent aussi Enracinement.',
          en: 'A cast delivers a nature impact, followed by damage over time to an enemy in front of you. This damage also breaks Entangling Roots.',
        },
      },
      {
        id: 'renewal',
        name: { fr: 'Renouveau', en: 'Renewal' },
        level: 1,
        rank2: 5,
        confirmed: false,
        description: {
          fr: 'Soigne progressivement un allié ou soi-même, sans soin immédiat au lancement. Hériter de Renouveau ne donne pas accès à Floraison.',
          en: 'Heals an ally or yourself over time, with no immediate healing when applied. Inheriting Renewal does not grant access to Bloom.',
        },
      },
      {
        id: 'entangling-roots',
        name: { fr: 'Enracinement', en: 'Entangling Roots' },
        level: 3,
        rank2: null,
        confirmed: false,
        description: {
          fr: 'Immobilise une cible ennemie. De nouveaux dégâts reçus rompent l’effet, y compris les dégâts progressifs déjà appliqués.',
          en: 'Roots an enemy in place. Further damage breaks the effect, including ticks from damage over time already applied.',
        },
      },
      {
        id: 'barkskin',
        name: { fr: 'Peau d’écorce', en: 'Barkskin' },
        level: 6,
        rank2: 10,
        confirmed: false,
        description: {
          fr: 'Réduit temporairement les dégâts physiques subis par le lanceur, tout en lui permettant de continuer à agir.',
          en: 'Temporarily reduces physical damage taken by the caster while allowing them to keep acting.',
        },
      },
      {
        id: 'bloom',
        name: { fr: 'Floraison', en: 'Bloom' },
        level: 10,
        rank2: null,
        confirmed: false,
        description: {
          fr: 'Termine les Renouveaux du Druid sur les membres du groupe proches et convertit leurs soins restants en soins immédiats. Ne consomme pas ceux d’un autre Druid et ne duplique pas les soins.',
          en: 'Ends the Druid’s Renewals on nearby group members and converts their remaining healing into immediate healing. Does not consume another Druid’s effects or duplicate healing.',
        },
      },
    ],
  },
];
