"""Validate the delivered Blender sources, embedded glTF assets and palette variants."""
from pathlib import Path
import hashlib
import json
import struct

import bpy
import numpy as np

ROOT = Path(__file__).resolve().parents[1]
TYPES = {5120: ('i1', 1), 5121: ('u1', 1), 5122: ('<i2', 2),
         5123: ('<u2', 2), 5125: ('<u4', 4), 5126: ('<f4', 4)}
WIDTHS = {'SCALAR': 1, 'VEC2': 2, 'VEC3': 3, 'VEC4': 4, 'MAT4': 16}


def glb_data(file):
    raw = file.read_bytes()
    magic, version, size = struct.unpack_from('<4sII', raw)
    assert magic == b'glTF' and version == 2 and size == len(raw), file
    json_size, kind = struct.unpack_from('<II', raw, 12)
    assert kind == 0x4E4F534A
    data = json.loads(raw[20:20+json_size])
    binary_size, kind = struct.unpack_from('<II', raw, 20+json_size)
    assert kind == 0x004E4942 and binary_size+json_size+28 == size
    binary = memoryview(raw)[28+json_size:]
    for item in data.get('buffers', [])+data.get('images', []):
        assert not item.get('uri'), (file, 'External dependency')
    for view in data['bufferViews']:
        assert view.get('buffer', 0) == 0
        assert 0 <= view.get('byteOffset', 0)
        assert view.get('byteOffset', 0)+view['byteLength'] <= binary_size

    def accessor(index):
        item = data['accessors'][index]
        view = data['bufferViews'][item['bufferView']]
        dtype, size = TYPES[item['componentType']]
        width = WIDTHS[item['type']]
        offset = view.get('byteOffset', 0)+item.get('byteOffset', 0)
        stride = view.get('byteStride', width*size)
        array = np.ndarray((item['count'], width), dtype=dtype, buffer=binary,
                           offset=offset, strides=(stride, size)).copy()
        assert np.isfinite(array).all()
        return array

    geometries = []
    triangles = 0
    primitives = 0
    for mesh in data['meshes']:
        for primitive in mesh['primitives']:
            assert primitive.get('mode', 4) == 4
            positions = accessor(primitive['attributes']['POSITION'])
            indices = accessor(primitive['indices']) if 'indices' in primitive else np.arange(len(positions))
            assert len(indices)%3 == 0 and np.max(indices) < len(positions)
            triangles += len(indices)//3
            assert 'NORMAL' in primitive['attributes']
            accessor(primitive['attributes']['NORMAL'])
            material = data['materials'][primitive['material']]
            if 'baseColorTexture' in material.get('pbrMetallicRoughness', {}):
                assert 'TEXCOORD_0' in primitive['attributes']
                accessor(primitive['attributes']['TEXCOORD_0'])
            digest = hashlib.sha256(positions.tobytes()+indices.tobytes()).hexdigest()
            geometries.append(digest)
            primitives += 1
    assert triangles > 0 and len(data.get('images', [])) > 0
    for image in data['images']:
        assert 'bufferView' in image

    animations = []
    for animation in data.get('animations', []):
        samplers = animation['samplers']
        channels = animation['channels']
        assert samplers and channels, (file, 'Empty animation')
        duration = 0.0
        sampler_durations = []
        for sampler in samplers:
            for key in ['input', 'output']:
                assert 0 <= sampler[key] < len(data['accessors'])
            input_meta = data['accessors'][sampler['input']]
            assert input_meta['type'] == 'SCALAR' and input_meta['componentType'] == 5126
            times = accessor(sampler['input']).ravel()
            assert len(times) >= 1 and times[0] >= 0 and np.all(np.diff(times) > 0)
            accessor(sampler['output'])
            assert sampler.get('interpolation', 'LINEAR') in {'LINEAR', 'STEP', 'CUBICSPLINE'}
            sampler_durations.append(float(times[-1]))
        targets = set()
        for channel in channels:
            assert 0 <= channel['sampler'] < len(samplers)
            target = channel['target']
            assert 0 <= target['node'] < len(data['nodes'])
            assert target['path'] in {'translation', 'rotation', 'scale', 'weights'}
            target_key = (target['node'], target['path'])
            assert target_key not in targets, (file, 'Duplicate animation target')
            targets.add(target_key)
            duration = max(duration, sampler_durations[channel['sampler']])
            sampler = samplers[channel['sampler']]
            input_meta = data['accessors'][sampler['input']]
            output_meta = data['accessors'][sampler['output']]
            multiplier = 3 if sampler.get('interpolation') == 'CUBICSPLINE' else 1
            expected_type = 'VEC4' if target['path'] == 'rotation' else 'VEC3'
            if target['path'] == 'weights':
                node = data['nodes'][target['node']]
                mesh = data['meshes'][node['mesh']]
                target_counts = {len(p.get('targets', [])) for p in mesh['primitives']}
                assert len(target_counts) == 1 and next(iter(target_counts)) > 0
                multiplier *= next(iter(target_counts))
                expected_type = 'SCALAR'
            assert output_meta['type'] == expected_type
            assert output_meta['count'] == input_meta['count']*multiplier
        assert duration > 0, (file, 'Animation duration must be positive')
        animations.append({'name': animation.get('name', ''), 'duration': duration,
                           'channels': len(channels), 'samplers': len(samplers),
                           'animated_nodes': len({node for node, _ in targets})})
    return {'triangles': triangles, 'meshes': len(data['meshes']), 'primitives': primitives,
            'materials': len(data['materials']), 'images': len(data['images']),
            'bytes': len(raw), 'geometry_hashes': sorted(geometries),
            'finite_geometry': True, 'embedded_images': True, 'external_dependencies': 0,
            'animations': animations}


