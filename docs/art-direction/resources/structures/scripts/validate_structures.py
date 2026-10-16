"""Read-only geometry checks for the first modular building kit (Python + NumPy).

Run from any directory: python path/to/validate_structures.py
Include editable source checks with Blender: blender --background --python
path/to/validate_structures.py -- --blend
The only file written is structures/validation.json; no source asset is changed.
"""

from pathlib import Path
import hashlib
import json
import struct
import sys

import numpy as np


ROOT = Path(__file__).resolve().parents[1]
DIMENSIONS = {
    "fondation": (1.0, 0.5, 1.0),
    "mur-plein": (1.0, 1.0, 0.15),
    "mur-fenetre": (1.0, 1.0, 0.15),
    "mur-porte": (1.0, 2.0, 0.15),
    "plafond": (1.0, 0.1, 1.0),
}
MATERIALS = {"chaume", "bois", "pierre"}
WOOD_TINTS = {"Arkya | bois | painted": (1., .97, .91, 1.),
              "Arkya | timber | painted": (.78, .76, .71, 1.)}
OPENINGS = {
    "mur-fenetre": (-0.25, 0.25, 0.3, 0.8),
    "mur-porte": (-0.36, 0.36, 0.0, 1.85),
}
TOLERANCE = 0.00001
DTYPES = {5120: "i1", 5121: "u1", 5122: "<i2", 5123: "<u2",
          5125: "<u4", 5126: "<f4"}
WIDTHS = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4, "MAT4": 16}


def require(condition, message):
    if not condition:
        raise ValueError(message)


def matrix(node):
    if "matrix" in node:
        value = np.asarray(node["matrix"], dtype=float).reshape(4, 4).T
        require(np.isfinite(value).all(), "Non-finite node matrix")
        return value
    x, y, z, w = node.get("rotation", (0, 0, 0, 1))
    require(abs(x*x + y*y + z*z + w*w - 1) < 0.0001, "Invalid node quaternion")
    value = np.eye(4)
    value[:3, :3] = np.array([
        [1-2*(y*y+z*z), 2*(x*y-z*w), 2*(x*z+y*w)],
        [2*(x*y+z*w), 1-2*(x*x+z*z), 2*(y*z-x*w)],
        [2*(x*z-y*w), 2*(y*z+x*w), 1-2*(x*x+y*y)],
    ]) @ np.diag(node.get("scale", (1, 1, 1)))
    value[:3, 3] = node.get("translation", (0, 0, 0))
    require(np.isfinite(value).all(), "Non-finite node transform")
    return value


class Glb:
    def __init__(self, path):
        self.raw = path.read_bytes()
        require(len(self.raw) >= 28, f"Truncated GLB: {path}")
        magic, version, size = struct.unpack_from("<4sII", self.raw)
        require(magic == b"glTF" and version == 2 and size == len(self.raw), "Invalid GLB header")
        offset = 12
        chunks = {}
        while offset < size:
            length, kind = struct.unpack_from("<II", self.raw, offset)
            require(offset+8+length <= size, "GLB chunk exceeds file")
            require(kind not in chunks, "Duplicate GLB chunk")
            chunks[kind] = memoryview(self.raw)[offset+8:offset+8+length]
            offset += 8+length
        require(offset == size and 0x4E4F534A in chunks and 0x004E4942 in chunks, "Missing GLB chunks")
        self.data = json.loads(bytes(chunks[0x4E4F534A]))
        self.binary = chunks[0x004E4942]
        require(len(self.data.get("buffers", [])) == 1, "Expected one embedded buffer")
        require(not self.data["buffers"][0].get("uri"), "External buffer dependency")
        require(0 <= len(self.binary)-self.data["buffers"][0]["byteLength"] <= 3, "Invalid binary length")
        for view in self.data.get("bufferViews", []):
            require(view.get("buffer", 0) == 0, "Unexpected buffer")
            require(0 <= view.get("byteOffset", 0) and view["byteLength"] > 0, "Invalid buffer view")
            require(view.get("byteOffset", 0)+view["byteLength"] <= len(self.binary), "Buffer view exceeds binary")

    def accessor(self, index):
        item = self.data["accessors"][index]
        require("sparse" not in item and "bufferView" in item, "Sparse/accessor-only data unsupported")
        view = self.data["bufferViews"][item["bufferView"]]
        dtype = np.dtype(DTYPES[item["componentType"]])
        width = WIDTHS[item["type"]]
        stride = view.get("byteStride", width*dtype.itemsize)
        relative_offset = item.get("byteOffset", 0)
        require(item["count"] > 0 and stride >= width*dtype.itemsize, "Invalid accessor layout")
        used = relative_offset + (item["count"]-1)*stride + width*dtype.itemsize
        require(relative_offset >= 0 and used <= view["byteLength"], "Accessor exceeds view")
        array = np.ndarray((item["count"], width), dtype=dtype, buffer=self.binary,
                           offset=view.get("byteOffset", 0)+relative_offset,
                           strides=(stride, dtype.itemsize)).copy()
        if item.get("normalized", False):
            limit = np.iinfo(dtype)
            array = array.astype(float)/limit.max
            if limit.min < 0:
                array = np.maximum(array, -1)
        require(np.isfinite(array).all(), "Non-finite accessor data")
        return array


