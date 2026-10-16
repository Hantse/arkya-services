"""Validate the character art library using only the Python standard library.

Run: python docs/art-direction/resources/personnages/scripts/validate_characters.py
Writes personnages/validation.json. Does not alter models or open Blender/Unity.
"""

import argparse
from datetime import datetime, timezone
import hashlib
import json
import math
from pathlib import Path
import re
import struct
import sys


ROOT = Path(__file__).resolve().parents[1]
RACES = {"human": 1.75, "elf": 1.85}
SILHOUETTES = ("a", "b")
HAIR_STYLES = ("short", "long", "braid")
TINT_MATERIALS = {"Skin", "Hair", "Iris", "OutfitAccent"}
COMPONENTS = {5120: ("b", 1), 5121: ("B", 1), 5122: ("h", 2),
              5123: ("H", 2), 5125: ("I", 4), 5126: ("f", 4)}
WIDTHS = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4,
          "MAT2": 4, "MAT3": 9, "MAT4": 16}
IDENTITY = ((1, 0, 0, 0), (0, 1, 0, 0), (0, 0, 1, 0), (0, 0, 0, 1))


def require(condition, message):
    if not condition:
        raise ValueError(message)


def integer(value, label, minimum=0):
    require(isinstance(value, int) and not isinstance(value, bool) and value >= minimum,
            f"Invalid {label}: {value!r}")
    return value


def indexed(items, index, label):
    integer(index, label)
    require(index < len(items), f"{label} is outside array")
    return items[index]


def local_file(root, relative, suffix=None):
    require(isinstance(relative, str) and relative, "Missing local asset path")
    path = (root / relative).resolve()
    require(path.is_relative_to(root.resolve()) and path.is_file(),
            f"Missing or outside-root file: {relative}")
    if suffix:
        require(path.suffix.lower() == suffix, f"Expected {suffix}: {relative}")
    return path


def multiply(a, b):
    return tuple(tuple(sum(a[row][k] * b[k][col] for k in range(4))
                       for col in range(4)) for row in range(4))


def transform_point(matrix, point):
    return tuple(sum(matrix[row][k] * point[k] for k in range(3)) + matrix[row][3]
                 for row in range(3))


def node_matrix(node):
    if "matrix" in node:
        values = node["matrix"]
        require(len(values) == 16, "Node matrix must have 16 values")
        result = tuple(tuple(values[col * 4 + row] for col in range(4)) for row in range(4))
    else:
        rotation = node.get("rotation", (0, 0, 0, 1))
        scale = node.get("scale", (1, 1, 1))
        translation = node.get("translation", (0, 0, 0))
        require(len(rotation) == 4 and len(scale) == len(translation) == 3, "Invalid node transform")
        x, y, z, w = rotation
        require(abs(x*x + y*y + z*z + w*w - 1) < .001, "Invalid node quaternion")
        rotation_rows = ((1-2*(y*y+z*z), 2*(x*y-z*w), 2*(x*z+y*w)),
                         (2*(x*y+z*w), 1-2*(x*x+z*z), 2*(y*z-x*w)),
                         (2*(x*z-y*w), 2*(y*z+x*w), 1-2*(x*x+y*y)))
        result = tuple(tuple(rotation_rows[row][col] * scale[col] for col in range(3))
                       + (translation[row],) for row in range(3)) + ((0, 0, 0, 1),)
    require(all(math.isfinite(value) for row in result for value in row), "Non-finite node transform")
    require(all(abs(result[3][i] - IDENTITY[3][i]) < 1e-6 for i in range(4)),
            "Non-affine node transform")
    return result


def bounds(points):
    require(points, "No points for bounds")
    return ([min(point[axis] for point in points) for axis in range(3)],
            [max(point[axis] for point in points) for axis in range(3)])


def geometry_signature(points, scale=1):
    unique = sorted({tuple(round(value / scale, 6) for value in point) for point in points})
    return hashlib.sha256(json.dumps(unique, separators=(",", ":")).encode("ascii")).hexdigest()


