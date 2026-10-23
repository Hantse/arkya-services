"""Validate the 21 weapon deliveries, then reimport their FBX files in Blender.

Run with Blender in background mode::

    blender --background --python validate_weapons.py

Only validation.json is written. Source models and the running Unity Editor are
untouched. Technical success does not constitute an artistic or gameplay review.
The GLB reader is shared with the existing character asset validator.
"""
import argparse
from collections import Counter
from datetime import datetime, timezone
import hashlib
import importlib.util
import json
import math
from pathlib import Path
import sys
import traceback

import bpy
import bmesh
from mathutils import Vector
from mathutils.kdtree import KDTree
from io_scene_fbx import parse_fbx


sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
EXPECTED = tuple(prefix + '-' + variant
                 for prefix in ('E1', 'E2', 'H1', 'H2', 'B', 'BT', 'AR')
                 for variant in 'ABC')
CATEGORIES = {
    'E1': ('sword-one-hand',), 'E2': ('sword-two-hands',),
    'H1': ('AxeOneHand', 'axe-one-hand'), 'H2': ('AxeTwoHands', 'axe-two-hands'),
    'B': ('shield',), 'BT': ('staff',), 'AR': ('bow',),
}
POSITION_TOLERANCE = 2e-5
AREA_TOLERANCE = 1e-12


