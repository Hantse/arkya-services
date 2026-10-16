"""New mana silhouettes using frozen approved materials, textures and animation helpers."""
import argparse
import hashlib
import json
import math
from pathlib import Path
import random
import sys

import bpy
from mathutils import Vector

sys.path.insert(0, str(Path(__file__).resolve().parent))
import generate_mana_pool as base
import validate_mana_pool as validator

ROOT = Path(__file__).resolve().parents[1]
SHAPES = {
    'allongee': {'label': 'Petite mare allongée', 'description': 'Flaque étroite et sinueuse, une extrémité gonflée et trois volutes réparties le long du courant.', 'width': 1.80, 'depth': .80, 'wisps': 3, 'droplets': 6},
    'lobee': {'label': 'Grande mare à lobes', 'description': 'Large nappe asymétrique découpée en lobes arrondis, sept sources de mana et courants séparés.', 'width': 3.50, 'depth': 2.80, 'wisps': 7, 'droplets': 12},
}
APPROVED_IDS = ['mare-bleue', 'mare-orange', 'mare-verte']


def verify_reference():
    document = json.loads((ROOT / 'approved-reference.json').read_text(encoding='utf-8'))
    for record in document['files']:
        assert hashlib.sha256((ROOT / record['file']).read_bytes()).hexdigest().lower() == record['sha256'].lower(), record['file']
    return len(document['files'])


def approved_materials(palette):
    stem = base.PALETTES[palette]['id']
    names = ['Painted coloured mana | ' + palette, 'Wet irregular liquid edge', 'Mana core | soft 3D ribbons', 'Mana halo | soft 3D ribbons', 'Mana droplets | ' + palette, 'Broken surface current | ' + palette]
    with bpy.data.libraries.load(str(ROOT / 'assets' / stem / (stem + '.blend')), link=False) as (source, target):
        assert all(name in source.materials for name in names)
        target.materials = names
    assert len(target.materials) == 6 and all(target.materials)
    return target.materials


def contour(shape, angle):
    if shape == 'allongee':
        r = 1 + .10 * math.cos(3 * angle - .65) + .055 * math.cos(5 * angle + .4)
        x = .90 * r * math.cos(angle)
        y = .29 * r * math.sin(angle) * (1 - .24 * math.cos(angle)) + .085 * math.sin(x * 2.7)
    else:
        r = 1 + .18 * math.cos(5 * angle + .45) + .075 * math.sin(3 * angle - .2) + .025 * math.sin(8 * angle)
        x = 1.55 * r * math.cos(angle) + .08 * math.sin(angle * 2)
        y = 1.23 * r * math.sin(angle)
    return Vector((x, y, 0))


def boundary_points(shape, n):
    boundary = [contour(shape, k / n * math.tau) for k in range(n)]
    low = Vector((min(p.x for p in boundary), min(p.y for p in boundary), 0)); high = Vector((max(p.x for p in boundary), max(p.y for p in boundary), 0))
    centre = (low + high) / 2
    width = SHAPES[shape]['width'] * .94; depth = SHAPES[shape]['depth'] * .94
    return [Vector(((p.x - centre.x) * width / (high.x - low.x), (p.y - centre.y) * depth / (high.y - low.y), 0)) for p in boundary]