class Glb:
    def __init__(self, path):
        self.raw = path.read_bytes()
        require(len(self.raw) >= 28, "Truncated GLB")
        magic, version, size = struct.unpack_from("<4sII", self.raw)
        require(magic == b"glTF" and version == 2 and size == len(self.raw), "Invalid GLB header")
        chunks, offset = {}, 12
        while offset < size:
            require(offset + 8 <= size, "Truncated GLB chunk header")
            length, kind = struct.unpack_from("<II", self.raw, offset)
            require(length % 4 == 0 and offset + 8 + length <= size, "Invalid GLB chunk range")
            require(kind not in chunks, "Duplicate GLB chunk")
            if not chunks:
                require(kind == 0x4E4F534A, "JSON must be first GLB chunk")
            chunks[kind] = memoryview(self.raw)[offset + 8:offset + 8 + length]
            offset += 8 + length
        require(offset == size and 0x4E4F534A in chunks and 0x004E4942 in chunks, "Missing GLB chunks")
        self.data = json.loads(bytes(chunks[0x4E4F534A]))
        self.binary = chunks[0x004E4942]
        self.cache = {}
        require(self.data.get("asset", {}).get("version") == "2.0", "Expected glTF 2.0")
        buffers = self.data.get("buffers", [])
        require(len(buffers) == 1 and "uri" not in buffers[0], "Expected one embedded GLB buffer")
        declared_length = integer(buffers[0]["byteLength"], "buffer byte length", 1)
        require(0 <= len(self.binary) - declared_length <= 3, "Binary chunk length mismatch")
        for view in self.data.get("bufferViews", []):
            require(view.get("buffer", 0) == 0, "Unexpected buffer index")
            offset = integer(view.get("byteOffset", 0), "buffer view offset")
            length = integer(view["byteLength"], "buffer view length", 1)
            require(offset + length <= declared_length, "Buffer view exceeds declared buffer")
        for index in range(len(self.data.get("accessors", []))):
            self.accessor(index)

    def accessor(self, index):
        if index in self.cache:
            return self.cache[index]
        item = indexed(self.data.get("accessors", []), index, "accessor index")
        require("sparse" not in item and "bufferView" in item, "Sparse/missing accessor buffer unsupported")
        view = indexed(self.data.get("bufferViews", []), item["bufferView"], "accessor buffer view")
        require(item.get("componentType") in COMPONENTS and item.get("type") in WIDTHS,
                "Unknown accessor type")
        fmt, size = COMPONENTS[item["componentType"]]
        width = WIDTHS[item["type"]]
        require(not (item["type"] in ("MAT2", "MAT3") and size < 4),
                "Padded small-component matrices unsupported")
        count = integer(item["count"], "accessor count", 1)
        stride = integer(view.get("byteStride", width * size), "accessor stride", width * size)
        offset = integer(item.get("byteOffset", 0), "accessor offset")
        absolute_offset = view.get("byteOffset", 0) + offset
        require(stride % size == 0 and absolute_offset % size == 0, "Misaligned accessor")
        require(offset + (count - 1) * stride + width * size <= view["byteLength"],
                "Accessor exceeds buffer view")
        values = [struct.unpack_from("<" + fmt * width, self.binary, absolute_offset + row * stride)
                  for row in range(count)]
        if item.get("normalized", False):
            require(item["componentType"] in (5120, 5121, 5122, 5123), "Invalid normalized component type")
            maximum = {5120: 127, 5121: 255, 5122: 32767, 5123: 65535}[item["componentType"]]
            values = [tuple(max(-1, value / maximum) for value in row) for row in values]
        require(all(math.isfinite(value) for row in values for value in row), "Non-finite accessor values")
        self.cache[index] = values
        return values

    def embedded_images(self):
        result = []
        images = self.data.get("images", [])
        for index, item in enumerate(images):
            require("uri" not in item and "bufferView" in item, "External image dependency")
            view = indexed(self.data.get("bufferViews", []), item["bufferView"], "image buffer view")
            start = view.get("byteOffset", 0)
            blob = bytes(self.binary[start:start + view["byteLength"]])
            mime = item.get("mimeType")
            if mime == "image/png":
                require(len(blob) >= 33 and blob.startswith(b"\x89PNG\r\n\x1a\n")
                        and blob[12:16] == b"IHDR", "Invalid embedded PNG header")
                width, height = struct.unpack_from(">II", blob, 16)
                require(width > 0 and height > 0, "Empty embedded PNG")
                dimensions = [width, height]
            elif mime == "image/jpeg":
                require(len(blob) > 4 and blob.startswith(b"\xff\xd8") and blob.endswith(b"\xff\xd9"),
                        "Invalid embedded JPEG markers")
                dimensions = None
            else:
                raise ValueError(f"Unsupported image MIME type: {mime}")
            result.append({"index": index, "mime_type": mime, "bytes": len(blob), "size_px": dimensions})
        for texture in self.data.get("textures", []):
            indexed(images, texture.get("source"), "texture image source")
            if "sampler" in texture:
                indexed(self.data.get("samplers", []), texture["sampler"], "texture sampler")
        return result


