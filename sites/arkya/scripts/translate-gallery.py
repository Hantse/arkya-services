"""Generate the English catalogue without reading or re-encoding artwork.

Run from any directory: python scripts/translate-gallery.py
Exact phrase maps intentionally fail on new untranslated source content.
"""

import json
from pathlib import Path


DIST = Path(__file__).resolve().parents[1] / "dist"

COLLECTIONS = {
    "Arbres · matières V3": "Trees · V3 materials",
    "Arbres · silhouettes": "Trees · silhouettes",
    "Atelier des origines": "Origins workshop",
    "Buissons": "Bushes",
    "Cailloux": "Stones",
    "Champignons": "Mushrooms",
    "Cristaux": "Crystals",
    "Directions UI": "UI directions",
    "Explorations d’univers": "World explorations",
    "Fleurs": "Flowers",
    "HUD V3": "HUD V3",
    "Mares de mana": "Mana pools",
    "Outils invoqués": "Summoned tools",
    "Personnages animés": "Animated characters",
    "Roches métallifères": "Ore-bearing rocks",
    "Souches": "Stumps",
    "Structures modulaires": "Modular structures",
    "Touffes d’herbe": "Grass tufts",
    "Troncs couchés": "Fallen logs",
    "Études Blender": "Blender studies",
    "Études de personnages": "Character studies",
    "Île de départ · Unity": "Starter island · Unity",
}

# Titles use complete, delimited phrases; no substring or word replacement.
PHRASES = {
    **COLLECTIONS,
    "0 sockets": "0 sockets",
    "1 socket": "1 socket",
    "2 sockets": "2 sockets",
    "3 sockets": "3 sockets",
    "Affleurement": "Outcrop",
    "Amanite bombée": "Domed amanita",
    "Ambre": "Amber",
    "Améthyste": "Amethyst",
    "Ancien moussu": "Ancient moss",
    "Arbre de référence": "Reference tree",
    "Arbre jeune": "Young tree",
    "Arbre vénérable": "Venerable tree",
    "Arbre élancé": "Slender tree",
    "Arbre étalé": "Spreading tree",
    "Arbres": "Trees",
    "Automnal": "Autumnal",
    "Azur": "Azure",
    "Billot court": "Short log",
    "Blender": "Blender",
    "Bloc fendu": "Split block",
    "Bloc oblique": "Slanted block",
    "Bois": "Wood",
    "Bois brûlé": "Burnt wood",
    "Bouquet": "Cluster",
    "Bouquet de prairie": "Meadow bouquet",
    "Brûlé": "Burnt",
    "Buisson dressé": "Upright bush",
    "Buisson rond": "Round bush",
    "Buisson étalé": "Spreading bush",
    "Carnet d’exploration": "Explorer’s journal",
    "Chanterelles en éventail": "Fan-shaped chanterelles",
    "Chaume": "Thatch",
    "Cités d’Éther": "Aether Cities",
    "Colonie de boutons": "Button mushroom colony",
    "Corail abricot": "Apricot coral",
    "Corail crème": "Cream coral",
    "Coussin bas": "Low cushion",
    "Cuivre patiné": "Patinated copper",
    "Elfe": "Elf",
    "Encre & cuivre": "Ink & copper",
    "Faucille invoquée": "Summoned sickle",
    "Fer acier": "Steel-toned iron",
    "Filon oblique": "Slanted vein",
    "Fondation": "Foundation",
    "Formation maîtresse": "Main formation",
    "Galet compact": "Compact pebble",
    "Grand lis étoilé": "Large star lily",
    "Grand tronc fourchu": "Large forked log",
    "Grande mare à lobes": "Large lobed pool",
    "Grès chaud": "Warm sandstone",
    "Géode ouverte": "Open geode",
    "HUD": "HUD",
    "Hache invoquée": "Summoned axe",
    "Hampe de clochettes": "Bellflower stem",
    "Hautes herbes": "Tall grass",
    "Herbe dorée": "Golden grass",
    "Humain": "Human",
    "Humaine": "Female human",
    "Ivoire doré": "Golden ivory",
    "La côte sous l’orage": "The storm-battered coast",
    "La lune et les étoiles": "The moon and stars",
    "Le point d’arrivée": "The arrival point",
    "Le refuge": "The refuge",
    "Le sous-bois": "The forest floor",
    "L’océan violet": "The violet ocean",
    "L’île de départ": "The starter island",
    "Mana azur": "Azure mana",
    "Mana bleu azur": "Azure blue mana",
    "Mana orange": "Orange mana",
    "Mana orange ambré": "Amber orange mana",
    "Mana vert": "Green mana",
    "Mana vert émeraude": "Emerald green mana",
    "Mare de mana": "Mana pool",
    "Miel des sous-bois": "Woodland honey",
    "Mur avec fenêtre": "Wall with window",
    "Mur avec porte": "Wall with door",
    "Mur plein": "Solid wall",
    "Mystique": "Mystic",
    "Mystique boréal": "Boreal mystic",
    "Nodule": "Nodule",
    "Or stylisé": "Stylized gold",
    "Parasol ancien": "Ancient parasol",
    "Petite mare allongée": "Small elongated pool",
    "Pierre": "Stone",
    "Pierre de mana": "Mana stone",
    "Pierre gris bleu": "Blue-grey stone",
    "Pierre moussue": "Mossy stone",
    "Pioche invoquée": "Summoned pickaxe",
    "Plafond": "Ceiling",
    "Prairie verte": "Green meadow",
    "Rocher large": "Broad rock",
    "Rosette basse": "Low rosette",
    "Royaumes de l’Aube": "Realms of Dawn",
    "Souche basse": "Low stump",
    "Souche creuse": "Hollow stump",
    "Souche tordue": "Twisted stump",
    "Terres des Anciens": "Lands of the Ancients",
    "Touffe courbée": "Curved tuft",
    "Touffe rase": "Short tuft",
    "Touffe éventail": "Fan-shaped tuft",
    "Trois langages d’interface": "Three interface styles",
    "Tronc creux": "Hollow log",
    "Tronc noueux": "Gnarled log",
    "UI": "UI",
    "Unity": "Unity",
    "V3": "V3",
    "Vert sylvestre": "Forest green",
    "Vieille souche brisée": "Old broken stump",
    "animation": "animation",
    "apparence": "appearance",
    "composants": "components",
    "concept": "concept",
    "costume": "costume",
    "course": "running",
    "course et cheveux tressés": "running with braided hair",
    "crépuscule": "dusk",
    "environnement": "environment",
    "gameplay": "gameplay",
    "la nuit": "at night",
    "lumière du jour": "daylight",
    "maquette": "mockup",
    "marche": "walking",
    "marche et cheveux longs": "walking with long hair",
    "palette": "palette",
    "personnage": "character",
    "personnage V3": "V3 character",
    "posture au repos": "idle pose",
    "prototype": "prototype",
    "rendu 3D": "3D render",
    "rig": "rig",
    "silhouette": "silhouette",
    "troisième personne": "third person",
    "vue de gameplay": "gameplay view",
    "vue d’ensemble": "overview",
    "Éclat": "Shard",
    "étude Blender V3": "V3 Blender study",
    "île": "island",
}

