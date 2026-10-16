"""Prepare the existing self-contained GLBs for the Arkya Unity/glTFast pipeline.

Run with ordinary Python, from any directory. This only writes import-manifest.json
beside this script; source models, Unity Assets and scenes are never modified.
"""
from pathlib import Path
import hashlib
import json
import math
import struct


ROOT = Path(__file__).resolve().parent
REPO = ROOT.parents[3]
RESOURCES = ROOT.parents[1] / 'resources'
SOURCE = RESOURCES / 'structures'
NAMES = {'Snap_Left', 'Snap_Right', 'Snap_Bottom', 'Snap_Top', 'Snap_Front', 'Snap_Back'}


def require(condition, message):
    if not condition:
        raise ValueError(message)


def digest(data):
    return hashlib.sha256(data).hexdigest()


def relative(path):
    return path.relative_to(REPO).as_posix()


def unity_vector(vector):
    return [0.0 if x == 0 else x for x in [-vector[0], vector[1], vector[2]]]


def read_glb(path):
    raw = path.read_bytes()
    magic, version, total = struct.unpack_from('<4sII', raw)
    require(magic == b'glTF' and version == 2 and total == len(raw), f'Invalid GLB: {path}')
    offset, data, binary = 12, None, None
    while offset < total:
        size, kind = struct.unpack_from('<II', raw, offset)
        chunk = raw[offset+8:offset+8+size]
        require(len(chunk) == size, f'Truncated chunk: {path}')
        if kind == 0x4E4F534A:
            data = json.loads(chunk)
        elif kind == 0x004E4942:
            binary = chunk
        offset += 8+size
    require(offset == total and data is not None and binary is not None, f'Missing GLB chunks: {path}')
    return raw, data, binary


def material_records(data, binary):
    records = []
    for index, material in enumerate(data.get('materials', [])):
        pbr = material.get('pbrMetallicRoughness', {})
        require('baseColorTexture' in pbr, 'Expected embedded painted base-color texture')
        texture = data['textures'][pbr['baseColorTexture']['index']]
        image = data['images'][texture['source']]
        require('uri' not in image and 'bufferView' in image, 'External image dependency')
        view = data['bufferViews'][image['bufferView']]
        start = view.get('byteOffset', 0)
        pixels = binary[start:start+view['byteLength']]
        require(pixels[:8] == b'\x89PNG\r\n\x1a\n', 'Expected packed PNG')
        width, height = struct.unpack_from('>II', pixels, 16)
        name = material['name']
        key = name.split('|')[1].strip()
        source_texture = SOURCE/'textures'/(key+'-basecolor.png')
        require(source_texture.is_file(), f'Missing original texture: {key}')
        roughness = pbr.get('roughnessFactor', 1.)
        records.append({
            'index': index, 'name': name, 'key': key,
            'metallic': pbr.get('metallicFactor', 1.), 'roughness': roughness,
            'urp_smoothness_equivalent': round(1-roughness, 6),
            'base_color_factor': pbr.get('baseColorFactor', [1., 1., 1., 1.]),
            'texture': relative(source_texture),
            'texture_source_sha256': digest(source_texture.read_bytes()),
            'texture_embedded_sha256': digest(pixels), 'texture_size': [width, height],
            'alpha_mode': material.get('alphaMode', 'OPAQUE'),
            'double_sided': material.get('doubleSided', False),
        })
    require(1 <= len(records) <= 3, 'Material budget exceeded')
    return records