def material_textures(material):
    result = []
    for key, value in material.items():
        if key.endswith("Texture") and isinstance(value, dict):
            result.append(value)
        elif isinstance(value, dict):
            result.extend(material_textures(value))
    return result


def validate_asset(root, race, silhouette, hair, relative):
    asset_id = f"{race}-{silhouette}-{hair}"
    path = local_file(root, relative, ".glb")
    blend_relative = f"assets/{asset_id}.blend"
    blend_path = local_file(root, blend_relative, ".blend")
    with blend_path.open("rb") as source:
        source_header = source.read(12)
    require(source_header.startswith((b"BLENDER", b"\x1f\x8b", b"\x28\xb5\x2f\xfd")),
            "Invalid Blender source signature")
    glb = Glb(path)
    data = glb.data
    images = glb.embedded_images()
    materials = data.get("materials", [])
    names = [material.get("name", "") for material in materials]
    require(TINT_MATERIALS <= set(names), "Missing tint material: " + ", ".join(sorted(TINT_MATERIALS - set(names))))
    require(all(names.count(name) == 1 for name in TINT_MATERIALS), "Ambiguous tint material names")
    for material in materials:
        for texture in material_textures(material):
            indexed(data.get("textures", []), texture.get("index"), "material texture")
    require(not data.get("cameras"), "Character export contains camera")
    require("KHR_lights_punctual" not in data.get("extensions", {}), "Character export contains lights")
    scene = indexed(data.get("scenes", []), data.get("scene", 0), "default scene")
    require(scene.get("nodes"), "Empty default scene")
    points, hair_points, head_points = [], [], []
    seen, used_materials = set(), set()
    triangles = primitive_count = mesh_node_count = vertex_color_primitives = uv_primitives = 0
    uv_min, uv_max = math.inf, -math.inf

    def visit(index, parent):
        nonlocal triangles, primitive_count, mesh_node_count, vertex_color_primitives, uv_primitives, uv_min, uv_max
        require(index not in seen, "Repeated or cyclic scene node")
        seen.add(index)
        node = indexed(data.get("nodes", []), index, "scene node")
        require("pivot" not in node.get("extras", {}), "Reserved node extras.pivot conflicts with viewer")
        matrix = multiply(parent, node_matrix(node))
        if "mesh" in node:
            mesh_node_count += 1
            mesh = indexed(data.get("meshes", []), node["mesh"], "mesh index")
            require(mesh.get("primitives"), "Empty mesh")
            for primitive in mesh["primitives"]:
                primitive_count += 1
                require(primitive.get("mode", 4) == 4, "Expected triangle primitive")
                attributes = primitive.get("attributes", {})
                require(all(key in attributes for key in ("POSITION", "NORMAL")),
                        "Missing position or normal attribute")
                position = glb.accessor(attributes["POSITION"])
                normal = glb.accessor(attributes["NORMAL"])
                require(len(position) == len(normal), "Attribute counts disagree")
                require(all(len(row) == 3 for row in position + normal), "Attribute vector shape mismatch")
                require(all(abs(sum(value * value for value in row) - 1) < .01 for row in normal),
                        "Non-unit normal")
                material = indexed(materials, primitive.get("material"), "primitive material")
                name = material["name"]
                used_materials.add(name)
                uv_attributes = [key for key in attributes if key.startswith("TEXCOORD_")]
                if uv_attributes:
                    uv_primitives += 1
                for key in uv_attributes:
                    uv = glb.accessor(attributes[key])
                    require(len(uv) == len(position) and all(len(row) == 2 for row in uv),
                            "UV attribute shape mismatch")
                    uv_min = min(uv_min, min(value for row in uv for value in row))
                    uv_max = max(uv_max, max(value for row in uv for value in row))
                for texture in material_textures(material):
                    require(f"TEXCOORD_{texture.get('texCoord', 0)}" in attributes,
                            "Textured material references missing UV coordinates")
                if "COLOR_0" in attributes:
                    colors = glb.accessor(attributes["COLOR_0"])
                    require(len(colors) == len(position) and all(len(row) in (3, 4) for row in colors),
                            "Vertex color shape mismatch")
                    require(all(-1e-6 <= value <= 1 + 1e-6 for row in colors for value in row),
                            "Vertex colors outside [0, 1]")
                    vertex_color_primitives += 1
                else:
                    require(material.get("pbrMetallicRoughness", {}).get("baseColorTexture"),
                            "Primitive has neither painted vertex colors nor a base-color texture")
                if "indices" in primitive:
                    accessor = indexed(data["accessors"], primitive["indices"], "index accessor")
                    require(accessor["type"] == "SCALAR" and accessor["componentType"] in (5121, 5123, 5125)
                            and not accessor.get("normalized", False), "Invalid index accessor format")
                    indices = [row[0] for row in glb.accessor(primitive["indices"])]
                else:
                    indices = list(range(len(position)))
                require(len(indices) > 0 and len(indices) % 3 == 0
                        and min(indices) >= 0 and max(indices) < len(position), "Invalid triangle index range")
                triangles += len(indices) // 3
                world = [transform_point(matrix, value) for value in position]
                require(all(math.isfinite(value) for row in world for value in row), "Non-finite world position")
                points.extend(world)
                if name == "Hair":
                    hair_points.extend(world)
                if "head" in node.get("name", "").lower() and name == "Skin":
                    head_points.extend(world)
        for child in node.get("children", []):
            visit(child, matrix)

    for node in scene["nodes"]:
        visit(node, IDENTITY)
    require(triangles > 0 and mesh_node_count > 0, "Empty character geometry")
    require(TINT_MATERIALS <= used_materials, "Tint material is unused")
    require(hair_points and head_points, "Missing Hair geometry or Skin geometry under Head node")
    low, high = bounds(points)
    base_height = RACES[race]
    require(abs(low[1]) <= .005, f"Feet not on ground: {low[1]:.6f} m")
    require(abs(high[1] - base_height) <= .10,
            f"Unexpected height: {high[1]:.4f} m, expected {base_height} m ±0.10 m including hair")
    head_low, head_high = bounds(head_points)
    return {"id": asset_id, "race": race, "silhouette": silhouette, "hair": hair,
            "glb": relative, "blend": blend_relative, "glb_bytes": len(glb.raw),
            "glb_sha256": hashlib.sha256(glb.raw).hexdigest(),
            "triangles": triangles, "primitives": primitive_count, "mesh_nodes": mesh_node_count,
            "bounds_min_m": low, "bounds_max_m": high,
            "dimensions_width_height_depth_m": [high[i] - low[i] for i in range(3)],
            "feet_on_ground": True, "expected_base_height_m": base_height,
            "height_tolerance_including_hair_m": .10,
            "materials": names, "tint_materials_used": sorted(TINT_MATERIALS),
            "embedded_images": images, "external_dependencies": 0,
            "vertex_color_primitives": vertex_color_primitives, "uv_primitives": uv_primitives,
            "finite_positions_normals_uv_colors": True, "valid_buffer_and_accessor_ranges": True,
            "valid_triangle_index_ranges": True, "uv_range": [uv_min, uv_max] if uv_primitives else None,
            "geometry_sha256": geometry_signature(points),
            "hair_geometry_sha256": geometry_signature(hair_points),
            "head_geometry_normalized_sha256": geometry_signature(head_points, base_height),
            "head_width_over_character_height": (head_high[0] - head_low[0]) / base_height,
            "blend_source_present": True, "blend_source_opened": False}