def _load_reader():
    path = ROOT.parent / 'personnages/scripts/validate_characters.py'
    spec = importlib.util.spec_from_file_location('arkya_weapon_glb_reader', path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


GLTF = _load_reader()


def require(condition, message):
    if not condition:
        raise ValueError(message)


def sha256(path):
    digest = hashlib.sha256()
    with path.open('rb') as stream:
        while block := stream.read(1024 * 1024):
            digest.update(block)
    return digest.hexdigest()


def close(actual, expected, label, tolerance=POSITION_TOLERANCE):
    require(len(actual) == len(expected), label + ': dimension mismatch')
    error = max(abs(float(a)-float(e)) for a, e in zip(actual, expected))
    require(error <= tolerance, f'{label}: error {error:.8g} exceeds {tolerance:g}; {list(actual)} != {list(expected)}')
    return error


def finite_vector(value, label):
    require(len(value) == 3 and all(math.isfinite(v) for v in value), label + ': invalid vector')


def bounds(points):
    require(bool(points), 'No mesh positions')
    low = [min(p[i] for p in points) for i in range(3)]
    high = [max(p[i] for p in points) for i in range(3)]
    return {'min': low, 'max': high, 'dimensions': [high[i]-low[i] for i in range(3)]}


def compare_bounds(actual, expected, label):
    return max(close(actual[key], expected[key], label + ' ' + key) for key in ('min', 'max'))


def source_tree(points):
    tree = KDTree(len(points))
    for i, point in enumerate(points):
        tree.insert(Vector(point), i)
    tree.balance()
    return tree


def compare_vertices(points, reference_points, label):
    tree = source_tree(reference_points)
    maximum = max(tree.find(Vector(point))[2] for point in points)
    reverse = source_tree(points)
    maximum = max(maximum, max(reverse.find(Vector(point))[2] for point in reference_points))
    require(maximum <= POSITION_TOLERANCE, label + f': geometry moved by {maximum:.8g} m')
    return maximum


def color_stats(rows, label):
    require(bool(rows), label + ': missing vertex colors')
    require(all(len(row) in (3, 4) for row in rows), label + ': malformed colors')
    require(all(math.isfinite(value) and -1e-6 <= value <= 1.000001 for row in rows for value in row),
            label + ': non-finite or out-of-range colors')
    low = [min(row[i] for row in rows) for i in range(3)]
    high = [max(row[i] for row in rows) for i in range(3)]
    distinct = len({tuple(round(value, 4) for value in row[:3]) for row in rows})
    return {'samples': len(rows), 'minRGB': low, 'maxRGB': high,
            'distinctRGBAtFourDecimals': distinct, 'finiteAndNormalized': True}


def triangle_area(a, b, c):
    u = [b[i]-a[i] for i in range(3)]
    v = [c[i]-a[i] for i in range(3)]
    cross = (u[1]*v[2]-u[2]*v[1], u[2]*v[0]-u[0]*v[2], u[0]*v[1]-u[1]*v[0])
    return math.sqrt(sum(x*x for x in cross)) * .5


def mesh_stats(obj):
    data = obj.data
    data.calc_loop_triangles()
    points = [tuple(obj.matrix_world @ vertex.co) for vertex in data.vertices]
    require(points and all(math.isfinite(c) for point in points for c in point), obj.name + ': invalid positions')
    areas = [triangle_area(*(points[i] for i in tri.vertices)) for tri in data.loop_triangles]
    bm = bmesh.new()
    bm.from_mesh(data)
    stats = {'vertices': len(points), 'triangles': len(areas),
             'boundaryEdges': sum(edge.is_boundary for edge in bm.edges),
             'nonManifoldEdges': sum(not edge.is_manifold for edge in bm.edges),
             'degenerateTriangles': sum(area <= AREA_TOLERANCE for area in areas),
             'minimumTriangleAreaM2': min(areas, default=0),
             'finitePositions': True}
    bm.free()
    require(stats['triangles'] > 0, obj.name + ': empty triangle mesh')
    require(stats['boundaryEdges'] == stats['nonManifoldEdges'] == 0,
            obj.name + ': open or non-manifold source topology ' + str(stats))
    require(stats['degenerateTriangles'] == 0, obj.name + ': zero-area triangle ' + str(stats))
    attr = data.color_attributes.get('Painted') or data.color_attributes.active_color
    require(attr is not None, obj.name + ': vertex pigments missing')
    stats['colors'] = color_stats([tuple(item.color) for item in attr.data], obj.name)
    stats['materialNames'] = sorted({m.name for m in data.materials if m})
    require(0 < len(stats['materialNames']) <= 5, obj.name + ': material budget exceeded')
    require(len(data.uv_layers) > 0, obj.name + ': UV layer missing')
    return stats, points


def check_metadata(folder, code):
    paths = {ext: folder / (code + '.' + ext) for ext in ('blend', 'fbx', 'glb', 'json')}
    paths['preview'] = folder / (code + '-preview.png')
    for kind, path in paths.items():
        require(path.is_file() and path.stat().st_size > 0, f'{code}: missing or empty {kind}')
    record = json.loads(paths['json'].read_text(encoding='utf-8'))
    require(record.get('code') == code, code + ': record identifier mismatch')
    require(record.get('category') in CATEGORIES[code.rsplit('-', 1)[0]], code + ': wrong category')
    require(record.get('units') == 'meters', code + ': units must be meters')
    require(record.get('blenderAxes') == {'up': '+Z', 'front': '-Y'}, code + ': source axes changed')
    require(record.get('unityAxes') == {'up': '+Y', 'front': '+Z'}, code + ': target axes changed')
    require(set(record.get('formats', [])) == {'blend', 'fbx', 'glb'}, code + ': missing format declaration')
    markers = record.get('markers', {})
    require('Grip_Primary' in markers and 'Tip' in markers, code + ': primary grip or tip marker missing')
    for name, point in markers.items():
        finite_vector(point, code + ' ' + name)
    close(markers['Grip_Primary'], [0, 0, 0], code + ' primary grip')
    if code.startswith(('E2-', 'H2-', 'BT-')):
        require('Grip_Secondary' in markers, code + ': second hand marker missing')
        distance = Vector(markers['Grip_Secondary']).length
        require(.15 <= distance <= .8, code + ': implausible hand spacing')
    for point in record['boundsBlender'].values():
        finite_vector(point, code + ' bounds')
    finite_vector(record['dimensionsBlender'], code + ' dimensions')
    expected = record['boundsBlender']
    close(record['dimensionsBlender'], [expected['max'][i]-expected['min'][i] for i in range(3)], code + ' recorded dimensions')
    require(all(0 < d < 3 for d in record['dimensionsBlender']), code + ': implausible metre dimensions')
    lods = record.get('lods', [])
    require(len(lods) == 3, code + ': expected three LOD records')
    counts = [lod['triangles'] for lod in lods]
    require(counts[0] > counts[1] > counts[2] > 0, code + ': LOD triangle counts do not decrease')
    for lod in lods:
        require(lod['boundaryEdges'] == lod['nonManifoldEdges'] == lod['degenerateFaces'] == 0,
                code + ': generation recorded invalid topology')
        require(lod['finite'] and lod['vertexColors'] and 0 < lod['materials'] <= 5,
                code + ': generation recorded invalid pigments or material count')
    require(0 < len(record.get('surfaceMaterials', [])) <= 5, code + ': material count exceeds five')
    hashes = {}
    for ext in ('blend', 'fbx', 'glb'):
        hashes[ext] = sha256(paths[ext])
        require(record.get('sha256', {}).get(ext) == hashes[ext], code + ': stale ' + ext + ' hash')
    hashes['json'] = sha256(paths['json'])
    hashes['preview'] = sha256(paths['preview'])
    with paths['blend'].open('rb') as source:
        require(source.read(12).startswith((b'BLENDER', b'\x1f\x8b', b'\x28\xb5\x2f\xfd')), code + ': invalid Blender signature')
    with paths['preview'].open('rb') as source:
        require(source.read(8) == b'\x89PNG\r\n\x1a\n', code + ': invalid preview PNG')
    return record, paths, hashes


def check_blend(path, record):
    bpy.ops.wm.open_mainfile(filepath=str(path), load_ui=False)
    scene = bpy.context.scene
    code = record['code']
    require(scene.unit_settings.system == 'METRIC' and abs(scene.unit_settings.scale_length - 1) < 1e-6,
            code + ': Blender units are not metres')
    root = scene.objects.get(code)
    require(root is not None and root.type == 'EMPTY' and root.parent is None, code + ': source grip root missing')
    close(root.matrix_world.translation, [0, 0, 0], code + ' source pivot')
    close(root.scale, [1, 1, 1], code + ' source root scale')
    close(root.rotation_euler, [0, 0, 0], code + ' source root rotation')
    parts = [obj for obj in root.children_recursive if obj.type == 'MESH']
    require(parts and len(parts) == len([obj for obj in scene.objects if obj.type == 'MESH']), code + ': mesh outside model root')
    require(not any(obj.type in ('ARMATURE', 'CAMERA', 'LIGHT') for obj in scene.objects), code + ': unexpected source scene object')
    stats, points, materials = [], [], set()
    for obj in parts:
        result, positions = mesh_stats(obj)
        stats.append(result)
        points.extend(positions)
        materials.update(result['materialNames'])
    require(len(materials) <= 5, code + ': source uses more than five materials')
    require(materials == set(record['surfaceMaterials']), code + ': source materials differ from metadata')
    count = sum(stat['triangles'] for stat in stats)
    require(count == record['lods'][0]['triangles'], code + ': source triangle count differs from LOD0')
    actual_bounds = bounds(points)
    error = compare_bounds(actual_bounds, record['boundsBlender'], code + ' source bounds')
    for name, expected in record['markers'].items():
        obj = scene.objects.get(name)
        require(obj is not None and obj.type == 'EMPTY' and obj.parent == root, code + ': missing source marker ' + name)
        close(obj.matrix_world.translation, expected, code + ' source ' + name)
    return {'status': 'passed', 'parts': len(parts), 'vertices': len(points), 'triangles': count,
            'boundsMeters': actual_bounds, 'maxBoundsErrorM': error,
            'boundaryEdges': 0, 'nonManifoldEdges': 0, 'degenerateTriangles': 0,
            'minimumTriangleAreaM2': min(stat['minimumTriangleAreaM2'] for stat in stats),
            'materialNames': sorted(materials), 'allPartsHaveFiniteVertexColors': True,
            'markersAtDeclaredPositions': True, 'rootPivotAtPrimaryGrip': True}, points


def check_glb(path, record, source_points):
    glb = GLTF.Glb(path)
    data = glb.data
    require(not data.get('cameras') and 'KHR_lights_punctual' not in data.get('extensions', {}), 'GLB contains studio objects')
    require(not data.get('animations') and not data.get('skins'), 'Static weapon GLB contains animation or skin')
    require(not data.get('images') and not data.get('textures'), 'Unexpected external pigment texture dependency')
    materials = data.get('materials', [])
    require(0 < len(materials) <= 5, 'GLB exceeds five material surfaces')
    nodes = data.get('nodes', [])
    scene = data['scenes'][data.get('scene', 0)]
    roots = scene.get('nodes', [])
    require(len(roots) == 1, 'GLB requires one attachable model root')
    root_index = roots[0]
    require(nodes[root_index].get('name') == record['code'], 'GLB root identifier mismatch')
    world_matrices, visited = {}, set()

    def visit(index, parent_matrix):
        require(index not in visited, 'GLB contains a cycle or shared scene node')
        visited.add(index)
        node = nodes[index]
        world = GLTF.multiply(parent_matrix, GLTF.node_matrix(node))
        world_matrices[index] = world
        for child in node.get('children', []):
            visit(child, world)

    visit(root_index, GLTF.IDENTITY)
    require(len(visited) == len(nodes), 'GLB has unreachable nodes')
    close(GLTF.transform_point(world_matrices[root_index], [0, 0, 0]), [0, 0, 0], 'GLB root pivot')
    named = {}
    for index in visited:
        name = nodes[index].get('name', '')
        require(name not in named, 'Duplicate GLB node name ' + name)
        named[name] = index
    for name, (x, y, z) in record['markers'].items():
        require(name in named, 'GLB marker missing: ' + name)
        index = named[name]
        require(index in nodes[root_index].get('children', []) and 'mesh' not in nodes[index], 'GLB marker parent/type changed: ' + name)
        close(GLTF.transform_point(world_matrices[index], [0, 0, 0]), [x, z, -y], 'GLB +Y conversion ' + name)

    mesh_nodes = [index for index in visited if 'mesh' in nodes[index]]
    require(len(mesh_nodes) == 1 and nodes[mesh_nodes[0]].get('name') == record['code'] + '_LOD0',
            'GLB must contain only the portable LOD0 mesh')
    positions_all, colors_all, triangle_count, minimum_area = [], [], 0, math.inf
    # glTF splits indexed vertices at normal, color and material seams. Count
    # coincident-position junctions separately; true topology is checked in
    # .blend and the independently reimported FBX, where indices are preserved.
    edges = Counter()
    for node_index in mesh_nodes:
        node = nodes[node_index]
        close(GLTF.transform_point(world_matrices[node_index], [0, 0, 0]), [0, 0, 0], 'GLB mesh pivot')
        primitives = data['meshes'][node['mesh']]['primitives']
        for primitive in primitives:
            require(primitive.get('mode', 4) == 4, 'GLB primitive must be triangles')
            attrs = primitive['attributes']
            require({'POSITION', 'NORMAL', 'COLOR_0', 'TEXCOORD_0'} <= set(attrs), 'GLB position, normal, UV or color missing')
            positions = glb.accessor(attrs['POSITION'])
            normals = glb.accessor(attrs['NORMAL'])
            colors = glb.accessor(attrs['COLOR_0'])
            uv = glb.accessor(attrs['TEXCOORD_0'])
            require(len(positions) == len(normals) == len(colors) == len(uv), 'GLB vertex attribute length mismatch')
            require(all(len(p) == 3 for p in positions) and all(len(n) == 3 for n in normals), 'Malformed GLB vector accessor')
            require(all(.95 <= math.sqrt(sum(v*v for v in normal)) <= 1.05 for normal in normals), 'GLB normals are not normalized')
            require(0 <= primitive.get('material', -1) < len(materials), 'GLB material index out of range')
            transformed = [GLTF.transform_point(world_matrices[node_index], p) for p in positions]
            indices = [row[0] for row in glb.accessor(primitive['indices'])] if 'indices' in primitive else list(range(len(positions)))
            require(len(indices) % 3 == 0 and all(isinstance(i, int) and 0 <= i < len(positions) for i in indices), 'GLB triangle indices invalid')
            for start in range(0, len(indices), 3):
                tri = [transformed[index] for index in indices[start:start+3]]
                area = triangle_area(*tri)
                require(area > AREA_TOLERANCE, f'GLB zero-area triangle: {area:g}')
                minimum_area = min(minimum_area, area)
                for a, b in ((tri[0], tri[1]), (tri[1], tri[2]), (tri[2], tri[0])):
                    qa, qb = tuple(round(v, 7) for v in a), tuple(round(v, 7) for v in b)
                    if qa != qb:
                        edges[tuple(sorted((qa, qb)))] += 1
            triangle_count += len(indices)//3
            positions_all.extend(transformed)
            colors_all.extend(colors)
    require(triangle_count == record['lods'][0]['triangles'], 'GLB triangle count differs from source LOD0')
    single_use_edges = sum(count == 1 for count in edges.values())
    require(single_use_edges == 0, f'GLB has {single_use_edges} unpaired geometric edges')
    pigments = color_stats(colors_all, 'GLB pigments')
    require(pigments['distinctRGBAtFourDecimals'] > 2, 'GLB painted variation missing')
    low, high = record['boundsBlender']['min'], record['boundsBlender']['max']
    expected = {'min': [low[0], low[2], -high[1]], 'max': [high[0], high[2], -low[1]]}
    actual = bounds(positions_all)
    error = compare_bounds(actual, expected, 'GLB +Y metre bounds')
    source_space = [(x, -z, y) for x, y, z in positions_all]
    vertex_error = compare_vertices(source_space, source_points, 'GLB vertices')
    return {'status': 'passed', 'method': 'Independent glTF 2.0 binary accessor decoding',
            'triangles': triangle_count, 'exportedVerticesIncludingAttributeSeams': len(positions_all),
            'minimumTriangleAreaM2': minimum_area, 'colors': pigments,
            'materialNames': sorted(material.get('name', '') for material in materials),
            'boundsMetersYUp': actual, 'maxBoundsErrorM': error,
            'maxVertexRoundtripErrorM': vertex_error,
            'coincidentPositionEdgeDiagnostics': {'singleUseEdges': single_use_edges,
                'junctionEdgesWithMoreThanTwoUses': sum(count > 2 for count in edges.values()),
                'note': 'Diagnostic only: attribute seams and coincident closed shells share positions. Indexed manifold checks use Blender and FBX.'},
            'primaryGripPivotZero': True, 'markersConvertedFromZUpToYUp': True}


def fbx_settings(path):
    root, version = parse_fbx.parse(str(path))
    settings = next((node for node in root.elems if node.id == b'GlobalSettings'), None)
    require(settings is not None, 'FBX GlobalSettings missing')
    properties = next((node for node in settings.elems if node.id == b'Properties70'), None)
    require(properties is not None, 'FBX axis/unit properties missing')
    values = {node.props[0].decode(): node.props[-1] for node in properties.elems if node.id == b'P'}
    require(values.get('UpAxis') == 1 and values.get('UpAxisSign') == 1, 'FBX does not declare +Y up')
    require(abs(values.get('UnitScaleFactor', 0)-100) < 1e-6, 'FBX units do not encode metres')
    return {'version': version, **{key: values.get(key) for key in
        ('UpAxis', 'UpAxisSign', 'FrontAxis', 'FrontAxisSign', 'CoordAxis', 'CoordAxisSign',
         'UnitScaleFactor', 'OriginalUnitScaleFactor')}}


def check_fbx(path, record, source_points):
    settings = fbx_settings(path)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(path), use_anim=False, colors_type='LINEAR')
    scene = bpy.context.scene
    code = record['code']
    root = scene.objects.get(code)
    require(root is not None and root.type == 'EMPTY' and root.parent is None, code + ': FBX grip root missing')
    close(root.matrix_world.translation, [0, 0, 0], code + ' FBX pivot')
    close(root.matrix_world.to_scale(), [1, 1, 1], code + ' FBX metre scale')
    parts = [obj for obj in scene.objects if obj.type == 'MESH']
    require(len(parts) == 3, code + ': FBX requires LOD0, LOD1 and LOD2')
    reports = []
    for level in range(3):
        name = code + '_LOD' + str(level)
        obj = scene.objects.get(name)
        require(obj is not None and obj.type == 'MESH' and obj.parent == root, code + ': missing FBX mesh ' + name)
        close(obj.matrix_world.translation, [0, 0, 0], name + ' FBX mesh pivot')
        stats, positions = mesh_stats(obj)
        require(stats['triangles'] == record['lods'][level]['triangles'], name + ': FBX triangle count changed')
        stats['boundsMetersZUpAfterReimport'] = bounds(positions)
        if level == 0:
            stats['maxBoundsErrorM'] = compare_bounds(stats['boundsMetersZUpAfterReimport'], record['boundsBlender'], 'FBX roundtrip bounds')
            stats['maxVertexRoundtripErrorM'] = compare_vertices(positions, source_points, 'FBX vertices')
        reports.append(stats)
    require(reports[0]['triangles'] > reports[1]['triangles'] > reports[2]['triangles'], code + ': imported LOD counts do not decrease')
    for name, expected in record['markers'].items():
        obj = scene.objects.get(name)
        require(obj is not None and obj.type == 'EMPTY' and obj.parent == root, code + ': FBX marker missing: ' + name)
        close(obj.matrix_world.translation, expected, code + ' FBX ' + name)
    return {'status': 'passed', 'method': 'Fresh Blender FBX import for every asset',
            'globalSettings': settings, 'lods': reports,
            'rootPivotAtPrimaryGrip': True, 'markersAtDeclaredPositions': True,
            'lodTriangleCountsStrictlyDecrease': True}


