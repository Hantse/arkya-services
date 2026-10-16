"""Original stylized coloured mana seep and portable animated 3D wisps."""
import argparse
import json
import math
from pathlib import Path
import random
import struct
import sys

import bpy
import numpy as np
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[1]
ASSET = ROOT / 'assets'
PALETTE = 'bleu'
PALETTES = {
    'bleu': {'id': 'mare-bleue', 'label': 'Mana bleu azur', 'swatch': '#35a8d2', 'dark': (.035, .23, .42), 'mid': (.08, .53, .70), 'iris': ((.06, .72, .72), (.19, .40, .83)), 'core': (.37, .91, .99), 'edge': (.04, .37, .61)},
    'orange': {'id': 'mare-orange', 'label': 'Mana orange ambré', 'swatch': '#ee9b29', 'dark': (.43, .11, .018), 'mid': (.93, .39, .045), 'iris': ((1.0, .70, .13), (.91, .29, .055)), 'core': (1.0, .75, .31), 'edge': (.67, .24, .026)},
    'vert': {'id': 'mare-verte', 'label': 'Mana vert émeraude', 'swatch': '#3ac17c', 'dark': (.025, .29, .14), 'mid': (.08, .59, .30), 'iris': ((.16, .80, .38), (.16, .57, .40)), 'core': (.37, 1.0, .61), 'edge': (.04, .42, .21)},
}
TAU = math.tau
FPS = 24
DURATION = 6


def png(name, pixels, noncolor=False):
    path = ROOT / 'textures' / (PALETTE + '-' + name + '.png')
    im = bpy.data.images.new(name, width=pixels.shape[1], height=pixels.shape[0], alpha=True)
    im.colorspace_settings.name = 'Non-Color' if noncolor else 'sRGB'
    im.pixels.foreach_set(np.clip(pixels, 0, 1).astype(np.float32).ravel())
    im.filepath_raw = str(path); im.file_format = 'PNG'; im.save(); im.pack()
    return im


def paint_liquid():
    n = 2048
    pal = PALETTES[PALETTE]
    x, y = np.meshgrid(np.linspace(-1.52, 1.52, n, dtype=np.float32), np.linspace(-1.20, 1.20, n, dtype=np.float32))
    # Broad swept glazes: coherent liquid currents, deliberately no fractal noise.
    dx = x + .22 * np.sin(y * 3.0) + .10 * np.sin(x * 2 + y)
    dy = y + .18 * np.sin(x * 2.6) - .07 * np.sin(y * 3.2)
    r = np.sqrt((dx + .15) ** 2 + (dy - .04) ** 2)
    a = np.arctan2(dy - .04, dx + .15)
    flow = r * 8.1 - 2.0 * a + .55 * np.sin(a * 3.0 + r * 2.0)
    sweep = .5 + .5 * np.sin(flow)
    brush = .5 + .5 * np.sin(x * 3.1 + y * 4 + np.sin(y * 2.3))
    base = np.zeros((n, n, 3), np.float32)
    dark = np.array(pal['dark'], np.float32)
    indigo = np.array(pal['mid'], np.float32)
    base[:] = dark
    glaze = .21 + .42 * sweep + .14 * brush
    base = dark[None, None, :] * (1 - glaze[..., None]) + indigo[None, None, :] * glaze[..., None]
    # Oil-film iridescence forms broad broken sheets between the dark pools.
    centre_fade = np.clip(r / .20, 0, 1)
    film = np.exp(-((np.sin(flow) - .67) / .27) ** 2) * (.20 + .55 * (.5 + .5 * np.sin(a * 2.0 + r * .9))) * centre_fade
    tint = .5 + .5 * np.sin(a * 2.0 + r * 3.4)
    teal = np.array(pal['iris'][0], np.float32)
    violet = np.array(pal['iris'][1], np.float32)
    iris = teal * tint[..., None] + violet * (1 - tint[..., None])
    base = base * (1 - film[..., None]) + iris * film[..., None]
    # Thin paint accents are partially interrupted, leaving the centre liquid readable.
    filament = np.exp(-((np.sin(flow + .45) - .88) / .035) ** 2)
    mask = np.clip(np.sin(a * 3.0 + r * 2.0) - .1, 0, 1)
    filament *= mask * .42 * centre_fade
    base += filament[..., None] * np.array(pal['core']) * .52
    rough = .38 + .10 * (.5 + .5 * np.sin(flow + .7)) + .02 * brush
    emission = base * .075 + (iris * (film * .12)[..., None]) + filament[..., None] * np.array(pal['core']) * .4
    one = np.ones((n, n, 1), np.float32)
    return (png('mana-liquid-basecolor', np.concatenate((base, one), 2)),
            png('mana-liquid-roughness', np.concatenate((np.repeat(rough[..., None], 3, 2), one), 2), True),
            png('mana-liquid-emission', np.concatenate((emission, one), 2)))