ORIGINS = {
    "Capture Unity": "Unity screenshot",
    "Capture de l’atelier d’apparence": "Appearance workshop screenshot",
    "Concept généré": "Generated concept",
    "Rendu Blender": "Blender render",
}

DESCRIPTIONS = {
    "Pierre mate, volumes usés et arêtes adoucies.": "Matte stone, weathered forms and softened edges.",
    "Filons et inclusions métalliques intégrés à une roche peinte.": "Metal veins and inclusions embedded in painted rock.",
    "Formations colorées aux arêtes travaillées et à la lumière contenue.": "Colourful formations with carefully shaped edges and a restrained glow.",
    "Masses de feuilles courbées, nervures discrètes et ombres peintes.": "Clusters of curved leaves, subtle veins and painted shadows.",
    "Brins courbés et grandes variations de couleur lisibles à distance.": "Curved blades and broad colour variations that remain readable at a distance.",
    "Chapeaux sculptés, rebords doux et dégradés peints.": "Sculpted caps, soft edges and painted gradients.",
    "Pétales modelés, feuillages souples et palettes de sous-bois.": "Sculpted petals, flowing foliage and woodland palettes.",
    "Racines noueuses, bois peint et cavités modelées.": "Gnarled roots, painted wood and sculpted hollows.",
    "Écorce issue de la référence V3 et cernes sur les coupes de bois.": "Bark based on the V3 reference, with growth rings on the cut ends.",
    "Surface peinte, volutes et gouttes assorties. Aperçu fixe d’un modèle animé.": "A painted surface with matching wisps and droplets. Still preview of an animated model.",
    "Outil de récolte au cœur de mana et aux fragments en suspension. Proposition artistique, aperçu fixe.": "A harvesting tool with a mana core and floating fragments. Art proposal, still preview.",
    "Pièce de construction peinte pour un assemblage sur grille. Proposition artistique.": "A painted building piece for grid-based assembly. Art proposal.",
    "Finition peinte V3 approuvée : la référence de volumes et de surfaces pour les objets d’Arkya.": "Approved V3 painted finish: the reference for the forms and surfaces of Arkya’s objects.",
    "Étude de composition et de matières calculée dans Blender Cycles. Le rendu du jeu reste en développement.": "A composition and material study rendered in Blender Cycles. The game’s rendering remains in development.",
    "Étude de silhouette, de vêtements et d’équipement. Personnage adapté des bases Rain et Snow de Blender Studio.": "A study of silhouette, clothing and equipment. Character adapted from Blender Studio’s Rain and Snow bases.",
    "Première planche comparative des pistes Encre & cuivre, Carnet d’exploration et Pierre de mana.": "An initial comparison board for the Ink & copper, Explorer’s journal and Mana stone directions.",
    "Maquette générée sur un fond de la forêt Unity : HUD allégé, groupe compact, objectifs sans panneau et deux barres d’action. Interface non implémentée.": "A generated mockup over a Unity forest background: a simplified HUD, compact party display, objectives without a panel and two action bars. Interface not implemented.",
    "Étude personnalisable : silhouette, taille, peau, cheveux, regard et étoffe. Proposition artistique issue du viewer d’apparence.": "A customizable study: silhouette, height, skin, hair, eyes and fabric. Art proposal from the appearance viewer.",
    "Aperçu d’une pose du rig de jeu. Les animations et la présentation sont en cours d’intégration ; l’apparence reste une proposition artistique.": "Preview of a pose from the game rig. Animation and presentation integration is ongoing; the appearance remains an art proposal.",
}