def pool(shape, mats):
    n, rings = (144, 20) if shape == 'allongee' else (192, 22)
    boundary = boundary_points(shape, n)
    width = SHAPES[shape]['width'] * .94; depth = SHAPES[shape]['depth'] * .94
    vertices = [(0, 0, .024)]; faces = []; uv = [(.5, .5)]
    for j in range(1, rings + 1):
        r = j / rings
        for p in boundary:
            x, y = p.x * r, p.y * r
            z = .023 + .006 * math.sin(x * 3.5 + y * 2.7) * math.sin(r * math.pi)
            z += .008 * math.exp(-((r - .94) / .048) ** 2)
            if j == rings: z = .009
            vertices.append((x, y, z)); uv.append((.5 + x / (width * 1.10), .5 + y / (depth * 1.10)))
    for k in range(n): faces.append((0, 1 + k, 1 + (k + 1) % n))
    for j in range(rings - 1):
        for k in range(n):
            a = 1 + j * n + k; b = 1 + j * n + (k + 1) % n
            faces.append((a, a + n, b + n, b))
    bottom = len(vertices); vertices.append((0, 0, .001)); uv.append((.5, .5))
    for k in range(n):
        a = 1 + (rings - 1) * n + k; b = 1 + (rings - 1) * n + (k + 1) % n
        faces.append((bottom, b, a))
    liquid = base.mesh_object('Mana ' + shape + ' | sculpted shallow liquid', vertices, faces, mats[0], uv)
    objects = [liquid]
    count = 3 if shape == 'allongee' else 4
    for i in range(count):
        a = .55 + i * math.tau / count
        p = boundary[int(a / math.tau * n) % n] * 1.09
        size = (.016 if shape == 'allongee' else .035) * (1 + i * .15)
        objects.append(base.ellipsoid('Satellite wet spill %02d' % i, (p.x, p.y, .005), (size * 1.4, size, .005), mats[1]))
    return objects


def currents(shape, mat):
    boundary = boundary_points(shape, 192)
    def inside(x, y):
        result = False
        for i, p in enumerate(boundary):
            q = boundary[(i + 1) % len(boundary)]
            if (p.y > y) != (q.y > y) and x < (q.x - p.x) * (y - p.y) / (q.y - p.y) + p.x: result = not result
        return result
    specs = ([(-.32, -.025, .27, .13, .35, 3.2), (.35, .04, .25, .12, 2.7, 3.4)] if shape == 'allongee' else [
        (-.78, .30, .44, .31, .3, 3.0), (.64, -.37, .42, .29, 2.5, 3.2),
        (.18, .67, .53, .28, .2, 2.1), (-.36, -.57, .54, .29, 3.2, 1.8),
        (.10, .01, .62, .44, 2.8, 2.0)])
    objects = []
    for i, (cx, cy, rx, ry, start, length) in enumerate(specs):
        points = []
        for j in range(44):
            t = j / 43; a = start + length * t; r = 1 - .20 * t
            points.append((cx + rx * r * math.cos(a), cy + ry * r * math.sin(a), .035 + .002 * math.sin(t * math.pi)))
        # Keep every accent over liquid with a small inset from the sculpted boundary.
        for _ in range(60):
            if all(inside(p[0] + dx, p[1] + dy) for p in points for dx, dy in [(-.025, 0), (.025, 0), (0, -.025), (0, .025)]): break
            points = [(x * .97, y * .97, z) for x, y, z in points]
        objects.append(base.tube('Sculpted current %02d' % i, points, .0038 if shape == 'allongee' else .0065, mat))
    return objects