def validate_manifest(root):
    manifest = json.loads((root / "manifest.json").read_text(encoding="utf-8-sig"))
    require(manifest.get("schemaVersion", manifest.get("version")) == 1, "Expected manifest version 1")
    races = manifest.get("races", [])
    require(len(races) == 2 and {race["id"] for race in races} == set(RACES), "Expected Human and Elf races")
    assets = []
    for race in races:
        race_id = race["id"]
        require(abs(race["baseHeightCm"] / 100 - RACES[race_id]) < 1e-6, "Unexpected base height")
        height = race["height"]
        require(all(isinstance(height.get(key), (int, float)) and math.isfinite(height[key])
                    for key in ("min", "max", "default")), "Invalid manifest height values")
        require(0 < height["min"] <= height["default"] <= height["max"] and height["min"] < height["max"],
                "Invalid height selection range")
        models = race["models"]
        require(set(models) == set(SILHOUETTES), "Expected silhouettes a and b")
        for silhouette in SILHOUETTES:
            require(set(models[silhouette]) == set(HAIR_STYLES), "Expected short, long and braid hairstyles")
            for hair in HAIR_STYLES:
                relative = models[silhouette][hair]
                require(relative == f"models/{race_id}-{silhouette}-{hair}.glb", "Unexpected model mapping")
                assets.append((race_id, silhouette, hair, relative))
    palettes = manifest.get("palettes", {})
    for key in ("skin", "hair", "eyes", "outfit"):
        choices = palettes.get(key, [])
        require(len(choices) >= 2, f"Palette {key} needs at least two choices")
        require(len({choice["id"] for choice in choices}) == len(choices), f"Duplicate {key} choice IDs")
        require(all(re.fullmatch(r"#[0-9a-fA-F]{6}", choice.get("hex", ""))
                    and choice.get("label") for choice in choices), f"Invalid {key} palette")
    return manifest, assets