WORLD_PALETTES = {
    "Pierre claire, terre cuite, tissus bleu canard et lumière chaleureuse.": "Pale stone, terracotta, teal fabrics and warm light.",
    "Ivoire, bronze, corail et turquoise composent une fantasy lumineuse.": "Ivory, bronze, coral and turquoise create a luminous fantasy world.",
    "Bois sculpté, forêt enveloppante et lumières chaudes des lieux habités.": "Carved wood, an enveloping forest and the warm lights of inhabited places.",
}
WORLD_CONTEXTS = {
    "Planche conceptuelle générée, sans validation du lore ou du rendu final.": "Generated concept board; neither the lore nor the final rendering has been approved.",
    "Simulation de gameplay générée ; ce n’est pas une capture Unity.": "Generated gameplay simulation; this is not a Unity screenshot.",
}
for source_palette, target_palette in WORLD_PALETTES.items():
    for source_context, target_context in WORLD_CONTEXTS.items():
        DESCRIPTIONS[f"{source_palette} {source_context}"] = f"{target_palette} {target_context}"

TREE_SHAPES = {
    "Arbre massif, contreforts puissants, grande couronne courbe et modérément vrillée.": "A massive tree with strong buttress roots and a broad, curved, gently twisted crown.",
    "Jeune arbre fin, houppier compact et vertical, contreforts discrets.": "A slender young tree with a compact, upright crown and subtle buttress roots.",
    "Port étalé, tronc court et large couronne en ombrelle.": "A spreading form, short trunk and broad umbrella-shaped crown.",
    "Tronc élancé, premières fourches hautes et houppier étroit.": "A slender trunk, high first forks and a narrow crown.",
}
for source, target in TREE_SHAPES.items():
    DESCRIPTIONS[f"{source} Silhouette approuvée ; matière de la première étude de forme."] = (
        f"{target} Approved silhouette; material from the first shape study."
    )

ISLAND_VIEWS = {
    "Le terrain du prototype et son parcours d’exploration de niveaux 1 à 6.": "The prototype terrain and its exploration route for levels 1 to 6.",
    "Une première lecture du terrain et du chemin dans le prototype Unity.": "A first look at the terrain and path in the Unity prototype.",
    "Les arbres peints composent le paysage du prototype Unity.": "Painted trees shape the landscape of the Unity prototype.",
    "Un abri modulaire au milieu de la forêt du prototype.": "A modular shelter in the prototype’s forest.",
    "Le cycle jour et nuit modifie le ciel et l’éclairage du refuge.": "The day and night cycle changes the sky and the refuge’s lighting.",
    "Le ciel nocturne du prototype Unity et son cycle de présentation.": "The Unity prototype’s night sky and its visual cycle.",
    "La pluie, la couverture nuageuse et les éclairs animent le prototype.": "Rain, cloud cover and lightning bring the prototype to life.",
    "L’océan bouillonnant marque la limite de l’île dans le prototype Unity.": "The boiling ocean marks the island boundary in the Unity prototype.",
}
for source, target in ISLAND_VIEWS.items():
    DESCRIPTIONS[f"{source} Capture réelle du moteur ; environnement en développement."] = (
        f"{target} Actual engine screenshot; environment in development."
    )


def translated(mapping, source, context):
    try:
        return mapping[source]
    except KeyError as error:
        raise ValueError(f"Missing English translation for {context}: {source!r}") from error


def translate_records(records):
    result = {}
    for record in records:
        identifier = record["id"]
        if identifier in result:
            raise ValueError(f"Duplicate catalogue id: {identifier}")
        result[identifier] = {
            "title": " · ".join(
                translated(PHRASES, part, f"{identifier}.title")
                for part in record["title"].split(" · ")
            ),
            "collection": translated(COLLECTIONS, record["collection"], f"{identifier}.collection"),
            "description": translated(DESCRIPTIONS, record["description"], f"{identifier}.description"),
            "origin": translated(ORIGINS, record["origin"], f"{identifier}.origin"),
            "tags": [translated(PHRASES, tag, f"{identifier}.tags") for tag in record["tags"]],
        }
    return result


def main():
    records = json.loads((DIST / "gallery.json").read_text(encoding="utf-8"))
    localized = translate_records(records)
    serialized = json.dumps(localized, ensure_ascii=False, indent=2) + "\n"
    if "\ufffd" in serialized:
        raise ValueError("Invalid replacement character in English catalogue")
    (DIST / "gallery.en.json").write_text(serialized, encoding="utf-8")
    print(f"English catalogue: {len(localized)} records translated.")


if __name__ == "__main__":
    main()