def prepare_asset(asset):
    path = SOURCE/asset['glb']
    raw, data, binary = read_glb(path)
    nodes = data['nodes']
    roots = data['scenes'][data.get('scene', 0)]['nodes']
    require(len(roots) == 1, f'Unexpected scene roots: {asset["id"]}')
    root = nodes[roots[0]]
    require(root.get('extras', {}).get('arkya_pivot') == 'bottom_center', 'Missing root pivot contract')
    for node in nodes:
        require('pivot' not in node.get('extras', {}), 'Reserved extras.pivot must not be exported')
        require('matrix' not in node, 'Unexpected matrix transform')
        require(node.get('scale', [1, 1, 1]) == [1, 1, 1], 'Unexpected scale')
        require(node.get('rotation', [0, 0, 0, 1]) == [0, 0, 0, 1], 'Unexpected rotation')
    require(root.get('translation', [0, 0, 0]) == [0, 0, 0], 'Root pivot moved')
    by_name = {n['name']: n for n in nodes if n.get('name', '').startswith('Snap_')}
    require(set(by_name) == NAMES, f'Missing/duplicate snap nodes: {asset["id"]}')
    snap_records = []
    for point in asset['snap_points']:
        name, node = point['name'], by_name[point['name']]
        position = node.get('translation', [0, 0, 0])
        require(all(math.isfinite(n) for n in position), 'Non-finite snap position')
        require(max(abs(a-b) for a, b in zip(position, point['position_m'])) < 1e-5,
                f'Snap manifest differs from model: {name}')
        # Reflecting X exchanges the semantic Left/Right of this symmetric module.
        canonical_name = {'Snap_Left': 'Snap_Right', 'Snap_Right': 'Snap_Left'}.get(name, name)
        snap_records.append({
            'source_name': name, 'gltf_position_m': position,
            'imported_unity_position_m': unity_vector(position),
            'imported_unity_normal': unity_vector(point['normal']),
            'canonical_unity_name': canonical_name,
        })
    return {
        'id': asset['id'], 'shape': asset['shape'], 'material': asset['material'], 'label': asset['label'],
        'source': path.relative_to(RESOURCES).as_posix(), 'source_repo': relative(path),
        'sha256': digest(raw), 'bytes': len(raw), 'format': 'glb', 'embedded_textures': True,
        'nominal_dimensions': asset['nominal_dimensions'], 'opening': asset.get('opening'),
        'root_name': root['name'], 'mesh_name': next(n['name'] for n in nodes if 'mesh' in n),
        'pivot_unity_m': [0, 0, 0], 'triangles': asset['triangles'],
        'snap_points': snap_records, 'materials': material_records(data, binary),
    }


def main():
    manifest = json.loads((SOURCE/'manifest.json').read_text(encoding='utf-8-sig'))
    require(len(manifest['assets']) == 15, 'Expected the full 5 × 3 kit')
    project = REPO/'Unity3D/Arkya'
    packages = json.loads((project/'Packages/manifest.json').read_text(encoding='utf-8-sig'))
    require(packages['dependencies'].get('com.unity.cloud.gltfast') == '6.20.0',
            'Recheck axis conversion before using a different glTFast version')
    report = {
        'format': 'glb', 'conversion_required': False, 'unity_project': relative(project),
        'gltfast_version': '6.20.0', 'family_artistic_status': manifest['artistic_status'],
        'coordinate_contract': {
            'gltf_to_unity_position_and_normal': '(-x, y, z)', 'vertical_axis': 'Y', 'units': 'metres',
            'model_scale': [1, 1, 1], 'pivot': 'bottom_center',
            'snap_normalization': 'Rename imported Snap_Left to Snap_Right and imported Snap_Right to Snap_Left.',
            'snap_rotation': 'Orient each prefab snap Transform from its converted outward normal; source empty rotations are identity.',
            'evidence': [
                'Unity3D/Arkya/Library/PackageCache/com.unity.cloud.gltfast@11ddc2436f97/Runtime/Scripts/NodeExtension.cs',
                'Unity3D/Arkya/Library/PackageCache/com.unity.cloud.gltfast@11ddc2436f97/Runtime/Scripts/CachedFunction.cs',
            ],
        },
        'assets': [prepare_asset(a) for a in manifest['assets']],
    }
    target = ROOT/'import-manifest.json'
    target.write_text(json.dumps(report, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
    print(f'Prepared {len(report["assets"])} self-contained GLBs: {target}')


if __name__ == '__main__':
    main()