def paint_wisp(name, cyan=True):
    n = 512
    u, v = np.meshgrid(np.linspace(0, 1, n, dtype=np.float32), np.linspace(0, 1, n, dtype=np.float32))
    side = np.abs((u - .5) * 2)
    alpha = np.exp(-side ** 2 * 5.5) * (1 - side) ** .55
    alpha *= np.clip(np.sin(v * math.pi), 0, 1) ** .62
    alpha *= .64 + .13 * np.sin(v * 10.0 + u * 2)
    # Soft luminous core, coloured edges. Texture alone remains readable without bloom.
    pal = PALETTES[PALETTE]
    edge = np.array(pal['edge'], np.float32)
    core = np.array(pal['core'], np.float32)
    if not cyan: core = core * .78 + np.array(pal['iris'][0]) * .22
    center = np.exp(-side ** 2 * 15.0)[..., None]
    color = edge * (1 - center) + core * center
    color *= (.70 + .25 * np.sin(v * math.pi))[..., None]
    return png(name, np.concatenate((color, alpha[..., None]), 2))


def basic_material(name, color, rough=.35, metal=0, emission=0):
    mat = bpy.data.materials.new(name); mat.use_nodes = True
    p = mat.node_tree.nodes.get('Principled BSDF')
    p.inputs['Base Color'].default_value = (*color, 1)
    p.inputs['Roughness'].default_value = rough
    p.inputs['Metallic'].default_value = metal
    p.inputs['Emission Color'].default_value = (*color, 1)
    p.inputs['Emission Strength'].default_value = emission
    mat.diffuse_color = (*color, 1)
    return mat


def materials():
    tex = paint_liquid()
    pal = PALETTES[PALETTE]
    mat = basic_material('Painted coloured mana | ' + PALETTE, pal['mid'], .43, .08)
    p = mat.node_tree.nodes.get('Principled BSDF')
    for image, socket in zip(tex, ('Base Color', 'Roughness', 'Emission Color')):
        node = mat.node_tree.nodes.new('ShaderNodeTexImage'); node.image = image
        mat.node_tree.links.new(node.outputs['Color'], p.inputs[socket])
    p.inputs['Emission Strength'].default_value = .8
    p.inputs['Specular IOR Level'].default_value = .25
    mats = [mat, basic_material('Wet irregular liquid edge', tuple(c * .8 for c in pal['mid']), .32, .10)]
    mats[1].node_tree.nodes.get('Principled BSDF').inputs['Specular IOR Level'].default_value = .25
    for name, cyan in [('Mana core | soft 3D ribbons', True), ('Mana halo | soft 3D ribbons', False)]:
        m = basic_material(name, (.1, .7, .8), .55, 0, 1.6)
        image = paint_wisp('mana-wisp-' + ('core' if cyan else 'halo'), cyan)
        p = m.node_tree.nodes.get('Principled BSDF')
        node = m.node_tree.nodes.new('ShaderNodeTexImage'); node.image = image
        m.node_tree.links.new(node.outputs['Color'], p.inputs['Base Color'])
        m.node_tree.links.new(node.outputs['Color'], p.inputs['Emission Color'])
        m.node_tree.links.new(node.outputs['Alpha'], p.inputs['Alpha'])
        m.surface_render_method = 'DITHERED'; m.use_backface_culling = False
        mats.append(m)
    mats.append(basic_material('Mana droplets | ' + PALETTE, pal['core'], .30, 0, 1.1))
    mats.append(basic_material('Broken surface current | ' + PALETTE, tuple(c * .75 for c in pal['core']), .32, .1, .6))
    return mats