def axis_hits(triangles, axis, point):
    """Intersect an infinite axis-aligned line with actual triangle surfaces."""
    other = [i for i in range(3) if i != axis]
    a = triangles[:, 0, other]
    u = triangles[:, 1, other]-a
    v = triangles[:, 2, other]-a
    q = np.asarray(point)-a
    determinant = u[:, 0]*v[:, 1]-u[:, 1]*v[:, 0]
    usable = np.abs(determinant) > 1e-13
    inverse = np.zeros_like(determinant)
    inverse[usable] = 1/determinant[usable]
    beta = (q[:, 0]*v[:, 1]-q[:, 1]*v[:, 0])*inverse
    gamma = (u[:, 0]*q[:, 1]-u[:, 1]*q[:, 0])*inverse
    hit = usable & (beta >= -1e-8) & (gamma >= -1e-8) & (beta+gamma <= 1+1e-8)
    coordinates = (triangles[:, 0, axis]
                   + beta*(triangles[:, 1, axis]-triangles[:, 0, axis])
                   + gamma*(triangles[:, 2, axis]-triangles[:, 0, axis]))
    return coordinates[hit]


def clip_half_plane(polygon, axis, boundary, keep_greater):
    if not polygon:
        return []
    result = []
    previous = polygon[-1]
    previous_inside = previous[axis] >= boundary if keep_greater else previous[axis] <= boundary
    for current in polygon:
        current_inside = current[axis] >= boundary if keep_greater else current[axis] <= boundary
        if current_inside != previous_inside:
            fraction = (boundary-previous[axis])/(current[axis]-previous[axis])
            result.append(previous+fraction*(current-previous))
        if current_inside:
            result.append(current)
        previous, previous_inside = current, current_inside
    return result


def opening_check(triangles, shape):
    if shape not in OPENINGS:
        return None
    left, right, bottom, top = OPENINGS[shape]
    inner = (left+TOLERANCE, right-TOLERANCE, bottom+TOLERANCE, top-TOLERANCE)
    projected = triangles[:, :, :2]
    low, high = projected.min(axis=1), projected.max(axis=1)
    candidates = ((high[:, 0] >= inner[0]) & (low[:, 0] <= inner[1])
                  & (high[:, 1] >= inner[2]) & (low[:, 1] <= inner[3]))
    for triangle in projected[candidates]:
        polygon = list(triangle)
        for axis, boundary, greater in [(0, inner[0], True), (0, inner[1], False),
                                        (1, inner[2], True), (1, inner[3], False)]:
            polygon = clip_half_plane(polygon, axis, boundary, greater)
        require(not polygon, f"Triangle obstructs {shape} opening")
    center_y = (bottom+top)/2
    probes = [((left-.5)/2, center_y), ((right+.5)/2, center_y), (0, (top+DIMENSIONS[shape][1])/2)]
    if bottom > 0:
        probes.append((0, bottom/2))
    groove_adjustments = 0
    def wall_at(x, y):
        hits = axis_hits(triangles, 2, (x, y))
        return len(hits) >= 2 and hits.min() < 0 and hits.max() > 0
    for x, y in probes:
        if wall_at(x, y):
            continue
        require(any(wall_at(a, b) for a, b in [(x-.006, y), (x+.006, y), (x, y-.006), (x, y+.006)]),
                f"Missing front/back wall around opening at {(x, y)}")
        groove_adjustments += 1
    return {"bounds_xy_m": [left, right, bottom, top],
            "clear_width_m": right-left, "clear_height_m": top-bottom,
            "triangle_projection_clipping": True, "obstructing_triangles": 0,
            "boundary_tolerance_m": TOLERANCE, "surrounding_wall_ray_probes": len(probes),
            "surrounding_wall_narrow_groove_adjustments": groove_adjustments}


