"""Check portable tool assets, grip pivots and the exported floating-shard loop."""
import importlib.util
import json
import hashlib
import sys
import re
from pathlib import Path

import bpy
import numpy as np
from mathutils import Vector, Quaternion

sys.dont_write_bytecode = True

ROOT = Path(__file__).resolve().parents[1]
RESOURCES = ROOT.parent
SHAPES = ('hache', 'pioche', 'faucille')


def load_module(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


common = load_module('resource_validation', RESOURCES / 'scripts/validate_resources.py')
mana = load_module('mana_validation_reader', RESOURCES / 'mares-de-mana/scripts/validate_mana_pool.py')


def validate_asset(asset):
    for key in ('blend', 'glb', 'preview'):
        assert (ROOT / asset[key]).is_file(), (asset['id'], key)
    result = common.glb_data(ROOT / asset['glb'])
    assert 0 < result['triangles'] <= 16000, result['triangles']
    assert result['triangles'] == asset['triangles']
    assert result['materials'] <= 5
    doc, binary = mana.glb_read(ROOT / asset['glb'])
    assert not doc.get('cameras'), 'Studio camera exported'
    assert 'KHR_lights_punctual' not in doc.get('extensions', {}), 'Studio lights exported'
    roots = doc['scenes'][doc.get('scene', 0)]['nodes']
    assert len(roots) == 1, 'Expected one attachable root'
    root = doc['nodes'][roots[0]]
    assert root['name'] == 'Tool_Root'
    assert np.allclose(root.get('translation', [0, 0, 0]), 0, atol=1e-6)
    assert np.allclose(root.get('scale', [1, 1, 1]), 1, atol=1e-6)
    assert np.allclose(root.get('rotation', [0, 0, 0, 1]), [0, 0, 0, 1], atol=1e-6)
    socket_count = asset['socket_count']
    expected_names = ['Socket_%02d' % (i + 1) for i in range(socket_count)]
    socket_nodes = [(i, node) for i, node in enumerate(doc['nodes']) if re.fullmatch(r'Socket_\d+', node.get('name', ''))]
    assert sorted(node['name'] for _, node in socket_nodes) == expected_names
    for index, node in socket_nodes:
        assert index in root.get('children', []), 'Socket must be attached directly to grip root'
        assert 'mesh' not in node and np.allclose(node.get('scale', [1, 1, 1]), 1)
        assert np.isfinite(node.get('translation', [0, 0, 0])).all()
        q = node.get('rotation', [0, 0, 0, 1])
        outward = Quaternion((q[3], q[0], q[1], q[2])) @ Vector((0, 0, 1))
        assert np.allclose(outward, [0, 0, 1], atol=1e-5), 'GLB socket axis must face forward'
        metadata = next(s for s in asset['sockets'] if s['name'] == node['name'])
        assert metadata['empty'] and metadata['inner_diameter'] > 0 and metadata['cavity_depth'] > 0
        assert np.allclose(node.get('translation', [0, 0, 0]), metadata['position_gltf'], atol=1e-6)
    animations = doc.get('animations', [])
    assert len(animations) == 1
    clip = animations[0]
    assert clip['name'] == asset['animation']
    max_delta = 0.0
    animated_nodes = set()
    animation_fingerprints = []
    duration = 0.0
    for channel in clip['channels']:
        target = channel['target']
        assert target['node'] != roots[0], 'Grip moves during idle'
        assert target['node'] not in {i for i, _ in socket_nodes}, 'Socket moves during idle'
        animated_nodes.add(target['node'])
        sampler = clip['samplers'][channel['sampler']]
        times = mana.accessor(doc, binary, sampler['input']).ravel()
        values = mana.accessor(doc, binary, sampler['output'])
        animation_fingerprints.append((doc['nodes'][target['node']]['name'], target['path'],
                                       hashlib.sha256(times.tobytes() + values.tobytes()).hexdigest()))
        assert sampler.get('interpolation', 'LINEAR') == 'LINEAR'
        assert abs(times[0]) < 1e-5 and np.all(np.diff(times) > 0)
        assert np.isfinite(values).all()
        delta = np.max(np.abs(values[0] - values[-1]))
        if target['path'] == 'rotation':
            delta = min(delta, np.max(np.abs(values[0] + values[-1])))
        max_delta = max(max_delta, float(delta))
        duration = max(duration, float(times[-1]))
    assert len(animated_nodes) == 3, 'Expected three animated mana shards'
    assert max_delta < 1e-5, 'Non-seamless loop'
    assert abs(duration - 4) < 1e-5
    assert abs(duration - asset['animation_duration']) < 1e-5
    blend = common.blend_data(ROOT / asset['blend'])
    assert 'Tool_Root' in bpy.data.objects
    grip = bpy.data.objects['Tool_Root']
    assert np.allclose(grip.location, 0, atol=1e-6)
    assert not grip.animation_data, 'Grip should remain fixed'
    marker_reports = []
    model_meshes = [obj for obj in grip.children_recursive if obj.type == 'MESH']
    assert len(asset.get('sockets', [])) == socket_count
    for name in expected_names:
        marker = bpy.data.objects.get(name)
        assert marker and marker.type == 'EMPTY' and marker.parent == grip
        assert np.allclose(marker.scale, 1) and not marker.animation_data
        origin = marker.matrix_world.translation
        metadata = next(s for s in asset['sockets'] if s['name'] == name)
        assert np.allclose(origin, metadata['position_blender'], atol=1e-6)
        axis = (marker.matrix_world.to_3x3() @ Vector((0, 0, 1))).normalized()
        assert np.allclose(axis, [0, -1, 0], atol=1e-5), (name, 'Attachment axis must face outward')
        # A centred ray must reach the seating surface, never an inserted gem or a closed cap.
        ray_origin = origin + axis * .15
        distances = []
        for obj in model_meshes:
            inverse = obj.matrix_world.inverted()
            hit, point, _, _ = obj.ray_cast(inverse @ ray_origin, inverse.to_3x3() @ -axis)
            if hit:
                world_point = obj.matrix_world @ point
                distances.append(float((world_point - origin).dot(axis)))
        assert distances, (asset['id'], name, 'Missing socket bottom')
        centre_surface_offset = max(distances)
        assert abs(centre_surface_offset) < .001, (asset['id'], name, 'Socket is filled or marker does not sit on its bottom', centre_surface_offset)
        marker_reports.append({'name': name, 'empty_cavity': True,
                               'centre_surface_offset_m': centre_surface_offset,
                               'position_blender': list(origin), 'axis_blender': list(axis)})
    return {'id': asset['id'], 'shape': asset['shape'], 'material': asset['material'],
            'socket_count': socket_count, 'sockets': marker_reports, 'glb': result, 'blend': blend,
            'animation_fingerprints': sorted(animation_fingerprints),
            'single_grip_root': True, 'loop_duration': duration,
            'animated_nodes': len(animated_nodes), 'maximum_loop_endpoint_delta': max_delta}


def check_reference(folder, name):
    record = json.loads((folder / name).read_text(encoding='utf-8-sig'))
    for item in record['files']:
        actual = hashlib.sha256((folder / item['file']).read_bytes()).hexdigest()
        assert actual.lower() == item['sha256'].lower(), item['file']
    return len(record['files'])


def main():
    manifest = json.loads((ROOT / 'manifest.json').read_text(encoding='utf-8-sig'))
    assert manifest['artistic_status'] != 'approved'
    assert {a['shape'] for a in manifest['assets']} == set(SHAPES)
    assert {s['id'] for s in manifest['shapes']} == set(SHAPES)
    assert manifest['socket_counts'] == [0, 1, 2, 3]
    assert {m['id'] for m in manifest['materials']} == {'azur', 'orange', 'vert'}
    assert len(manifest['assets']) == 36
    expected = {(shape, palette, count) for shape in SHAPES
                for palette in ('azur', 'orange', 'vert') for count in range(4)}
    actual = {(a['shape'], a['material'], a['socket_count']) for a in manifest['assets']}
    assert actual == expected
    rows = [validate_asset(asset) for asset in manifest['assets']]
    groups = []
    for shape in SHAPES:
        socket_layout = next(a['sockets'] for a in manifest['assets']
                             if a['shape'] == shape and a['material'] == 'azur' and a['socket_count'] == 3)
        for count in range(4):
            for asset in manifest['assets']:
                if asset['shape'] == shape and asset['socket_count'] == count:
                    assert asset['sockets'] == socket_layout[:count], 'Socket positions change between variants'
            variants = [r for r in rows if r['shape'] == shape and r['socket_count'] == count]
            assert all(r['glb']['geometry_hashes'] == variants[0]['glb']['geometry_hashes'] for r in variants)
            assert all(r['sockets'] == variants[0]['sockets'] for r in variants)
            assert all(r['animation_fingerprints'] == variants[0]['animation_fingerprints'] for r in variants)
            groups.append({'shape': shape, 'socket_count': count, 'same_geometry_sockets_animation_across_palettes': True})
    for row in rows:
        row['glb'].pop('geometry_hashes')
        row.pop('animation_fingerprints')
    references = {
        'tree': check_reference(RESOURCES / 'references/arbre-peint-v3', 'reference.json'),
        'mana': check_reference(RESOURCES / 'mares-de-mana', 'approved-reference.json'),
        'original_tools': check_reference(ROOT, 'baseline-v1.json'),
        'pre_sickle_tools_and_textures': check_reference(ROOT, 'baseline-before-sickle.json'),
    }
    report = {'success': True, 'asset_count': len(rows), 'assets': rows,
              'variant_matrix_complete': True, 'palette_geometry_checks': groups,
              'reference_and_baseline_hashes_unchanged': references}
    (ROOT / 'validation.json').write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print('TOOLS_VALIDATED', len(rows), 'assets;', sum(len(r['sockets']) for r in rows), 'empty sockets;', references, flush=True)


if __name__ == '__main__':
    main()