def mesh_object(name, vertices, faces, mat, uvs=None):
    mesh = bpy.data.meshes.new(name); mesh.from_pydata(vertices, [], faces); mesh.update(); mesh.materials.append(mat)
    for p in mesh.polygons: p.use_smooth = True
    layer = mesh.uv_layers.new(name='Painted_UV')
    if uvs is None:
        uvs = [((v[0] + 1.52) / 3.04, (v[1] + 1.2) / 2.4) for v in vertices]
    for polygon in mesh.polygons:
        for loop in polygon.loop_indices: layer.data[loop].uv = uvs[mesh.loops[loop].vertex_index]
    obj = bpy.data.objects.new(name, mesh); bpy.context.scene.collection.objects.link(obj)
    return obj


def outline(a):
    return 1 + .073 * math.sin(3 * a + .3) + .055 * math.sin(5 * a - .8) + .029 * math.sin(8 * a + .9)


def pool(mat, edge):
    n = 160; rings = 24
    vertices = [(0, 0, .024)]; faces = []
    for j in range(1, rings + 1):
        r = j / rings
        for k in range(n):
            a = k / n * TAU; o = outline(a)
            x = 1.28 * r * o * math.cos(a); y = .96 * r * o * math.sin(a)
            z = .023 + .006 * math.sin(x * 3.5 + y * 2.7) * math.sin(r * math.pi)
            z += .008 * math.exp(-((r - .94) / .048) ** 2)
            if j == rings: z = .009
            vertices.append((x, y, z))
    for k in range(n): faces.append((0, 1 + k, 1 + (k + 1) % n))
    for j in range(rings - 1):
        for k in range(n):
            a = 1 + j * n + k; b = 1 + j * n + (k + 1) % n
            faces.append((a, a + n, b + n, b))
    # Close the underside and thin irregular perimeter: this is a puddle, no plinth.
    bottom = len(vertices); vertices.append((0, 0, .001))
    for k in range(n):
        a = 1 + (rings - 1) * n + k; b = 1 + (rings - 1) * n + (k + 1) % n
        faces.append((bottom, b, a))
    ob = mesh_object('Mana pool | irregular shallow liquid', vertices, faces, mat)
    # A few isolated damp lobes make the boundary a natural spill.
    drops = []
    for i, (a, radius, size) in enumerate([(.7, 1.09, .043), (2.7, 1.11, .032), (4.35, 1.12, .055), (5.6, 1.10, .024)]):
        loc = (1.28 * radius * math.cos(a), .96 * radius * math.sin(a), .005)
        drops.append(ellipsoid('Thin satellite spill %02d' % i, loc, (size * 1.4, size, .005), edge))
    return [ob] + drops


def ellipsoid(name, loc, scale, mat):
    # Explicit indexed triangles avoid operator-generated face ordering differences.
    n = 16; rings = 10; vertices = [(0, 0, scale[2])]; faces = []; uv = [(.5, 1)]
    for j in range(1, rings):
        theta = math.pi * j / rings
        for k in range(n):
            a = k / n * TAU
            vertices.append((scale[0] * math.sin(theta) * math.cos(a), scale[1] * math.sin(theta) * math.sin(a), scale[2] * math.cos(theta)))
            uv.append((k / n, 1 - j / rings))
    bottom = len(vertices); vertices.append((0, 0, -scale[2])); uv.append((.5, 0))
    for k in range(n): faces.append((0, 1 + k, 1 + (k + 1) % n))
    for j in range(rings - 2):
        for k in range(n):
            a = 1 + j * n + k; b = 1 + j * n + (k + 1) % n
            faces.extend(((a, a + n, b + n), (a, b + n, b)))
    for k in range(n): faces.append((bottom, 1 + (rings - 2) * n + (k + 1) % n, 1 + (rings - 2) * n + k))
    ob = mesh_object(name, vertices, faces, mat, uv); ob.location = loc
    return ob