def check_wall_coverage(triangles, shape):
    if not shape.startswith("mur-"):
        return None
    height = DIMENSIONS[shape][1]
    opening = OPENINGS.get(shape)
    checked, narrow_grooves = 0, 0
    for x in np.arange(-.48, .49, .04):
        for y in np.arange(.02, height-.019, .04):
            if opening and opening[0]-TOLERANCE <= x <= opening[1]+TOLERANCE and opening[2]-TOLERANCE <= y <= opening[3]+TOLERANCE:
                continue
            checked += 1
            if len(axis_hits(triangles, 2, (x, y))):
                continue
            nearby = [(x-.006, y), (x+.006, y), (x, y-.006), (x, y+.006)]
            require(any(len(axis_hits(triangles, 2, point)) for point in nearby),
                    f"Unexpected open gap outside intended aperture at x={x:.4f}, y={y:.4f}")
            narrow_grooves += 1
    return {"axis_ray_grid_pitch_m": .04, "outside_aperture_probes": checked,
            "unexpected_wide_gaps": 0, "small_join_grooves_allowed": narrow_grooves,
            "groove_neighborhood_radius_m": .006}


def check_seams(triangles, dimensions, shape=None):
    width, height, depth = dimensions
    probes = []
    if shape == "fondation":
        # Foundations meet at the continuous deck frame; side infills may recede.
        heights = [height-.065+.065*f for f in (.25, .5, .75)]
    else:
        heights = [height*f for f in (.25, .5, .75)]
    for y in heights:
        for z_fraction in (-.25, 0, .25):
            z = depth*z_fraction
            hits = axis_hits(triangles, 0, (y, z))
            require(len(hits) and np.any(np.abs(hits+width/2) <= TOLERANCE)
                    and np.any(np.abs(hits-width/2) <= TOLERANCE),
                    f"Side join plane missing geometry at y={y}, z={z}")
            probes.append([round(y, 6), round(z, 6)])
    return {"planes_x_m": [-width/2, width/2], "surface_ray_probes_yz_m": probes,
            "contact_zone": "upper deck perimeter" if shape == "fondation" else "middle side face",
            "both_planes_reached": True, "tolerance_m": TOLERANCE}


def check_preview(path):
    header = path.read_bytes()[:24]
    require(header[:8] == b"\x89PNG\r\n\x1a\n" and len(header) == 24, "Invalid PNG preview")
    width, height = struct.unpack_from(">II", header, 16)
    require(width > 0 and height > 0, "Empty PNG preview")
    return [width, height]


