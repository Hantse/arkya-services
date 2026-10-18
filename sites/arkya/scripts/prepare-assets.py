"""Build the showcase's static image catalogue from Arkya's checked-in art.

Run from any directory with Python 3 and Pillow: python scripts/prepare-assets.py
Only proportional resizing and WebP encoding are applied; artwork is not retouched.
"""

from collections import Counter
import hashlib
import json
from pathlib import Path
import runpy
import shutil

from PIL import Image, ImageOps


SITE = Path(__file__).resolve().parents[1]
REPO = SITE.parents[1]
DIST = SITE / "dist"
ASSETS = DIST / "assets"
ART = REPO / "docs/art-direction"
RESOURCE_ROOT = ART / "resources"
CATEGORIES = {"univers", "vegetation", "mineraux", "magie", "personnages", "architecture", "interfaces"}
CHARACTER_CREDIT = {
    "creator": "Blender Studio / Blender Foundation",
    "sources": ["https://studio.blender.org/characters/rain/v2/", "https://studio.blender.org/characters/snow/v4/"],
    "license": "CC BY 4.0",
    "licenseUrl": "https://creativecommons.org/licenses/by/4.0/",
    "modifications": "Adaptations Arkya : proportions, oreilles elfiques, coiffures, poses, vêtements et accessoires fantasy, surfaces peintes et matériaux.",
}

FAMILIES = {
    "cailloux": ("mineraux", "Cailloux", "Pierre mate, volumes usés et arêtes adoucies."),
    "roches-metalliferes": ("mineraux", "Roches métallifères", "Filons et inclusions métalliques intégrés à une roche peinte."),
    "cristaux": ("mineraux", "Cristaux", "Formations colorées aux arêtes travaillées et à la lumière contenue."),
    "buissons": ("vegetation", "Buissons", "Masses de feuilles courbées, nervures discrètes et ombres peintes."),
    "herbes": ("vegetation", "Touffes d’herbe", "Brins courbés et grandes variations de couleur lisibles à distance."),
    "champignons": ("vegetation", "Champignons", "Chapeaux sculptés, rebords doux et dégradés peints."),
    "fleurs": ("vegetation", "Fleurs", "Pétales modelés, feuillages souples et palettes de sous-bois."),
    "souches": ("vegetation", "Souches", "Racines noueuses, bois peint et cavités modelées."),
    "troncs-couches": ("vegetation", "Troncs couchés", "Écorce issue de la référence V3 et cernes sur les coupes de bois."),
    "mares-de-mana": ("magie", "Mares de mana", "Surface peinte, volutes et gouttes assorties. Aperçu fixe d’un modèle animé."),
    "outils-invoques": ("magie", "Outils invoqués", "Outil de récolte au cœur de mana et aux fragments en suspension. Proposition artistique, aperçu fixe."),
    "structures": ("architecture", "Structures modulaires", "Pièce de construction peinte pour un assemblage sur grille. Proposition artistique."),
    "reference": ("vegetation", "Arbres · matières V3", "Finition peinte V3 approuvée : la référence de volumes et de surfaces pour les objets d’Arkya."),
}

gallery = []
provenance = []


