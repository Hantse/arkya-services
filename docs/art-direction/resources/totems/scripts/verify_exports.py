"""Read delivered GLB buffers independently of Blender and verify the rig contract."""
import hashlib
import json
import math
from pathlib import Path
import re
import struct

ROOT = Path(__file__).resolve().parents[1]
COMPONENTS = {5120: ('b', 1), 5121: ('B', 1), 5122: ('h', 2),
              5123: ('H', 2), 5125: ('I', 4), 5126: ('f', 4)}
WIDTHS = {'SCALAR': 1, 'VEC2': 2, 'VEC3': 3, 'VEC4': 4, 'MAT4': 16}


def read_glb(path):
    raw = path.read_bytes()
    magic, version, length = struct.unpack_from('<4sII', raw)
    assert magic == b'glTF' and version == 2 and length == len(raw), path
    chunks = {}
    offset = 12
    while offset < length:
        size, kind = struct.unpack_from('<II', raw, offset)
        offset += 8
        assert offset + size <= length
        chunks[kind] = raw[offset:offset + size]
        offset += size
    return json.loads(chunks[0x4E4F534A]), chunks[0x004E4942]


def accessor(doc, binary, index):
    spec = doc['accessors'][index]
    assert 'sparse' not in spec, 'Sparse accessor requires an explicit validator'
    view = doc['bufferViews'][spec['bufferView']]
    kind, size = COMPONENTS[spec['componentType']]
    width = WIDTHS[spec['type']]
    stride = view.get('byteStride', size * width)
    offset = view.get('byteOffset', 0) + spec.get('byteOffset', 0)
    assert view.get('buffer', 0) == 0
    assert offset + (spec['count'] - 1) * stride + size * width <= len(binary)
    rows = [struct.unpack_from('<' + kind * width, binary, offset + i * stride)
            for i in range(spec['count'])]
    if spec.get('normalized') and spec['componentType'] != 5126:
        max_value = {5120: 127, 5121: 255, 5122: 32767, 5123: 65535}[spec['componentType']]
        rows = [tuple(max(-1, x / max_value) for x in row) for row in rows]
    assert all(math.isfinite(x) for row in rows for x in row)
    return rows


def check_file(path):
    doc, binary = read_glb(path)
    assert doc.get('skins'), f'{path.name}: missing skin'
    assert all('uri' not in b for b in doc['buffers'])
    assert doc.get('images') and all('bufferView' in i for i in doc['images'])
    image_hashes = []
    for image in doc['images']:
        view = doc['bufferViews'][image['bufferView']]
        start = view.get('byteOffset', 0)
        image_hashes.append(hashlib.sha256(binary[start:start + view['byteLength']]).hexdigest())
    cached = {}

    def data(index):
        if index not in cached:
            cached[index] = accessor(doc, binary, index)
        return cached[index]

    geometry_hash = hashlib.sha256()
    triangle_count = vertex_count = skinned_vertices = 0
    min_coord = [math.inf] * 3
    max_coord = [-math.inf] * 3
    max_weight_error = 0
    max_influences = 0
    for node in doc['nodes']:
        if 'mesh' not in node:
            continue
        assert 'skin' in node, f'{path.name}: unskinned mesh {node.get("name")}'
        skin = doc['skins'][node['skin']]
        assert len(skin['joints']) >= 20
        matrices = data(skin['inverseBindMatrices'])
        assert len(matrices) == len(skin['joints'])
        for primitive in doc['meshes'][node['mesh']]['primitives']:
            attributes = primitive['attributes']
            assert {'POSITION', 'NORMAL', 'TEXCOORD_0', 'JOINTS_0', 'WEIGHTS_0'} <= attributes.keys()
            positions = data(attributes['POSITION'])
            normals = data(attributes['NORMAL'])
            weights = data(attributes['WEIGHTS_0'])
            joints = data(attributes['JOINTS_0'])
            assert len(positions) == len(normals) == len(weights) == len(joints)
            assert primitive.get('mode', 4) == 4
            indices = [v[0] for v in data(primitive['indices'])]
            assert len(indices) % 3 == 0 and max(indices) < len(positions)
            triangle_count += len(indices) // 3
            vertex_count += len(positions)
            for point, normal, weight, joint in zip(positions, normals, weights, joints):
                assert .9 < sum(x*x for x in normal) < 1.1, 'Non-unit normal'
                assert all(x >= 0 for x in weight)
                error = abs(sum(weight) - 1)
                assert error < .001, f'Invalid skin weight sum {error}'
                assert all(0 <= j < len(skin['joints']) for j, w in zip(joint, weight) if w > 0)
                max_weight_error = max(max_weight_error, error)
                max_influences = max(max_influences, sum(w > 1e-6 for w in weight))
                skinned_vertices += 1
                for axis in range(3):
                    min_coord[axis] = min(min_coord[axis], point[axis])
                    max_coord[axis] = max(max_coord[axis], point[axis])
            for key in ('POSITION', 'NORMAL', 'JOINTS_0', 'WEIGHTS_0'):
                geometry_hash.update(json.dumps(data(attributes[key]), separators=(',', ':')).encode())
            geometry_hash.update(json.dumps(indices, separators=(',', ':')).encode())
    assert vertex_count == skinned_vertices
    animation_reports = []
    names = {a['name'] for a in doc.get('animations', [])}
    assert {'Idle', 'Walk', 'Run'} <= names, names
    for animation in doc['animations']:
        changed_channels = 0
        changed_body_channels = 0
        largest_loop_error = 0
        duration = 0
        for channel in animation['channels']:
            sampler = animation['samplers'][channel['sampler']]
            assert sampler.get('interpolation', 'LINEAR') in ('LINEAR', 'STEP')
            times = [x[0] for x in data(sampler['input'])]
            values = data(sampler['output'])
            assert len(times) == len(values) and len(times) > 1
            assert all(b > a for a, b in zip(times, times[1:]))
            duration = max(duration, times[-1] - times[0])
            error = max(abs(a-b) for a, b in zip(values[0], values[-1]))
            if channel['target']['path'] == 'rotation':
                error = min(error, max(abs(a+b) for a, b in zip(values[0], values[-1])))
            largest_loop_error = max(largest_loop_error, error)
            moves = any(max(abs(a-b) for a, b in zip(values[0], row)) > 1e-4 for row in values[1:])
            changed_channels += moves
            target_name = doc['nodes'][channel['target']['node']].get('name', '')
            if not target_name.startswith('Flow'):
                changed_body_channels += moves
        assert changed_channels > 0
        assert changed_body_channels > 2, 'Only decorative particles move'
        assert largest_loop_error < .001, (path.name, animation['name'], largest_loop_error)
        animation_reports.append({'name': animation['name'], 'durationSeconds': duration,
                                  'movingChannels': changed_channels,
                                  'movingBodyChannels': changed_body_channels,
                                  'maxLoopComponentError': largest_loop_error})
    return {'file': str(path.relative_to(ROOT)).replace('\\', '/'), 'bytes': path.stat().st_size,
            'sha256': hashlib.sha256(path.read_bytes()).hexdigest(),
            'triangles': triangle_count, 'vertices': vertex_count,
            'skinCount': len(doc['skins']), 'bones': max(len(s['joints']) for s in doc['skins']),
            'maxInfluences': max_influences, 'maxWeightSumError': max_weight_error,
            'materialCount': len(doc.get('materials', [])), 'embeddedImages': len(doc['images']),
            'embeddedImageSha256': image_hashes,
            'localMeshBounds': [min_coord, max_coord], 'animations': animation_reports,
            'geometrySha256': geometry_hash.hexdigest()}