def check_blend(path, dimensions, triangle_count):
    import bpy

    bpy.ops.wm.open_mainfile(filepath=str(path), use_scripts=False)
    require(not bpy.data.libraries, "Blender source has linked dependencies")
    roots = [obj for obj in bpy.context.scene.objects if obj.name.startswith("Arkya_Structure_") and obj.parent is None]
    require(len(roots) == 1 and roots[0].type == "EMPTY", "Source requires one placement root")
    root = roots[0]
    require(root.get("arkya_pivot") == "bottom_center" and "pivot" not in root,
            "Source root requires namespaced arkya_pivot metadata")
    require(np.allclose(np.asarray(root.matrix_world), np.eye(4), atol=TOLERANCE, rtol=0), "Source root transform is not identity")
    meshes = [obj for obj in root.children_recursive if obj.type == "MESH"]
    require(meshes, "Source contains no mesh under placement root")
    points, source_triangles, materials = [], 0, set()
    for obj in meshes:
        mesh = obj.data
        require(not obj.modifiers, "Source must preserve the applied export geometry")
        require(len(mesh.uv_layers) >= 1, "Source mesh has no UVs")
        positions = np.asarray([obj.matrix_world @ vertex.co for vertex in mesh.vertices])
        require(np.isfinite(positions).all(), "Source has non-finite vertices")
        uv = np.asarray([corner.uv[:] for corner in mesh.uv_layers.active.data])
        require(np.isfinite(uv).all(), "Source UVs are non-finite")
        for polygon in mesh.polygons:
            material = mesh.materials[polygon.material_index]
            face_uv = uv[list(polygon.loop_indices)]
            if material.name not in WOOD_TINTS:
                require(face_uv.min() >= -TOLERANCE and face_uv.max() <= 1+TOLERANCE,
                        "Non-wood source UV outside painted atlas")
        for material in mesh.materials:
            if material and material.name in WOOD_TINTS:
                shader = material.node_tree.nodes.get('Principled BSDF')
                tint = material.node_tree.nodes.get('Arkya worked timber tint')
                require(shader and abs(shader.inputs['Roughness'].default_value-.96) <= TOLERANCE,
                        "Source timber roughness differs from V2")
                require(tint and np.allclose(tint.inputs[7].default_value, WOOD_TINTS[material.name], atol=TOLERANCE),
                        "Source timber tint differs from V2")
                require(tuple(material.get('arkya_uv_tile_metres', ())) == (1.2, 2.4),
                        "Source timber metric UV contract missing")
                image_nodes = [node for node in material.node_tree.nodes if node.type == 'TEX_IMAGE']
                require(image_nodes and all(node.extension == 'REPEAT' for node in image_nodes),
                        "Repeated timber UVs need repeat image sampling")
        points.append(positions)
        mesh.calc_loop_triangles()
        source_triangles += len(mesh.loop_triangles)
        materials.update(material.name for material in mesh.materials if material)
    require(source_triangles == triangle_count, "Source and GLB triangle counts differ")
    require(len(materials) <= 3, "Source material budget exceeded")
    points = np.concatenate(points)
    expected_low = np.array([-dimensions[0]/2, -dimensions[2]/2, 0])
    expected_high = np.array([dimensions[0]/2, dimensions[2]/2, dimensions[1]])
    require(np.allclose(points.min(axis=0), expected_low, atol=TOLERANCE, rtol=0)
            and np.allclose(points.max(axis=0), expected_high, atol=TOLERANCE, rtol=0), "Source bounds differ from export")
    images = []
    for image in bpy.data.images:
        if image.source in {"FILE", "TILED"}:
            require(image.packed_file or image.packed_files, f"Unpacked source image: {image.name}")
            images.append({"name": image.name, "size_px": list(image.size)})
    require(images, "Blender source contains no packed images")
    return {"opened": True, "scripts_disabled": True, "linked_libraries": 0,
            "mesh_nodes": len(meshes), "triangles": source_triangles,
            "exact_bounds": True, "finite_geometry_and_uv": True, "packed_images": images}