def validate_asset(root, code):
    folder = root / 'models' / code
    record, paths, hashes = check_metadata(folder, code)
    blend, source_points = check_blend(paths['blend'], record)
    glb = check_glb(paths['glb'], record, source_points)
    fbx = check_fbx(paths['fbx'], record, source_points)
    return {'code': code, 'category': record['category'], 'status': 'passed',
            'sha256': hashes, 'blend': blend, 'glb': glb, 'fbx': fbx}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--root', type=Path, default=ROOT)
    parser.add_argument('--codes', nargs='+', choices=EXPECTED, default=list(EXPECTED))
    parser.add_argument('--output', type=Path)
    args = parser.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
    selected = list(dict.fromkeys(args.codes))
    complete = set(selected) == set(EXPECTED)
    output = args.output or args.root / ('validation.json' if complete else 'validation-partial.json')
    report = {'schemaVersion': 1, 'generatedUtc': datetime.now(timezone.utc).isoformat(),
              'validatorSha256': sha256(Path(__file__)), 'blenderVersion': bpy.app.version_string,
              'status': 'failed', 'scope': '21 static weapon art assets; offline source and export validation',
              'expectedCodes': list(EXPECTED), 'selectedCodes': selected, 'completeLibrary': complete,
              'technicalValidation': {'status': 'running', 'checks': [
                  'Seven categories with three distinct concept IDs each',
                  'Source and all FBX LOD meshes closed, finite and positive-area',
                  'Independent GLB indices, positions, normals, UVs and vertex pigments',
                  'Three decreasing FBX LODs and no more than five surface materials',
                  'Export +Y up, metre dimensions, zero grip pivot and attachment markers',
                  'Source-to-export vertex positions and immutable SHA-256 records']},
              'artReview': {'status': 'requires_visual_review',
                            'note': 'Geometry checks do not judge fidelity to the approved concept sheets. Review the 21 preview images and actual Unity rendering separately.'},
              'unityValidation': {'status': 'not_performed_by_this_validator',
                                  'note': 'Unity import, materials, prefabs and runtime remain outside this offline check. No Unity validation is claimed here.'},
              'assets': [], 'errors': []}
    try:
        folders = {path.name for path in (args.root / 'models').iterdir() if path.is_dir()}
        require(set(EXPECTED) <= folders, 'Missing model folders: ' + ', '.join(sorted(set(EXPECTED)-folders)))
    except (OSError, ValueError) as error:
        report['errors'].append(str(error))
    for code in selected:
        try:
            result = validate_asset(args.root, code)
            report['assets'].append(result)
            print('WEAPON_VALIDATED', code, 'triangles', [lod['triangles'] for lod in result['fbx']['lods']], flush=True)
        except Exception as error:
            message = code + ': ' + str(error)
            report['errors'].append(message)
            report['assets'].append({'code': code, 'status': 'failed', 'error': str(error)})
            print('WEAPON_VALIDATION_FAILED', message, flush=True)
            traceback.print_exc()
    passed = sum(asset['status'] == 'passed' for asset in report['assets'])
    report['summary'] = {'expectedAssets': 21, 'checkedAssets': len(selected),
                         'passedAssets': passed, 'failedAssets': len(selected)-passed,
                         'categories': len({code.rsplit('-',1)[0] for code in selected})}
    if not report['errors']:
        report['status'] = 'passed' if complete else 'partial_passed'
    report['technicalValidation']['status'] = report['status']
    output.write_text(json.dumps(report, indent=2, ensure_ascii=False)+'\n', encoding='utf-8')
    print('WEAPON_LIBRARY_VALIDATION', report['status'], passed, '/', len(selected), str(output), flush=True)
    return 0 if not report['errors'] else 1


if __name__ == '__main__':
    raise SystemExit(main())