def read_json(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def add_record(identifier, title, category, collection, status, description, source, tags=(), *, featured=False, max_size=1000, credit=False, origin="Rendu Blender"):
    source = Path(source).resolve()
    source.relative_to(REPO / "docs")
    assert category in CATEGORIES and status in {"approved", "proposal", "exploration", "prototype"}
    assert not any(record["id"] == identifier for record in gallery), identifier
    target = ASSETS / f"{identifier}.webp"
    with Image.open(source) as opened:
        img = ImageOps.exif_transpose(opened)
        original_size = list(img.size)
        img.thumbnail((max_size, max_size), Image.Resampling.LANCZOS)
        if img.mode not in ("RGB", "RGBA"):
            img = img.convert("RGBA" if "transparency" in img.info else "RGB")
        img.save(target, "WEBP", quality=84 if category == "interfaces" else 81, method=6)
        width, height = img.size
    record = {
        "id": identifier, "title": title, "category": category, "collection": collection,
        "status": status, "origin": origin, "description": description, "image": f"assets/{target.name}",
        "width": width, "height": height, "source": source.relative_to(REPO).as_posix(),
        "tags": list(tags), "featured": featured,
    }
    if credit:
        record["credit"] = CHARACTER_CREDIT
    gallery.append(record)
    provenance.append({
        "image": record["image"], "source": record["source"], "origin": origin,
        "sourceSha256": hashlib.sha256(source.read_bytes()).hexdigest(),
        "sourceDimensions": original_size, "outputDimensions": [width, height],
        "bytes": target.stat().st_size, "operation": "Proportional downscale and WebP encoding; no crop or retouching.",
        **({"credit": CHARACTER_CREDIT} if credit else {}),
    })


def build_resources():
    catalogue = read_json(RESOURCE_ROOT / "catalogue.json")
    for family in catalogue["families"]:
        fid = family["id"]
        category, collection, description = FAMILIES[fid]
        shapes = {item["id"]: item["label"] for item in family["shapes"]}
        materials = {item["id"]: item["label"] for item in family["materials"]}
        for index, item in enumerate(family["assets"]):
            title = f"{shapes[item['shape']]} · {materials[item['material']]}"
            if fid == "outils-invoques":
                sockets = item.get("socket_count", 0)
                title += f" · {sockets} socket{'s' if sockets != 1 else ''}"
            status = "proposal" if fid in {"outils-invoques", "structures"} else "approved"
            tags = [collection, shapes[item["shape"]], materials[item["material"]], "rendu 3D"]
            add_record(f"{fid}-{item['id']}", title, category, collection, status, description,
                       RESOURCE_ROOT / item["preview"], tags, featured=index == 0)
    tree_root = ART / "arbre-de-base/declinaisons"
    for item in read_json(tree_root / "catalogue.json")["items"]:
        if item["category"] == "forme":
            add_record(f"arbre-forme-{item['id']}", f"Arbre {item['label'].lower()}", "vegetation", "Arbres · silhouettes",
                       "approved", item["description"] + " Silhouette approuvée ; matière de la première étude de forme.",
                       tree_root / item["preview"].lstrip("/"), ["Arbres", "silhouette", "rendu 3D"])


def build_art_boards():
    directions = [
        ("01-royaumes-aube", "aube", "Royaumes de l’Aube", "Pierre claire, terre cuite, tissus bleu canard et lumière chaleureuse."),
        ("02-cites-ether", "ether", "Cités d’Éther", "Ivoire, bronze, corail et turquoise composent une fantasy lumineuse."),
        ("03-terres-anciens", "anciens", "Terres des Anciens", "Bois sculpté, forêt enveloppante et lumières chaudes des lieux habités."),
    ]
    for stem, short, title, description in directions:
        add_record(f"concept-{short}", title, "univers", "Explorations d’univers", "exploration",
                   description + " Planche conceptuelle générée, sans validation du lore ou du rendu final.",
                   ART / f"{stem}.png", ["concept", "palette", "environnement"], max_size=1600, origin="Concept généré")
        add_record(f"gameplay-{short}", f"{title} · vue de gameplay", "univers", "Explorations d’univers", "exploration",
                   description + " Simulation de gameplay générée ; ce n’est pas une capture Unity.",
                   ART / f"{stem}-gameplay.png", ["concept", "gameplay", "troisième personne"], featured=True, max_size=1920, origin="Concept généré")
    for stem, title in [("02-cites-ether", "Cités d’Éther"), ("03-terres-anciens", "Terres des Anciens")]:
        add_record(f"blender-{stem}", f"{title} · étude Blender V3", "architecture", "Études Blender", "exploration",
                   "Étude de composition et de matières calculée dans Blender Cycles. Le rendu du jeu reste en développement.",
                   ART / f"blender/{stem}-v3-render.png", ["Blender", "environnement", "V3"], max_size=1600, credit=True)
        add_record(f"personnage-{stem}", f"{title} · personnage V3", "personnages", "Études de personnages", "proposal",
                   "Étude de silhouette, de vêtements et d’équipement. Personnage adapté des bases Rain et Snow de Blender Studio.",
                   ART / f"blender/{stem}-character-v3.png", ["personnage", "costume", "Blender"], max_size=1400, credit=True)
    add_record("ui-directions", "Trois langages d’interface", "interfaces", "Directions UI", "exploration",
               "Première planche comparative des pistes Encre & cuivre, Carnet d’exploration et Pierre de mana.",
               ART / "ui/ui-directions-01.png", ["UI", "composants", "concept"], max_size=1920, origin="Concept généré")
    for stem, short, title in [
        ("01-encre-cuivre", "cuivre", "Encre & cuivre"),
        ("02-carnet-exploration", "carnet", "Carnet d’exploration"),
        ("03-pierre-mana", "mana", "Pierre de mana"),
    ]:
        add_record(f"hud-{short}-v3", f"HUD V3 · {title}", "interfaces", "HUD V3", "proposal",
                   "Maquette générée sur un fond de la forêt Unity : HUD allégé, groupe compact, objectifs sans panneau et deux barres d’action. Interface non implémentée.",
                   ART / f"ui/hud/{stem}-v3.png", ["HUD", "V3", title, "maquette"], featured=True, max_size=1920, origin="Concept généré")


def build_characters_and_world():
    for name, title in [("humain", "Humain"), ("elfe", "Elfe")]:
        add_record(f"atelier-{name}", f"Atelier des origines · {title}", "personnages", "Atelier des origines", "proposal",
                   "Étude personnalisable : silhouette, taille, peau, cheveux, regard et étoffe. Proposition artistique issue du viewer d’apparence.",
                   RESOURCE_ROOT / f"personnages/{name}-atelier.png", ["personnage", title, "apparence"], featured=True, max_size=1600, credit=True, origin="Capture de l’atelier d’apparence")
    for stem, title in [
        ("human-a-idle-1", "Humain · posture au repos"), ("human-b-idle-1", "Humaine · posture au repos"),
        ("human-a-walk-10", "Humain · marche"), ("human-b-run-19", "Humaine · course"),
        ("elf-a-run-7-braid", "Elfe · course et cheveux tressés"), ("elf-b-walk-28-long", "Elfe · marche et cheveux longs"),
    ]:
        add_record(f"rig-{stem}", title, "personnages", "Personnages animés", "proposal",
                   "Aperçu d’une pose du rig de jeu. Les animations et la présentation sont en cours d’intégration ; l’apparence reste une proposition artistique.",
                   RESOURCE_ROOT / f"personnages/game/previews/{stem}.png", ["personnage", "animation", "rig"], max_size=1200, credit=True)
    worlds = [
        ("overview", "L’île de départ · vue d’ensemble", "Le terrain du prototype et son parcours d’exploration de niveaux 1 à 6."),
        ("arrival", "Le point d’arrivée", "Une première lecture du terrain et du chemin dans le prototype Unity."),
        ("environment/forest-day", "Le sous-bois · lumière du jour", "Les arbres peints composent le paysage du prototype Unity."),
        ("environment/refuge-day", "Le refuge · lumière du jour", "Un abri modulaire au milieu de la forêt du prototype."),
        ("environment/refuge-dusk", "Le refuge · crépuscule", "Le cycle jour et nuit modifie le ciel et l’éclairage du refuge."),
        ("environment/moon-and-stars", "La lune et les étoiles", "Le ciel nocturne du prototype Unity et son cycle de présentation."),
        ("weather/storm-lightning-coast", "La côte sous l’orage", "La pluie, la couverture nuageuse et les éclairs animent le prototype."),
        ("boundary-water/boiling-night", "L’océan violet · la nuit", "L’océan bouillonnant marque la limite de l’île dans le prototype Unity."),
    ]
    for source, title, description in worlds:
        identifier = "ile-" + source.replace("/", "-")
        add_record(identifier, title, "univers", "Île de départ · Unity", "prototype",
                   description + " Capture réelle du moteur ; environnement en développement.",
                   REPO / f"docs/world/starter-island/{source}.png", ["île", "Unity", "prototype"],
                   featured=source in {"overview", "environment/forest-day", "environment/refuge-day"}, max_size=1600, origin="Capture Unity")


def build_aliases():
    aliases = {
        "hero": "gameplay-anciens", "aube": "gameplay-aube", "ether": "gameplay-ether", "anciens": "gameplay-anciens",
        "tree": "reference-reference-moussu", "tree-moussu": "reference-reference-moussu",
        "tree-mystique": "reference-reference-mystique", "tree-brule": "reference-reference-brule",
        "character": "personnage-03-terres-anciens", "world": "ile-environment-forest-day",
        "interface": "hud-cuivre-v3", "hud-cuivre": "hud-cuivre-v3", "hud-carnet": "hud-carnet-v3", "hud-mana": "hud-mana-v3",
    }
    for alias, identifier in aliases.items():
        source = ASSETS / f"{identifier}.webp"
        target = ASSETS / f"{alias}.webp"
        shutil.copyfile(source, target)
        origin = next(item for item in provenance if item["image"] == f"assets/{identifier}.webp")
        provenance.append({**origin, "image": f"assets/{alias}.webp", "aliasOf": f"assets/{identifier}.webp"})
    return aliases


def main():
    ASSETS.mkdir(parents=True, exist_ok=True)
    build_resources()
    build_art_boards()
    build_characters_and_world()
    aliases = build_aliases()
    gallery.sort(key=lambda record: not record["featured"])
    serialized = json.dumps(gallery, ensure_ascii=False, indent=2)
    assert "\ufffd" not in serialized, "Invalid replacement character in generated metadata"
    (DIST / "gallery.json").write_text(serialized + "\n", encoding="utf-8")
    runpy.run_path(str(SITE / "scripts/translate-gallery.py"), run_name="__main__")
    report = {
        "schemaVersion": 1, "generator": "scripts/prepare-assets.py", "sourceRoot": "repository",
        "note": "Curated current artwork and all catalogue resource previews; legacy iterations and texture maps omitted. Artistic status follows checked-in documentation.",
        "counts": {"records": len(gallery), "featured": sum(record["featured"] for record in gallery),
                   "byStatus": dict(Counter(record["status"] for record in gallery)),
                   "byCategory": dict(Counter(record["category"] for record in gallery)),
                   "files": len(provenance), "bytes": sum(item["bytes"] for item in provenance)},
        "aliases": aliases, "characterAttribution": CHARACTER_CREDIT, "images": provenance,
    }
    (DIST / "asset-provenance.json").write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(report["counts"], ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
