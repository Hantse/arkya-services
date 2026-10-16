"""Validate the twelve standalone ore-rock deliverables with Blender in background."""
import hashlib
import json
from pathlib import Path
import struct
from array import array

import bpy

ROOT = Path(__file__).resolve().parent
manifest = json.loads((ROOT / 'manifest.json').read_text(encoding='utf-8'))
assert len(manifest['assets']) == 12
assert len({(a['shape'], a['material']) for a in manifest['assets']}) == 12
results = []
shape_geometry = {}
for asset in manifest['assets']:
    for key in ('glb', 'blend', 'preview'):
        assert (ROOT / asset[key]).is_file(), (asset['id'], key)
    blob = (ROOT / asset['glb']).read_bytes()
    length, kind = struct.unpack_from('<II', blob, 12)
    assert kind == 0x4E4F534A
    gltf = json.loads(blob[20:20+length])
    assert not any('uri' in item for item in gltf.get('images', []) + gltf.get('buffers', []))
    assert len(gltf['images']) == 3
    triangles = 0
    for mesh in gltf['meshes']:
        for primitive in mesh['primitives']:
            assert {'POSITION', 'NORMAL', 'TEXCOORD_0'} <= set(primitive['attributes'])
            triangles += gltf['accessors'][primitive['indices']]['count'] // 3
    assert triangles == asset['triangles'] and triangles < 20000
    pbr = gltf['materials'][0]['pbrMetallicRoughness']
    assert 'baseColorTexture' in pbr and 'metallicRoughnessTexture' in pbr
    assert 'normalTexture' in gltf['materials'][0]
    assert hashlib.sha256(blob).hexdigest() == asset['sha256']
    for channel in ('basecolor', 'normal', 'roughness', 'metallic'):
        path = ROOT / asset['textures'] / (channel + '.png')
        png = path.read_bytes()
        assert png[:8] == b'\x89PNG\r\n\x1a\n'
        assert struct.unpack_from('>II', png, 16) == (2048, 2048)
    bpy.ops.wm.open_mainfile(filepath=str(ROOT / asset['blend']))
    objects = [obj for obj in bpy.data.objects if obj.name.startswith('Ore Rock |')]
    assert len(objects) == 1
    obj = objects[0]
    assert obj.location.length < 1e-6
    low = [min(v.co[axis] for v in obj.data.vertices) for axis in range(3)]
    high = [max(v.co[axis] for v in obj.data.vertices) for axis in range(3)]
    assert abs(low[2]) < 1e-6
    assert abs(high[0]+low[0]) < 1e-6 and abs(high[1]+low[1]) < 1e-6
    assert abs(high[2]-asset['height']) < 1e-4
    assert obj.data.uv_layers
    coordinates = array('f', [0]) * (len(obj.data.vertices) * 3)
    obj.data.vertices.foreach_get('co', coordinates)
    indices = array('i', [0]) * len(obj.data.loops)
    obj.data.loops.foreach_get('vertex_index', indices)
    uvs = array('f', [0]) * (len(obj.data.uv_layers.active.data) * 2)
    obj.data.uv_layers.active.data.foreach_get('uv', uvs)
    geometry_hash = hashlib.sha256(coordinates.tobytes() + indices.tobytes() + uvs.tobytes()).hexdigest()
    if asset['shape'] in shape_geometry:
        assert shape_geometry[asset['shape']] == geometry_hash
    shape_geometry[asset['shape']] = geometry_hash
    textures = [node.image for node in obj.active_material.node_tree.nodes if node.type == 'TEX_IMAGE']
    assert len(textures) == 4 and all(image.packed_file for image in textures)
    results.append({'id': asset['id'], 'triangles': triangles, 'height': asset['height'],
                    'portable_pbr': True, 'packed_blend': True, 'grounded_pivot': True,
                    'geometry_and_uv_sha256': geometry_hash})
assert (ROOT / 'comparaison-tailles.png').is_file()
(ROOT / 'validation.json').write_text(json.dumps({'passed': True, 'assets': results}, indent=2), encoding='utf-8')
print('VALIDATED', len(results), 'ore rocks; embedded PBR, packed Blender, 2K maps, centered grounded pivots')