def main():
    manifest = json.loads((ROOT / 'manifest.json').read_text(encoding='utf-8'))
    assert {s['id'] for s in manifest['species']} == {'wolf', 'fox'}
    assert len(manifest['colorways']) == 12
    coat_ids = {c['id'] for c in manifest['colorways']}
    assert len(coat_ids) == 12
    for coat in manifest['colorways']:
        assert len(coat['colors']) == len(set(coat['colors'])) == 12
        assert all(re.fullmatch(r'#[0-9a-fA-F]{6}', color) for color in coat['colors'])
    for species in manifest['species']:
        assert set(species['models']) == coat_ids and set(species['textures']) == coat_ids
        for path in [species['blend'], species['fbx'], *species['models'].values(), *species['textures'].values()]:
            resolved = (ROOT / path).resolve()
            assert resolved.is_relative_to(ROOT) and resolved.is_file() and resolved.stat().st_size > 100, path
    files = sorted((ROOT / 'models').glob('*.glb'))
    assert len(files) == 24, f'Expected 24 GLBs, found {len(files)}'
    reports = [check_file(path) for path in files]
    for species in ('wolf', 'fox'):
        group = [r for r in reports if Path(r['file']).stem.startswith(species + '-')]
        assert len(group) == 12
        assert len({r['geometrySha256'] for r in group}) == 1, f'{species}: colorways changed geometry'
        assert len({tuple(r['embeddedImageSha256']) for r in group}) == 12, f'{species}: duplicate painted coat textures'
    report = {'schemaVersion': 1, 'status': 'passed', 'method': 'Independent GLB binary parsing',
              'checks': ['2 species, 12 colorways, 12 distinct palette hues per colorway and all manifest assets present',
                         '24 self-contained GLBs', 'skin weights and joint indices',
                         'finite geometry, unit normals and valid triangle indices',
                         'Idle/Walk/Run movement and matching loop endpoints',
                         'identical geometry and weights across twelve coats of each animal',
                         'twelve distinct embedded painted textures per animal'],
              'models': reports}
    (ROOT / 'export-verification.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
    print(json.dumps({'status': 'passed', 'models': len(reports),
                      'trianglesBySpecies': {s: next(r['triangles'] for r in reports if Path(r['file']).stem.startswith(s+'-')) for s in ('wolf', 'fox')}}))


if __name__ == '__main__':
    main()
