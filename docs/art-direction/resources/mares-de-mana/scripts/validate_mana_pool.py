"""Validate self-contained mana GLBs, finite geometry and seamless node animation."""
import hashlib
import json
from pathlib import Path
import struct
import bpy
import numpy as np

ROOT = Path(__file__).resolve().parents[1]


def glb_read(path):
    raw = path.read_bytes()
    magic, version, size = struct.unpack_from('<III', raw)
    assert magic == 0x46546C67 and version == 2 and size == len(raw)
    json_size, kind = struct.unpack_from('<II', raw, 12)
    document = json.loads(raw[20:20 + json_size])
    binary_size, binary_kind = struct.unpack_from('<II', raw, 20 + json_size)
    return document, raw[28 + json_size:28 + json_size + binary_size]


def accessor(doc, binary, index):
    entry = doc['accessors'][index]; view = doc['bufferViews'][entry['bufferView']]
    components = {'SCALAR': 1, 'VEC2': 2, 'VEC3': 3, 'VEC4': 4, 'MAT4': 16}[entry['type']]
    dtype = {5120: 'i1', 5121: 'u1', 5122: '<i2', 5123: '<u2', 5125: '<u4', 5126: '<f4'}[entry['componentType']]
    dt = np.dtype(dtype); offset = view.get('byteOffset', 0) + entry.get('byteOffset', 0)
    stride = view.get('byteStride', components * dt.itemsize)
    return np.ndarray((entry['count'], components), dtype=dt, buffer=binary, offset=offset, strides=(stride, dt.itemsize))


def validate_asset(asset):
    doc, binary = glb_read(ROOT / asset['glb'])
    errors = []; geometry_hash = hashlib.sha256(); canonical_hash = hashlib.sha256(); triangles = 0
    for mesh in sorted(doc['meshes'], key=lambda m: m['name']):
        geometry_hash.update(mesh['name'].encode('utf-8'))
        canonical_hash.update(mesh['name'].encode('utf-8'))
        for primitive in mesh['primitives']:
            assert primitive.get('mode', 4) == 4
            triangles += len(accessor(doc, binary, primitive['indices'])) // 3
            for attribute in ('POSITION', 'NORMAL', 'TEXCOORD_0'):
                if attribute not in primitive['attributes']: errors.append('Missing ' + attribute); continue
                values = accessor(doc, binary, primitive['attributes'][attribute])
                if not np.isfinite(values).all(): errors.append('Non-finite ' + attribute)
                if attribute == 'POSITION': geometry_hash.update(values.tobytes())
            geometry_hash.update(accessor(doc, binary, primitive['indices']).tobytes())
            positions = accessor(doc, binary, primitive['attributes']['POSITION'])
            indexed = accessor(doc, binary, primitive['indices']).reshape(-1, 3)
            # Cyclic vertex rotation preserves winding while normalizing each triangle.
            faces = positions[indexed]
            first = np.lexsort((faces[:, :, 2], faces[:, :, 1], faces[:, :, 0]), axis=1)[:, 0]
            order = (first[:, None] + np.arange(3)[None, :]) % 3
            faces = np.take_along_axis(faces, order[:, :, None], axis=1).reshape(-1, 9)
            order = np.lexsort(tuple(faces[:, i] for i in reversed(range(9))))
            canonical_hash.update(faces[order].tobytes())
    if triangles != asset['triangles'] or triangles > 25000: errors.append('Triangle budget or metadata mismatch')
    if len(doc['materials']) > 6: errors.append('Material budget exceeded')
    if sum(m.get('alphaMode') == 'BLEND' for m in doc['materials']) != 2: errors.append('Expected two soft ribbon materials')
    if any('uri' in image or 'bufferView' not in image for image in doc['images']): errors.append('Texture not embedded')
    animations = doc.get('animations', [])
    if len(animations) != 1 or animations[0].get('name') != 'ManaFlow': errors.append('Expected single ManaFlow clip')
    channels = animations[0]['channels']; animated_nodes = set()
    maximum_endpoint_delta = 0; duration = 0
    for channel in channels:
        animated_nodes.add(channel['target']['node'])
        sampler = animations[0]['samplers'][channel['sampler']]
        times = accessor(doc, binary, sampler['input']); values = accessor(doc, binary, sampler['output'])
        if not np.isfinite(values).all(): errors.append('Non-finite animation channel')
        duration = max(duration, float(times[-1, 0]))
        maximum_endpoint_delta = max(maximum_endpoint_delta, float(np.max(np.abs(values[0] - values[-1]))))
        if abs(float(times[0, 0])) > .0001: errors.append('Clip does not begin at zero')
    if maximum_endpoint_delta > 1e-5: errors.append('Loop endpoints differ')
    if len(animated_nodes) != asset.get('animated_nodes', 15) or abs(duration - 6) > .0001: errors.append('Animation node count or duration mismatch')
    if not (ROOT / asset['preview']).exists(): errors.append('Missing preview')
    bpy.ops.wm.open_mainfile(filepath=str(ROOT / asset['blend']))
    images = [image for image in bpy.data.images if image.source == 'FILE']
    if len(images) != 5 or not all(image.packed_file for image in images): errors.append('Blend textures not packed')
    report = {'id': asset['id'], 'shape': asset['shape'], 'success': not errors, 'errors': errors, 'triangles': triangles, 'materials': len(doc['materials']), 'embedded_images': len(doc['images']), 'packed_blend_images': len(images), 'animation': 'ManaFlow', 'duration': duration, 'animated_nodes': len(animated_nodes), 'maximum_loop_endpoint_delta': maximum_endpoint_delta, 'geometry_hash': geometry_hash.hexdigest(), 'canonical_triangle_hash': canonical_hash.hexdigest()}
    return report


def main():
    manifest = json.loads((ROOT / 'manifest.json').read_text(encoding='utf-8'))
    reports = [validate_asset(asset) for asset in manifest['assets']]
    groups = []
    for shape in manifest['shapes']:
        rows = [r for r in reports if r['shape'] == shape['id']]
        groups.append({'shape': shape['id'], 'asset_count': len(rows), 'same_geometry_across_palettes': len({r['canonical_triangle_hash'] for r in rows}) == 1, 'same_export_index_order': len({r['geometry_hash'] for r in rows}) == 1})
    frozen = json.loads((ROOT / 'approved-reference.json').read_text(encoding='utf-8'))
    changed = [entry['file'] for entry in frozen['files'] if hashlib.sha256((ROOT / entry['file']).read_bytes()).hexdigest().lower() != entry['sha256'].lower()]
    same_geometry = all(group['same_geometry_across_palettes'] and group['asset_count'] == 3 for group in groups)
    same_index_order = all(group['same_export_index_order'] for group in groups)
    result = {'success': all(r['success'] for r in reports) and same_geometry and len(reports) == 9 and not changed, 'asset_count': len(reports), 'same_geometry_across_palettes': same_geometry, 'same_export_index_order': same_index_order, 'approved_reference_unchanged': not changed, 'frozen_files_checked': len(frozen['files']), 'changed_frozen_files': changed, 'shapes': groups, 'assets': reports}
    (ROOT / 'validation.json').write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print('MANA_VALIDATION', json.dumps(result), flush=True)
    if not result['success']: raise RuntimeError('Mana validation failed')


if __name__ == '__main__': main()