def blend_data(file):
    bpy.ops.wm.open_mainfile(filepath=str(file), use_scripts=False)
    assert not bpy.data.libraries, (file, 'Linked library')
    images = []
    for image in bpy.data.images:
        if image.source not in {'FILE', 'TILED'}:
            continue
        assert image.packed_file or image.packed_files, (file, image.name, 'Not packed')
        images.append({'name': image.name, 'size': list(image.size)})
    assert images, (file, 'No packed textures')
    return {'opened': True, 'packed_images': images, 'linked_libraries': 0}


def main():
    families = []
    triangle_budgets = {'cailloux': 20000, 'roches-metalliferes': 20000,
                        'cristaux': 20000, 'buissons': 45000, 'herbes': 15000,
                        'champignons': 30000, 'fleurs': 35000,
                        'souches': 35000, 'troncs-couches': 35000,
                        'mares-de-mana': 25000}
    for family, triangle_budget in triangle_budgets.items():
        folder = ROOT/family
        manifest = json.loads((folder/'manifest.json').read_text(encoding='utf-8-sig'))
        expected_shapes, expected_materials = (3, 3) if family == 'mares-de-mana' else (4, 3)
        expected_assets = expected_shapes*expected_materials
        assert len(manifest['shapes']) == expected_shapes
        assert len(manifest['materials']) == expected_materials
        assert len(manifest['assets']) == expected_assets
        pairs = {(a['shape'], a['material']) for a in manifest['assets']}
        assert len(pairs) == expected_assets
        assert pairs == {(s['id'], m['id']) for s in manifest['shapes'] for m in manifest['materials']}
        rows = []
        for item in manifest['assets']:
            for key in ['glb', 'blend', 'preview']:
                assert (folder/item[key]).is_file(), (family, item[key])
            result = glb_data(folder/item['glb'])
            assert result['triangles'] == item['triangles'], (item['id'], result['triangles'], item['triangles'])
            assert result['triangles'] <= triangle_budget, (family, item['id'], 'Triangle budget exceeded')
            if family == 'mares-de-mana':
                assert item['animation'] == 'ManaFlow' and np.isclose(item['animation_duration'], 6, atol=.05, rtol=0)
                matching = [a for a in result['animations'] if a['name'] == item['animation']]
                assert len(matching) == 1, (item['id'], 'Expected animation missing or ambiguous')
                assert item['animation_duration'] > 0
                assert np.isclose(matching[0]['duration'], item['animation_duration'], atol=.05, rtol=0), (item['id'], 'Animation duration differs from manifest')
            blend = blend_data(folder/item['blend'])
            rows.append({'id': item['id'], 'shape': item['shape'], 'material': item['material'],
                         'dimensions': [item[k] for k in ['height', 'width', 'depth']],
                         'glb': result, 'blend': blend})
        for shape in manifest['shapes']:
            variants = [r for r in rows if r['shape'] == shape['id']]
            assert len(variants) == expected_materials
            assert all(v['glb']['geometry_hashes'] == variants[0]['glb']['geometry_hashes'] for v in variants), (family, shape['id'], 'Geometry differs between palettes')
            assert all(np.allclose(v['dimensions'], variants[0]['dimensions'], atol=.0002) for v in variants)
        if expected_shapes > 1:
            assert (folder/'comparaison-tailles.png').is_file()
        families.append({'family': family, 'geometry_same_across_materials': True, 'assets': rows})
        print('FAMILY_VALIDATED', family, len(rows), flush=True)
    reference = ROOT/'references/arbre-peint-v3'
    frozen = json.loads((reference/'reference.json').read_text(encoding='utf-8-sig'))
    for file in frozen['files']:
        assert hashlib.sha256((reference/file['file']).read_bytes()).hexdigest().upper() == file['sha256']
    mana_reference = ROOT/'mares-de-mana'
    frozen_mana = json.loads((mana_reference/'approved-reference.json').read_text(encoding='utf-8-sig'))
    mana_manifest = json.loads((mana_reference/'manifest.json').read_text(encoding='utf-8-sig'))
    assert set(frozen_mana['asset_ids']) == {a['id'] for a in mana_manifest['assets']}
    frozen_paths = {f['file'] for f in frozen_mana['files']}
    assert all(a[key] in frozen_paths for a in mana_manifest['assets'] for key in ['glb', 'blend', 'preview'])
    for file in frozen_mana['files']:
        assert hashlib.sha256((mana_reference/file['file']).read_bytes()).hexdigest().lower() == file['sha256'].lower(), (file['file'], 'Approved mana reference changed')
    asset_count = sum(len(f['assets']) for f in families)
    report = {'success': True, 'asset_count': asset_count, 'families': families,
              'approved_reference_unchanged': True, 'reference_files_checked': len(frozen['files']),
              'approved_mana_reference_unchanged': True, 'mana_reference_files_checked': len(frozen_mana['files'])}
    (ROOT/'validation.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
    print('RESOURCES_VALIDATED', asset_count, flush=True)


if __name__ == '__main__':
    main()