def tube(name, points, radius, mat):
    vertices = []; faces = []; uvs = []; n = 7
    points = [Vector(p) for p in points]
    for j, p in enumerate(points):
        t = j / (len(points) - 1)
        axis = (points[min(j + 1, len(points) - 1)] - points[max(j - 1, 0)]).normalized()
        side = axis.cross(Vector((0, 0, 1)))
        if side.length < .01: side = axis.cross(Vector((0, 1, 0)))
        side.normalize(); up = axis.cross(side).normalized()
        r = radius * (.12 + .88 * math.sin(math.pi * t) ** .55)
        for k in range(n):
            a = k / n * TAU; vertices.append(tuple(p + (side * math.cos(a) + up * math.sin(a)) * r)); uvs.append((k / n, t))
    for j in range(len(points) - 1):
        for k in range(n): faces.append((j * n + k, j * n + (k + 1) % n, (j + 1) * n + (k + 1) % n, (j + 1) * n + k))
    faces.append(tuple(reversed(range(n)))); faces.append(tuple((len(points) - 1) * n + k for k in range(n)))
    return mesh_object(name, vertices, faces, mat, uvs)


def surface_currents(mat):
    objects = []
    for i, (cx, cy, rx, ry, start, length) in enumerate([
        (-.34, -.16, .45, .30, .15, 3.15), (.37, .27, .34, .22, 2.35, 3.6),
        (-.05, .02, .86, .59, 3.50, 1.4), (.08, -.05, .66, .42, .10, 1.05)]):
        points = []
        for j in range(44):
            t = j / 43; a = start + length * t
            r = 1 - .20 * t
            points.append((cx + rx * r * math.cos(a), cy + ry * r * math.sin(a), .035 + .002 * math.sin(t * math.pi)))
        objects.append(tube('Broken drifting current %02d' % i, points, .006 if i < 2 else .004, mat))
    return objects


def wisp(name, origin, height, width, angle, mat):
    vertices = []; faces = []; uvs = []; rows = 44; columns = 6
    # Two curved sheets cross through the same moving centre, remaining visible around the model.
    for cross in range(2):
        off = len(vertices)
        for j in range(rows):
            t = j / (rows - 1)
            center = Vector((.11 * math.sin(t * 5.9 + angle) - .11 * math.sin(angle), .07 * math.sin(t * 5.1 + angle * .7) - .07 * math.sin(angle * .7), .025 + height * t))
            a = angle + t * 1.7 + cross * math.pi / 2
            side = Vector((math.cos(a), math.sin(a), .04 * math.sin(t * TAU)))
            w = width * (.42 + .58 * math.sin(t * math.pi) ** .65)
            for k in range(columns):
                u = k / (columns - 1); p = center + side * (u - .5) * w
                vertices.append(tuple(p)); uvs.append((u, t))
        for j in range(rows - 1):
            for k in range(columns - 1):
                a = off + j * columns + k; faces.append((a, a + 1, a + 1 + columns, a + columns))
    obj = mesh_object(name, vertices, faces, mat, uvs); obj.location = origin
    return obj