def check_asset(asset, verify_blend=False):
    asset_id = asset["id"]
    dimensions = DIMENSIONS[asset["shape"]]
    paths = {}
    for key in ("glb", "blend", "preview"):
        path = (ROOT/asset[key]).resolve()
        require(path.is_relative_to(ROOT) and path.is_file(), f"Missing/outside-root {key}: {asset_id}")
        paths[key] = path
    with paths["blend"].open("rb") as source:
        source_magic = source.read(12)
    require(source_magic.startswith((b"BLENDER", b"\x1f\x8b", b"\x28\xb5\x2f\xfd")), "Invalid Blender source signature")
    preview_size = check_preview(paths["preview"])
    glb = Glb(paths["glb"])
    data = glb.data
    require(not data.get("cameras") and not data.get("animations"), "Building export includes camera/animation")
    require("KHR_lights_punctual" not in data.get("extensions", {}), "Export includes lights")
    require(1 <= len(data.get("materials", [])) <= 3, "Material budget exceeded")
    require(data.get("images"), "No embedded painted images")
    for image in data["images"]:
        require(not image.get("uri") and "bufferView" in image, "Texture is not embedded")
        require(image.get("mimeType") in ("image/png", "image/jpeg"), "Unsupported embedded image")
    for material in data["materials"]:
        require(material.get("alphaMode", "OPAQUE") == "OPAQUE", "Structure requires opaque material")
        require("baseColorTexture" in material.get("pbrMetallicRoughness", {}), "Material lacks painted base texture")
        if material.get("name") in WOOD_TINTS:
            pbr = material['pbrMetallicRoughness']
            require(np.allclose(pbr.get('baseColorFactor', [1, 1, 1, 1]), WOOD_TINTS[material['name']], atol=TOLERANCE),
                    "Exported timber tint differs from V2")
            require(abs(pbr.get('roughnessFactor', 1)-.96) <= TOLERANCE, "Exported timber roughness differs from V2")
            texture = data['textures'][pbr['baseColorTexture']['index']]
            sampler = data.get('samplers', [])[texture['sampler']] if 'sampler' in texture else {}
            require(sampler.get('wrapS', 10497) == 10497 and sampler.get('wrapT', 10497) == 10497,
                    "Repeated timber UVs need repeat sampling")
            image = data['images'][texture['source']]
            view = data['bufferViews'][image['bufferView']]
            image_bytes = glb.binary[view.get('byteOffset', 0):view.get('byteOffset', 0)+view['byteLength']]
            require(hashlib.sha256(image_bytes).digest() == hashlib.sha256((ROOT/'sources/worked-timber-painted-v2.png').read_bytes()).digest(),
                    "Exported timber texture differs from native V2 image")
    scene = data["scenes"][data.get("scene", 0)]
    require(len(scene["nodes"]) == 1, "Expected one placement root")
    root_index = scene["nodes"][0]
    root = data["nodes"][root_index]
    require(root.get("name", "").startswith("Arkya_Structure_"), "Unexpected root name")
    require(root.get("extras", {}).get("arkya_pivot") == "bottom_center",
            "Root requires namespaced arkya_pivot metadata")
    require("mesh" not in root and np.allclose(matrix(root), np.eye(4), atol=TOLERANCE, rtol=0),
            "Placement root must be empty with identity transform")
    expected_markers = {
        "Snap_Left": [-.5, dimensions[1]/2, 0],
        "Snap_Right": [.5, dimensions[1]/2, 0],
        "Snap_Bottom": [0, 0, 0],
        "Snap_Top": [0, dimensions[1], 0],
        "Snap_Front": [0, dimensions[1]/2, dimensions[2]/2],
        "Snap_Back": [0, dimensions[1]/2, -dimensions[2]/2],
    }
    expected_normals = {"Snap_Left": [-1, 0, 0], "Snap_Right": [1, 0, 0],
                        "Snap_Bottom": [0, -1, 0], "Snap_Top": [0, 1, 0],
                        "Snap_Front": [0, 0, 1], "Snap_Back": [0, 0, -1]}
    triangles, points, seen, markers = [], [], set(), {}
    geometry_hash = hashlib.sha256()
    uv_min, uv_max = np.inf, -np.inf
    mesh_nodes = 0

    def visit(index, parent):
        nonlocal mesh_nodes, uv_min, uv_max
        require(index not in seen, "Repeated/cyclic scene node")
        seen.add(index)
        node = data["nodes"][index]
        require("pivot" not in node.get("extras", {}),
                "Reserved pivot extra collides with GLTFLoader placement metadata; use arkya_pivot")
        require("camera" not in node and "KHR_lights_punctual" not in node.get("extensions", {}), "Camera/light node")
        transform = parent @ matrix(node)
        name = node.get("name", "")
        if name in expected_markers:
            require(name not in markers and "mesh" not in node, "Invalid snap empty")
            position = transform[:3, 3]
            require(np.allclose(position, expected_markers[name], atol=TOLERANCE, rtol=0), f"Incorrect {name} position")
            extras = node.get("extras", {})
            require(extras.get("snap_role") == name[5:].lower(), f"Invalid {name} role")
            require("position_m" in extras and "normal" in extras, f"Missing {name} placement metadata")
            require(np.allclose(extras["position_m"], position, atol=TOLERANCE, rtol=0), f"Incorrect {name} position metadata")
            require(np.allclose(extras["normal"], expected_normals[name], atol=TOLERANCE, rtol=0), f"Incorrect {name} outward normal")
            markers[name] = position.tolist()
        if "mesh" in node:
            mesh_nodes += 1
            require(np.allclose(transform, np.eye(4), atol=TOLERANCE, rtol=0), "Mesh transform must be applied")
            for primitive in data["meshes"][node["mesh"]]["primitives"]:
                require(primitive.get("mode", 4) == 4, "Non-triangle primitive")
                attrs = primitive["attributes"]
                require(all(k in attrs for k in ("POSITION", "NORMAL", "TEXCOORD_0")), "Missing positions/normals/UVs")
                positions = glb.accessor(attrs["POSITION"])
                normals = glb.accessor(attrs["NORMAL"])
                uv = glb.accessor(attrs["TEXCOORD_0"])
                require(positions.shape[1] == 3 and normals.shape == positions.shape and uv.shape == (len(positions), 2), "Attribute shape mismatch")
                require(np.allclose(np.linalg.norm(normals, axis=1), 1, atol=.002, rtol=0), "Invalid surface normals")
                require(np.isfinite(uv).all(), "Non-finite exported UVs")
                material_name = data['materials'][primitive['material']].get('name')
                if material_name not in WOOD_TINTS:
                    require(uv.min() >= -TOLERANCE and uv.max() <= 1+TOLERANCE, "Non-wood UV outside painted atlas")
                uv_min, uv_max = min(uv_min, float(uv.min())), max(uv_max, float(uv.max()))
                index_data = glb.accessor(primitive["indices"]).ravel() if "indices" in primitive else np.arange(len(positions))
                require(len(index_data)%3 == 0 and index_data.min() >= 0 and index_data.max() < len(positions), "Invalid triangle indices")
                require(np.issubdtype(index_data.dtype, np.integer), "Non-integer triangle indices")
                transformed = positions @ transform[:3, :3].T + transform[:3, 3]
                faces = transformed[index_data.reshape(-1, 3)]
                area2 = np.linalg.norm(np.cross(faces[:, 1]-faces[:, 0], faces[:, 2]-faces[:, 0]), axis=1)
                require(np.all(area2 > 1e-12), "Degenerate triangle")
                points.append(transformed)
                triangles.append(faces)
                geometry_hash.update(transformed.astype("<f4").tobytes())
                geometry_hash.update(index_data.astype("<u4").tobytes())
        for child in node.get("children", []):
            visit(child, transform)

    visit(root_index, np.eye(4))
    require(len(seen) == len(data["nodes"]), "Orphan scene nodes")
    require(set(markers) == set(expected_markers), "Missing snap markers")
    require(mesh_nodes >= 1 and points, "No rendered mesh")
    triangles = np.concatenate(triangles)
    require(0 < len(triangles) < 20000, "Triangle budget exceeded")
    if "triangles" in asset:
        require(len(triangles) == asset["triangles"], "Manifest triangle count differs from mesh")
    points = np.concatenate(points)
    low, high = points.min(axis=0), points.max(axis=0)
    expected_low = np.array([-dimensions[0]/2, 0, -dimensions[2]/2])
    expected_high = expected_low+dimensions
    require(np.allclose(low, expected_low, atol=TOLERANCE, rtol=0)
            and np.allclose(high, expected_high, atol=TOLERANCE, rtol=0),
            f"Wrong bounds: {low.tolist()} .. {high.tolist()}; expected {dimensions}")
    for index, key in enumerate(("width", "height", "depth")):
        if key in asset:
            require(abs(asset[key]-dimensions[index]) <= TOLERANCE, f"Wrong manifest {key}")
    return {"id": asset_id, "shape": asset["shape"], "material": asset["material"],
            "paths": {key: asset[key] for key in paths},
            "dimensions_width_height_depth_m": (high-low).tolist(),
            "bounds_min_m": low.tolist(), "bounds_max_m": high.tolist(),
            "bottom_center_pivot": True, "identity_root_and_mesh_transforms": True,
            "placement_metadata_contract": {"arkya_pivot": "bottom_center", "reserved_pivot_extra_absent": True},
            "triangles": len(triangles), "mesh_nodes": mesh_nodes,
            "materials": len(data["materials"]), "embedded_images": len(data["images"]),
            "external_dependencies": 0, "geometry_sha256": geometry_hash.hexdigest(),
            "finite_geometry_and_uv": True, "uv_range": [uv_min, uv_max],
            "uv_contract": "Wood/timber use metric repeat at 1.2 x 2.4 m; other materials stay in the 0..1 painted atlas",
            "snap_markers_positions_m": markers, "snap_marker_roles_and_normals_valid": True,
            "side_seams": check_seams(triangles, dimensions, asset["shape"]),
            "opening": opening_check(triangles, asset["shape"]),
            "wall_coverage": check_wall_coverage(triangles, asset["shape"]),
            "blend_source_present": True,
            "blend": check_blend(paths["blend"], dimensions, len(triangles)) if verify_blend else {"opened": False},
            "preview_size_px": preview_size, "glb_bytes": len(glb.raw)}