def cross_checks(results):
    lookup = {item["id"]: item for item in results}
    for race in RACES:
        for silhouette in SILHOUETTES:
            group = [lookup[f"{race}-{silhouette}-{hair}"] for hair in HAIR_STYLES]
            require(len({item["hair_geometry_sha256"] for item in group}) == len(HAIR_STYLES),
                    f"{race}-{silhouette}: hairstyles reuse identical geometry")
    comparisons = []
    for silhouette in SILHOUETTES:
        for hair in HAIR_STYLES:
            human, elf = (lookup[f"{race}-{silhouette}-{hair}"] for race in RACES)
            require(human["head_geometry_normalized_sha256"] != elf["head_geometry_normalized_sha256"],
                    f"{silhouette}-{hair}: races have identical normalized head geometry")
            require(elf["head_width_over_character_height"] > human["head_width_over_character_height"] + .005,
                    f"{silhouette}-{hair}: elf ear/head silhouette must extend farther laterally")
            comparisons.append({"silhouette": silhouette, "hair": hair,
                                "different_normalized_head_geometry": True,
                                "elf_head_lateral_extension_greater": True})
    return {"distinct_hair_geometry_per_style": True, "race_head_and_ear_comparisons": comparisons}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=ROOT, help="Character library directory")
    args = parser.parse_args()
    root = args.root.resolve()
    report = {"success": False, "checked_at_utc": datetime.now(timezone.utc).isoformat(),
              "family": "personnages", "coordinate_system": "glTF: X width, Y up, Z depth; metres",
              "scope": "Static GLB, manifest and Blender source presence checks only",
              "blender_sources_opened": False, "browser_checked": False, "unity_checked": False,
              "assets": [], "errors": []}
    try:
        manifest, expected = validate_manifest(root)
        for race, silhouette, hair, relative in expected:
            try:
                result = validate_asset(root, race, silhouette, hair, relative)
                report["assets"].append(result)
                print("CHARACTER_VERIFIED", result["id"], result["triangles"], "triangles", flush=True)
            except (ValueError, KeyError, IndexError, TypeError, OSError, struct.error) as error:
                report["errors"].append(f"{race}-{silhouette}-{hair}: {error}")
        require(not report["errors"], "One or more character assets failed validation")
        report["comparisons"] = cross_checks(report["assets"])
        report["palette_choice_counts"] = {key: len(value) for key, value in manifest["palettes"].items()}
        report["asset_count"] = len(report["assets"])
        report["manifest_paths_valid"] = True
        report["success"] = True
    except (ValueError, KeyError, IndexError, TypeError, OSError, struct.error) as error:
        report["errors"].append(str(error))
    (root / "validation.json").write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    if not report["success"]:
        for error in report["errors"]:
            print("CHARACTER_VALIDATION_FAILED", error, file=sys.stderr)
        return 1
    print("CHARACTERS_VALIDATED", report["asset_count"], flush=True)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