def build(shape, palette):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene; scene.name = 'ManaFlow'; scene.render.fps = 24; scene.frame_start = 1; scene.frame_end = 145; scene.unit_settings.system = 'METRIC'
    mats = approved_materials(palette); objects = pool(shape, mats) + currents(shape, mats[5])
    if shape == 'allongee':
        params = [((-.53, -.03, .02), .35, .14, .4, .12), ((-.03, -.03, .02), .48, .17, 2.0, .44), ((.50, .10, .02), .34, .14, 4.0, .76)]
    else:
        params = [((-.99, .25, .02), .61, .23, .4, .09), ((-.50, -.65, .02), .65, .24, 1.3, .23), ((.51, -.70, .02), .55, .22, 2.1, .37),
                  ((1.13, -.10, .02), .72, .25, 3.0, .51), ((.67, .65, .02), .77, .27, 3.8, .65), ((-.25, .84, .02), .86, .27, 4.5, .79), ((.02, .06, .02), .76, .24, 5.3, .93)]
    for i, (origin, height, width, angle, phase) in enumerate(params):
        ob = base.wisp('Mana ' + shape + ' | rising wisp %02d' % i, origin, height, width, angle, mats[2 + (i % 3 == 1)])
        base.animate_cycle(ob, phase, 'wisp', origin); objects.append(ob)
    rng = random.Random(2491 if shape == 'allongee' else 3842)
    for i in range(SHAPES[shape]['droplets']):
        if shape == 'allongee': origin = (rng.uniform(-.62, .62), rng.uniform(-.10, .16), .04 + rng.random() * .03)
        else:
            a = rng.random() * math.tau; r = rng.uniform(.20, .90)
            origin = (math.cos(a) * r * 1.25, math.sin(a) * r, .04 + rng.random() * .06)
        size = rng.uniform(.008, .014) if shape == 'allongee' else rng.uniform(.011, .020)
        ob = base.ellipsoid('Mana ' + shape + ' | floating droplet %02d' % i, origin, (size * .67, size * .67, size * 1.42), mats[4])
        base.animate_cycle(ob, i / SHAPES[shape]['droplets'] + .06, 'spark', origin)
        if shape == 'allongee':
            # Reduce the spark rise to match the smaller wisp silhouette.
            curve = next(c for c in ob.animation_data.action.fcurves if c.data_path == 'location' and c.array_index == 2)
            for key in curve.keyframe_points: key.co.y = origin[2] + (key.co.y - origin[2]) * .64
        objects.append(ob)
    liquid = objects[0]
    for frame, scale in [(1, 1), (37, 1.025), (73, 1), (109, .975), (145, 1)]:
        liquid.scale = (1, 1, scale); liquid.keyframe_insert(data_path='scale', frame=frame)
    liquid.animation_data.action.name = 'ManaFlow_LiquidBreath'
    scene.frame_set(43)
    return objects


def dimensions(objects):
    low = Vector((1e6, 1e6, 1e6)); high = -low
    for frame in range(1, 146):
        bpy.context.scene.frame_set(frame)
        for obj in objects:
            for corner in obj.bound_box:
                p = obj.matrix_world @ Vector(corner)
                for axis in range(3): low[axis] = min(low[axis], p[axis]); high[axis] = max(high[axis], p[axis])
    bpy.context.scene.frame_set(43)
    return high - low


def update_manifest(record):
    manifest = json.loads((ROOT / 'manifest.json').read_text(encoding='utf-8'))
    manifest['artistic_status'] = 'approved_reference_with_proposed_variants'; manifest['approved_at'] = '2026-10-15'; manifest['approved_asset_ids'] = APPROVED_IDS
    original = next(s for s in manifest['shapes'] if s['id'] == 'mare'); original['artistic_status'] = 'approved'
    manifest['shapes'] = [original] + [dict(id=s, label=values['label'], description=values['description'], height=record['height'] if s == record['shape'] else next((a['height'] for a in manifest['assets'] if a['shape'] == s), .58 if s == 'allongee' else .95), artistic_status='proposed') for s, values in SHAPES.items()]
    assets = [a for a in manifest['assets'] if a['id'] != record['id']] + [record]
    for asset in assets:
        asset['artistic_status'] = 'approved' if asset['id'] in APPROVED_IDS else 'proposed'
        if asset['id'] in APPROVED_IDS:
            asset['approved_at'] = '2026-10-15'; asset['animated_nodes'] = 15; asset['wisp_count'] = 5; asset['droplet_count'] = 9
    manifest['assets'] = sorted(assets, key=lambda a: (['mare', 'allongee', 'lobee'].index(a['shape']), list(base.PALETTES).index(a['material'])))
    (ROOT / 'manifest.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')