def animate_cycle(obj, phase, kind, origin):
    for frame in range(1, FPS * DURATION + 2, 2):
        time = (frame - 1) / (FPS * DURATION)
        t = (time + phase) % 1.0
        # Remain invisible around the cyclic position reset, including sampled frames.
        fade_t = max(0.0, min(1.0, (t - .045) / .91))
        if kind == 'wisp':
            envelope = math.sin(fade_t * math.pi) ** .75
            s = max(.0001, envelope)
            obj.scale = (s, s, s * (.85 + .15 * t))
            obj.location = (origin[0] + .032 * math.sin(t * TAU), origin[1] + .025 * math.sin(t * TAU + phase * 4), origin[2] + .19 * t)
            obj.rotation_euler[2] = .10 * math.sin(t * TAU)
        else:
            envelope = math.sin(fade_t * math.pi) ** .80; s = max(.0001, envelope)
            obj.scale = (s, s, s)
            obj.location = (origin[0] + .055 * math.sin(t * 5 + phase * 6), origin[1] + .04 * math.sin(t * TAU + 1.0), origin[2] + .66 * t)
        obj.keyframe_insert(data_path='location', frame=frame)
        obj.keyframe_insert(data_path='scale', frame=frame)
        if kind == 'wisp': obj.keyframe_insert(data_path='rotation_euler', frame=frame)
    obj.animation_data.action.name = 'ManaFlow_' + obj.name
    for curve in obj.animation_data.action.fcurves:
        for key in curve.keyframe_points: key.interpolation = 'LINEAR'


def build():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene; scene.name = 'ManaFlow'; scene.render.fps = FPS; scene.frame_start = 1; scene.frame_end = FPS * DURATION + 1
    scene.unit_settings.system = 'METRIC'
    mats = materials(); objects = pool(mats[0], mats[1]) + surface_currents(mats[5])
    wisps = []
    params = [((-.62, .05, .02), .56, .22, .35, .16), ((.35, .34, .025), .70, .24, 1.75, .43), ((.62, -.28, .02), .49, .18, 3.2, .71), ((-.12, -.37, .018), .64, .21, 4.5, .90), ((-.17, .49, .018), .45, .17, 2.6, .29)]
    for i, (origin, height, width, angle, phase) in enumerate(params):
        ob = wisp('Rising mana wisp %02d' % i, origin, height, width, angle, mats[2 + (i % 3 == 1)])
        animate_cycle(ob, phase, 'wisp', origin); wisps.append(ob)
    rng = random.Random(1709)
    for i in range(9):
        a = rng.random() * TAU; r = rng.uniform(.18, .75)
        origin = (math.cos(a) * r, math.sin(a) * r * .7, .04 + rng.random() * .06)
        size = rng.uniform(.009, .017)
        ob = ellipsoid('Floating mana droplet %02d' % i, origin, (size * .67, size * .67, size * 1.42), mats[4])
        animate_cycle(ob, i / 9 + .06, 'spark', origin); wisps.append(ob)
    objects += wisps
    # A barely perceptible liquid pulse leaves the outer contact anchored at ground level.
    liquid = objects[0]
    for frame, scale in [(1, 1), (37, 1.025), (73, 1), (109, .975), (145, 1)]:
        liquid.scale = (1, 1, scale); liquid.keyframe_insert(data_path='scale', frame=frame)
    liquid.animation_data.action.name = 'ManaFlow_LiquidBreath'
    scene.frame_set(43)
    return objects