def check_approved_references():
    results = []
    for relative, manifest_name in [("references/arbre-peint-v3", "reference.json"),
                                     ("mares-de-mana", "approved-reference.json")]:
        folder = ROOT.parent/relative
        manifest = json.loads((folder/manifest_name).read_text(encoding="utf-8-sig"))
        for file in manifest["files"]:
            path = (folder/file["file"]).resolve()
            require(path.is_relative_to(folder.resolve()), "Reference path outside frozen directory")
            actual = hashlib.sha256(path.read_bytes()).hexdigest()
            require(actual.lower() == file["sha256"].lower(), f"Approved reference changed: {path}")
        results.append({"reference": relative, "hash_manifest": manifest_name,
                        "files_checked": len(manifest["files"]), "unchanged": True})
    return results


def check_provenance():
    provenance = json.loads((ROOT/"provenance.json").read_text(encoding="utf-8-sig"))
    results = []
    for source in provenance["sources"]:
        path = (ROOT/source["file"]).resolve()
        require(path.is_relative_to(ROOT.parent.resolve()), "Paint source is outside resource library")
        actual = hashlib.sha256(path.read_bytes()).hexdigest()
        require(actual.lower() == source["sha256"].lower(), "Paint source hash differs from provenance")
        prompt = (ROOT/source["prompt"]).resolve()
        require(prompt.is_relative_to(ROOT.parent.resolve()) and prompt.is_file(), "Paint source prompt missing")
        for relative in source["used_by"]:
            copy = (ROOT/relative).resolve()
            require(copy.is_relative_to(ROOT.resolve()), "Texture copy is outside structure library")
            require(hashlib.sha256(copy.read_bytes()).hexdigest() == actual, f"Paint source copy changed: {relative}")
        results.append({"source": source["file"], "sha256": actual,
                        "prompt_present": True, "identical_copies_checked": source["used_by"]})
    return results