def export_variant(shape, palette, draft=False):
    objects = build(shape, palette); extent = dimensions(objects)
    stem = shape + '-' + palette; folder = ROOT / 'assets' / stem; folder.mkdir(parents=True, exist_ok=True)
    bpy.ops.object.select_all(action='DESELECT')
    for obj in objects: obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    bpy.ops.export_scene.gltf(filepath=str(folder / (stem + '.glb')), export_format='GLB', use_selection=True, export_apply=True, export_animations=True, export_animation_mode='SCENE', export_anim_scene_split_object=False, export_anim_slide_to_zero=True, export_force_sampling=True, export_frame_range=True, export_frame_step=1, export_optimize_animation_size=False, export_materials='EXPORT', export_cameras=False, export_lights=False, export_tangents=False)
    doc, binary = validator.glb_read(folder / (stem + '.glb'))
    triangles = sum(doc['accessors'][p['indices']]['count'] // 3 for m in doc['meshes'] for p in m['primitives'])
    assert triangles <= 25000, triangles
    camera = base.studio(objects, draft); camera.data.ortho_scale = extent.x * 1.28
    scene = bpy.context.scene; scene.frame_set(43); scene.render.filepath = str(folder / (stem + '-preview.png')); bpy.ops.render.render(write_still=True)
    if not draft:
        for frame in [10, 100]:
            scene.frame_set(frame); scene.render.filepath = str(folder / (stem + '-pose-%03d.png' % frame)); bpy.ops.render.render(write_still=True)
        scene.frame_set(43)
    text = bpy.data.texts.new('Arkya mana shape | provenance')
    text.write('Original additional mana silhouette. Reuses frozen approved Arkya mana textures and materials from 2026-10-15. Sculpted shallow liquid, soft crossed ribbons, 6-second ManaFlow animation. All textures packed; metres. Generator: scripts/generate_mana_shapes.py. Artistic status: proposal; original mare in all three colours is approved. Studio floor, camera and lighting excluded from GLB.')
    bpy.context.preferences.filepaths.save_version = 0; bpy.ops.file.pack_all(); bpy.ops.wm.save_as_mainfile(filepath=str(folder / (stem + '.blend')))
    record = {'id': stem, 'shape': shape, 'material': palette, 'label': SHAPES[shape]['label'] + ' · ' + base.PALETTES[palette]['label'], 'glb': f'assets/{stem}/{stem}.glb', 'blend': f'assets/{stem}/{stem}.blend', 'preview': f'assets/{stem}/{stem}-preview.png', 'height': round(extent.z, 4), 'width': round(extent.x, 4), 'depth': round(extent.y, 4), 'triangles': triangles, 'texture_resolution': [2048, 2048], 'opaque': False, 'pivot': 'centre au sol', 'animation': 'ManaFlow', 'animation_duration': 6, 'animated_nodes': 1 + SHAPES[shape]['wisps'] + SHAPES[shape]['droplets'], 'wisp_count': SHAPES[shape]['wisps'], 'droplet_count': SHAPES[shape]['droplets'], 'artistic_status': 'proposed', 'dimension_note': 'Enveloppe maximale de la boucle complète, particules comprises.'}
    (folder / (stem + '.json')).write_text(json.dumps(record, ensure_ascii=False, indent=2) + '\n', encoding='utf-8'); update_manifest(record)
    print('MANA_SHAPE_READY', json.dumps(record, ensure_ascii=False), flush=True)


def main():
    parser = argparse.ArgumentParser(); parser.add_argument('--draft', action='store_true'); parser.add_argument('--shape', choices=['all'] + list(SHAPES), default='all'); parser.add_argument('--palette', choices=['all'] + list(base.PALETTES), default='all')
    args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else [])
    print('FROZEN_FILES_VERIFIED', verify_reference(), flush=True)
    for shape in list(SHAPES) if args.shape == 'all' else [args.shape]:
        for palette in list(base.PALETTES) if args.palette == 'all' else [args.palette]: export_variant(shape, palette, args.draft)
    print('FROZEN_FILES_UNCHANGED', verify_reference(), flush=True)


if __name__ == '__main__': main()