def studio(objects, draft):
    scene = bpy.context.scene; scene.render.engine = 'CYCLES'; scene.cycles.samples = 16 if draft else 40
    scene.cycles.use_denoising = True; scene.cycles.device = 'GPU'
    prefs = bpy.context.preferences.addons['cycles'].preferences; prefs.compute_device_type = 'OPTIX'; prefs.get_devices()
    for device in prefs.devices: device.use = device.type == 'OPTIX'
    scene.render.resolution_x = 700 if draft else 1100; scene.render.resolution_y = scene.render.resolution_x; scene.render.resolution_percentage = 100
    scene.world = bpy.data.worlds.new('Neutral dusk studio'); scene.world.use_nodes = True
    scene.world.node_tree.nodes['Background'].inputs[0].default_value = (.21, .25, .29, 1)
    scene.world.node_tree.nodes['Background'].inputs[1].default_value = .45
    bpy.ops.mesh.primitive_plane_add(size=200, location=(0, 0, -.002)); ground = bpy.context.object; ground.name = 'Preview floor | not exported'
    ground.data.materials.append(basic_material('Neutral slate backdrop', (.15, .17, .18), .88))
    for name, loc, energy, size in [('Large warm softbox', (-3, -4, 7), 750, 5), ('Cool broad fill', (4, -1, 4), 430, 4), ('Soft rim', (1, 4, 6), 700, 3)]:
        data = bpy.data.lights.new(name, 'AREA'); data.energy = energy; data.shape = 'DISK'; data.size = size
        ob = bpy.data.objects.new(name, data); scene.collection.objects.link(ob); ob.location = loc
        ob.rotation_euler = (Vector((0, 0, .1)) - ob.location).to_track_quat('-Z', 'Y').to_euler()
    data = bpy.data.cameras.new('Preview camera'); camera = bpy.data.objects.new('Preview camera', data); scene.collection.objects.link(camera); scene.camera = camera
    camera.location = (3.6, -5.4, 4.8); camera.rotation_euler = (Vector((0, 0, .18)) - camera.location).to_track_quat('-Z', 'Y').to_euler()
    data.type = 'ORTHO'; data.ortho_scale = 3.55
    scene.view_settings.view_transform = 'AgX'; scene.render.image_settings.file_format = 'PNG'
    return camera