def main():
    verify_blend = "--blend" in sys.argv
    manifest = json.loads((ROOT/"manifest.json").read_text(encoding="utf-8-sig"))
    expected = {(shape, material) for shape in DIMENSIONS for material in MATERIALS}
    actual = {(item["shape"], item["material"]) for item in manifest["assets"]}
    require(len(manifest["assets"]) == 15 and actual == expected, "Expected five shapes in three materials")
    require(len({item["id"] for item in manifest["assets"]}) == 15, "Duplicate asset IDs")
    results = []
    for asset in manifest["assets"]:
        try:
            results.append(check_asset(asset, verify_blend=verify_blend))
        except (ValueError, KeyError, IndexError) as error:
            raise ValueError(f"{asset['id']}: {error}") from error
        print("STRUCTURE_VERIFIED", asset["id"], flush=True)
    for shape in DIMENSIONS:
        variants = [item for item in results if item["shape"] == shape]
        require(len({item["geometry_sha256"] for item in variants}) == 3,
                f"{shape}: materials should have their own construction geometry")
    comparison = ROOT/"comparaison-tailles.png"
    require(comparison.is_file(), "Missing common-scale comparison preview")
    report = {"success": True, "family": "structures", "asset_count": 15,
              "coordinate_system": "glTF: X width, Y up, Z depth; metres",
              "geometry_differs_across_materials": True,
              "exact_envelopes_and_bottom_center_pivots": True,
              "placement_metadata_contract": "Root extras.arkya_pivot=bottom_center; reserved node extras.pivot absent",
              "openings_checked_from_triangles": True,
              "comparison_preview_size_px": check_preview(comparison),
              "blender_sources_opened_and_packed": verify_blend,
              "paint_source_provenance": check_provenance(),
              "approved_reference_preservation": check_approved_references(), "assets": results}
    (ROOT/"validation.json").write_text(json.dumps(report, ensure_ascii=False, indent=2)+"\n", encoding="utf-8")
    print("STRUCTURES_VALIDATED", len(results), flush=True)


if __name__ == "__main__":
    main()