def inspect_glb(path):
    raw = path.read_bytes(); length, kind = struct.unpack_from('<II', raw, 12)
    doc = json.loads(raw[20:20 + length]); errors = []
    triangles = sum(doc['accessors'][p['indices']]['count'] // 3 for m in doc['meshes'] for p in m['primitives'])
    animations = doc.get('animations', [])
    if len(animations) != 1: errors.append('Expected one combined animation')
    if animations:
        # Scene exporter uses the scene name. Assert rather than patch the GLB.
        names = [a.get('name') for a in animations]
        if names != ['ManaFlow']: errors.append('Animation name is not ManaFlow: ' + repr(names))
    if triangles > 25000: errors.append('Triangle budget exceeded')
    if len(doc.get('materials', [])) > 6: errors.append('Material budget exceeded')
    if any('uri' in i for i in doc.get('images', [])): errors.append('External image reference')
    nodes = {c['target']['node'] for a in animations for c in a['channels']}
    duration = max((doc['accessors'][s['input']]['max'][0] for a in animations for s in a['samplers']), default=0)
    if abs(duration - DURATION) > .001: errors.append('Incorrect animation duration')
    if len(nodes) != 15: errors.append('Expected 15 animated nodes, found ' + str(len(nodes)))
    report = {'success': not errors, 'errors': errors, 'triangles': triangles, 'materials': len(doc.get('materials', [])), 'embedded_images': len(doc.get('images', [])), 'animations': [a.get('name') for a in animations], 'animated_nodes': len(nodes), 'animation_duration': duration}
    (ASSET / 'validation.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    return report


def build_variant(args):
    stem = PALETTES[PALETTE]['id']
    ASSET.mkdir(parents=True, exist_ok=True); (ROOT / 'textures').mkdir(parents=True, exist_ok=True)
    objects = build()
    minimum = Vector((1e6, 1e6, 1e6)); maximum = -minimum
    for frame in range(1, FPS * DURATION + 2):
        bpy.context.scene.frame_set(frame)
        for ob in objects:
            for corner in ob.bound_box:
                p = ob.matrix_world @ Vector(corner)
                for axis in range(3):
                    minimum[axis] = min(minimum[axis], p[axis]); maximum[axis] = max(maximum[axis], p[axis])
    dimensions = maximum - minimum
    bpy.context.scene.frame_set(43)
    bpy.ops.object.select_all(action='DESELECT')
    for ob in objects: ob.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    target = ASSET / (stem + '.glb')
    bpy.ops.export_scene.gltf(filepath=str(target), export_format='GLB', use_selection=True, export_apply=True, export_animations=True, export_animation_mode='SCENE', export_anim_scene_split_object=False, export_anim_slide_to_zero=True, export_force_sampling=True, export_frame_range=True, export_frame_step=1, export_optimize_animation_size=False, export_materials='EXPORT', export_cameras=False, export_lights=False, export_tangents=True)
    report = inspect_glb(target); print('MANA_GLB', json.dumps(report), flush=True)
    camera = studio(objects, args.draft)
    scene = bpy.context.scene; scene.frame_set(43); scene.render.filepath = str(ASSET / (stem + '-preview.png')); bpy.ops.render.render(write_still=True)
    if not args.draft:
        for frame in [10, 100]:
            scene.frame_set(frame); scene.render.filepath = str(ASSET / (stem + '-pose-%03d.png' % frame)); bpy.ops.render.render(write_still=True)
        scene.frame_set(43)
    bpy.context.preferences.filepaths.save_version = 0
    text = bpy.data.texts.new('Arkya mana seep | authorship and setup')
    text.write('Original Arkya DA proposal. Coloured painted oil-like mana seep in blue, orange or green, with 5 soft crossed 3D ribbon wisps, 9 floating droplets and 6-second ManaFlow loop. Generator scripts/generate_mana_pool.py. Base colour, roughness and emission painted procedurally into PNGs; portable glTF Principled materials, no simulation or volume requirement. Studio floor, lights and camera excluded from GLB. All textures packed. Metres. Artistic status: proposed, pending user validation.')
    bpy.ops.file.pack_all(); bpy.ops.wm.save_as_mainfile(filepath=str(ASSET / (stem + '.blend')))
    record = {'id': stem, 'shape': 'mare', 'material': PALETTE, 'label': 'Mare de mana · ' + PALETTES[PALETTE]['label'], 'glb': f'assets/{stem}/{stem}.glb', 'blend': f'assets/{stem}/{stem}.blend', 'preview': f'assets/{stem}/{stem}-preview.png', 'height': round(dimensions.z, 4), 'width': round(dimensions.x, 4), 'depth': round(dimensions.y, 4), 'triangles': report['triangles'], 'texture_resolution': [2048, 2048], 'opaque': False, 'pivot': 'centre au sol', 'animation': 'ManaFlow', 'animation_duration': DURATION, 'dimension_note': 'Enveloppe maximale de la boucle complète, particules comprises.'}
    old = json.loads((ROOT / 'manifest.json').read_text(encoding='utf-8')) if (ROOT / 'manifest.json').exists() else {}
    records = [a for a in old.get('assets', []) if a['material'] in PALETTES and a['material'] != PALETTE] + [record]
    manifest = {'family': 'mares-de-mana', 'artistic_status': 'proposed', 'shapes': [{'id': 'mare', 'label': 'Mare de mana', 'height': record['height'], 'description': 'Mare organique aplatie, liquide coloré irisé et cinq volutes de mana animées.'}], 'materials': [{'id': key, 'label': p['label'], 'swatch': p['swatch'], 'description': 'Liquide peint coloré et volutes ascendantes assorties.'} for key, p in PALETTES.items()], 'assets': sorted(records, key=lambda a: list(PALETTES).index(a['material']))}
    (ROOT / 'manifest.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    (ASSET / (stem + '.json')).write_text(json.dumps(record, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    if not report['success']: raise RuntimeError(report['errors'])
    print('MANA_READY', str(ASSET), flush=True)


def main():
    global ASSET, PALETTE
    parser = argparse.ArgumentParser(); parser.add_argument('--draft', action='store_true'); parser.add_argument('--palette', choices=['all'] + list(PALETTES), default='all')
    args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else [])
    for palette in list(PALETTES) if args.palette == 'all' else [args.palette]:
        PALETTE = palette; ASSET = ROOT / 'assets' / PALETTES[PALETTE]['id']; build_variant(args)


if __name__ == '__main__': main()
