"""Reproducible Arkya building modules, metres, painted maps and true apertures.

Blender 4.5: blender -b --python generate_structures.py -- --shape all --material all --render --gpu
Only the asset mesh and six snap empties are exported. Studio objects stay in .blend.
"""
import argparse
import json
import math
from pathlib import Path
import random
import sys

import bpy
import bmesh
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[1]
WOOD_TILE_METRES = (1.2, 2.4)
WOOD_TINTS = {'bois': (1., .97, .91, 1.), 'timber': (.78, .76, .71, 1.)}
SHAPES = {
    'fondation': {'label': 'Fondation', 'width': 1.0, 'height': .5, 'depth': 1.0,
                  'description': 'Socle carré de 1 m, haut de 50 cm.'},
    'mur-plein': {'label': 'Mur plein', 'width': 1.0, 'height': 1.0, 'depth': .15,
                  'description': 'Panneau plein de 1 m de côté, épais de 15 cm.'},
    'mur-fenetre': {'label': 'Mur avec fenêtre', 'width': 1.0, 'height': 1.0, 'depth': .15,
                    'description': 'Vraie baie centrale de 50 × 50 cm, allège à 30 cm.',
                    'opening': {'width': .5, 'height': .5, 'bottom': .3, 'top': .8}},
    'mur-porte': {'label': 'Mur avec porte', 'width': 1.0, 'height': 2.0, 'depth': .15,
                  'description': 'Passage libre de 72 × 185 cm, sans seuil.',
                  'opening': {'width': .72, 'height': 1.85, 'bottom': 0., 'top': 1.85}},
    'plafond': {'label': 'Plafond', 'width': 1.0, 'height': .1, 'depth': 1.0,
                'description': 'Dalle carrée de 1 m, épaisse de 10 cm.'},
}
PALETTES = {
    'chaume': {'label': 'Chaume', 'swatch': '#c2a461',
               'description': 'Faisceaux dorés tressés, ossature de bois et liens de corde.'},
    'bois': {'label': 'Bois', 'swatch': '#94704c',
             'description': 'Larges planches sciées, cadres chauds et chevilles discrètes.'},
    'pierre': {'label': 'Pierre', 'swatch': '#87989e',
               'description': 'Blocs appareillés adoucis, variations peintes et mortier froid.'},
}


def material(key):
    name = 'Arkya | ' + key + ' | painted'
    if name in bpy.data.materials:
        return bpy.data.materials[name]
    path = ROOT / 'textures' / (key + '-basecolor.png')
    if not path.is_file():
        raise FileNotFoundError('Required painted texture: ' + str(path))
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    shader = mat.node_tree.nodes.get('Principled BSDF')
    shader.inputs['Roughness'].default_value = .96 if key in WOOD_TINTS else {'pierre': .86, 'mortar': .95, 'rope': .93}.get(key, .8)
    shader.inputs['Metallic'].default_value = 0
    shader.inputs['Specular IOR Level'].default_value = .23
    image = bpy.data.images.load(str(path), check_existing=True)
    image.colorspace_settings.name = 'sRGB'
    image.pack()
    tex = mat.node_tree.nodes.new('ShaderNodeTexImage')
    tex.image = image
    tex.interpolation = 'Linear'
    tex.extension = 'REPEAT'
    if key in WOOD_TINTS:
        tint = mat.node_tree.nodes.new('ShaderNodeMix')
        tint.name = 'Arkya worked timber tint'
        tint.data_type = 'RGBA'
        tint.blend_type = 'MULTIPLY'
        tint.inputs[0].default_value = 1.
        tint.inputs[7].default_value = WOOD_TINTS[key]
        mat.node_tree.links.new(tex.outputs['Color'], tint.inputs[6])
        mat.node_tree.links.new(tint.outputs[2], shader.inputs['Base Color'])
        mat.diffuse_color = WOOD_TINTS[key]
        mat['arkya_uv_tile_metres'] = WOOD_TILE_METRES
        mat['arkya_wood_revision'] = 2
    else:
        mat.node_tree.links.new(tex.outputs['Color'], shader.inputs['Base Color'])
    return mat


class Builder:
    """Independent softly beveled components merged with explicit, oriented UVs."""
    def __init__(self, palette):
        self.vertices = []
        self.faces = []
        self.uvs = []
        self.indices = []
        self.keys = [palette, 'timber', 'rope'] if palette != 'pierre' else ['pierre', 'mortar']
        self.rng = random.Random(1387)

    def box(self, name, limits, key, bevel=.009, grain_axis=2, irregular=False):
        x0, x1, y0, y1, z0, z1 = limits
        dims = Vector((x1 - x0, y1 - y0, z1 - z0))
        if min(dims) < .0005:
            return
        center = Vector(((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2))
        bm = bmesh.new()
        bmesh.ops.create_cube(bm, size=1)
        for vert in bm.verts:
            vert.co = Vector((vert.co.x * dims.x, vert.co.y * dims.y, vert.co.z * dims.z))
        radius = min(bevel, min(dims) * .23)
        bmesh.ops.bevel(bm, geom=list(bm.edges), offset=radius, segments=3,
                        affect='EDGES', profile=.55)
        # Variations stay in the module envelope. Main contact planes remain deliberate.
        if irregular:
            phase = self.rng.uniform(0, math.tau)
            for vert in bm.verts:
                for axis in range(3):
                    half = dims[axis] / 2
                    if abs(vert.co[axis]) < half - radius * .4:
                        vert.co[axis] += min(radius * .23, .0017) * math.sin(
                            vert.co[(axis + 1) % 3] * 31 + phase + axis * 2)
                        vert.co[axis] = max(-half, min(half, vert.co[axis]))
        bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
        bm.verts.ensure_lookup_table()
        bm.verts.index_update()
        offset = len(self.vertices)
        self.vertices.extend(tuple(v.co + center) for v in bm.verts)
        # Different crops avoid identical knots and washes on neighbouring boards.
        u0 = self.rng.uniform(.025, .25)
        v0 = self.rng.uniform(.025, .18)
        uscale = self.rng.uniform(.60, .72)
        vscale = self.rng.uniform(.70, .80)
        rotation = 0
        if key == 'pierre':
            rotation = self.rng.randrange(4)
            uscale, vscale = self.rng.uniform(.40, .82), self.rng.uniform(.40, .82)
            u0, v0 = self.rng.uniform(.02, .98-uscale), self.rng.uniform(.02, .98-vscale)
        for face in bm.faces:
            normal_axis = max(range(3), key=lambda a: abs(face.normal[a]))
            axes = [a for a in range(3) if a != normal_axis]
            va = grain_axis if grain_axis in axes else axes[-1]
            ua = next(a for a in axes if a != va)
            uv = []
            for vert in face.verts:
                u = vert.co[ua] / dims[ua] + .5
                v = vert.co[va] / dims[va] + .5
                for _ in range(rotation):
                    u, v = 1-v, u
                if key in WOOD_TINTS:
                    # Preserve the component's grain axis and deterministic geometry draws.
                    uv.append((u0 + vert.co[ua] / WOOD_TILE_METRES[0],
                               v0 + vert.co[va] / WOOD_TILE_METRES[1]))
                else:
                    uv.append((u0 + u * uscale, v0 + v * vscale))
            self.faces.append(tuple(v.index + offset for v in face.verts))
            self.uvs.append(uv)
            self.indices.append(self.keys.index(key))
        bm.free()

    def rope(self, start, end, radius=.004):
        a, b = Vector(start), Vector(end)
        direction = (b - a).normalized()
        side = direction.cross(Vector((0, 0, 1)))
        if side.length < .001:
            side = direction.cross(Vector((0, 1, 0)))
        side.normalize()
        across = direction.cross(side).normalized()
        n, rings = 8, 4
        base = len(self.vertices)
        for k in range(rings):
            t = k / (rings - 1)
            center = a.lerp(b, t)
            for j in range(n):
                theta = j * math.tau / n
                rr = radius * (1 + .035 * math.sin(j * 3 + t * 9))
                p = center + rr * (side * math.cos(theta) + across * math.sin(theta))
                self.vertices.append(tuple(p))
        for k in range(rings - 1):
            for j in range(n):
                self.faces.append((base+k*n+j, base+k*n+(j+1)%n,
                                   base+(k+1)*n+(j+1)%n, base+(k+1)*n+j))
                self.uvs.append(((j/n, k/(rings-1)), ((j+1)/n, k/(rings-1)),
                                 ((j+1)/n, (k+1)/(rings-1)), (j/n, (k+1)/(rings-1))))
                self.indices.append(self.keys.index('rope'))
        for k, reverse in [(0, True), (rings-1, False)]:
            row = list(range(base+k*n, base+(k+1)*n))
            if reverse:
                row.reverse()
            self.faces.append(tuple(row))
            self.uvs.append(tuple((.5+.45*math.cos(j*math.tau/n),
                                   .5+.45*math.sin(j*math.tau/n)) for j in range(n)))
            self.indices.append(self.keys.index('rope'))

    def object(self, name, root):
        mesh = bpy.data.meshes.new(name + '_Mesh')
        mesh.from_pydata(self.vertices, [], self.faces)
        mesh.update()
        for key in self.keys:
            mesh.materials.append(material(key))
        layer = mesh.uv_layers.new(name='PaintedUV')
        for poly, uv, index in zip(mesh.polygons, self.uvs, self.indices):
            poly.material_index = index
            poly.use_smooth = True
            for loop, co in zip(poly.loop_indices, uv):
                layer.data[loop].uv = co
        obj = bpy.data.objects.new(name, mesh)
        bpy.context.scene.collection.objects.link(obj)
        obj.parent = root
        bpy.context.view_layer.objects.active = obj
        obj.select_set(True)
        modifier = obj.modifiers.new('Intentional flat faces and soft edges', 'WEIGHTED_NORMAL')
        modifier.keep_sharp = True
        modifier.weight = 60
        bpy.ops.object.modifier_apply(modifier=modifier.name)
        obj['arkya_role'] = 'presentation_mesh'
        return obj


def regions(shape):
    cfg = SHAPES[shape]
    h = cfg['height']
    opening = cfg.get('opening')
    if not opening:
        return [(-.5, .5, 0, h)]
    half = opening['width'] / 2
    bottom, top = opening['bottom'], opening['top']
    result = [(-.5, -half, 0, h), (half, .5, 0, h), (-half, half, top, h)]
    if bottom > 0:
        result.append((-half, half, 0, bottom))
    return result


def timber_wall(builder, shape, palette):
    cfg = SHAPES[shape]
    h = cfg['height']
    opening = cfg.get('opening')
    frame = .045 if shape == 'mur-porte' else .055
    # Full outer posts establish repeatable metre joins; exposed tops stay flat.
    for x0, x1 in [(-.5, -.5+frame), (.5-frame, .5)]:
        builder.box('Outer post', (x0, x1, -.075, .075, 0, h), 'timber', .009)
    builder.box('Top rail', (-.5+frame, .5-frame, -.075, .075, h-frame, h), 'timber', .008, 0)
    if shape != 'mur-porte':
        builder.box('Bottom rail', (-.5+frame, .5-frame, -.075, .075, 0, frame), 'timber', .008, 0)
    if opening:
        half, bottom, top = opening['width']/2, opening['bottom'], opening['top']
        for x0, x1 in [(-half-frame, -half), (half, half+frame)]:
            # Continuous stiles close the four infill corners while keeping the clear bay exact.
            builder.box('Aperture jamb', (x0, x1, -.071, .071,
                                         frame if shape != 'mur-porte' else 0, h-frame), 'timber', .007)
        builder.box('Aperture head', (-half, half, -.071, .071, top, top+frame), 'timber', .007, 0)
        if bottom:
            builder.box('Window sill', (-half-frame, half+frame, -.075, .075,
                                        bottom-frame, bottom), 'timber', .007, 0)
    # Clip panels to a rectangular mask, leaving precisely clear door/window volumes.
    for rx0, rx1, rz0, rz1 in regions(shape):
        x0, x1 = max(rx0, -.5+frame), min(rx1, .5-frame)
        z0, z1 = max(rz0, frame if shape != 'mur-porte' else 0), min(rz1, h-frame)
        if opening:
            half, bottom, top = opening['width']/2, opening['bottom'], opening['top']
            if rx1 <= -half: x1 -= frame
            if rx0 >= half: x0 += frame
            if rz0 >= top: z0 += frame
            if rz1 <= bottom: z1 -= frame
        if min(x1-x0, z1-z0) < .002:
            continue
        if palette == 'bois':
            count = max(1, math.ceil((x1-x0)/.18))
            for index in range(count):
                left, right = x0+(x1-x0)*index/count, x0+(x1-x0)*(index+1)/count
                gap = .003 if count > 1 else .001
                builder.box('Broad sawn plank', (left+gap/2, right-gap/2, -.052, .052, z0, z1),
                            'bois', .008, 2, True)
            # A single inner batten underlines the carpentry without hiding the broad planks.
            if z1-z0 > .40:
                for y0, y1 in [(-.065, -.052), (.052, .065)]:
                    z = z0+(z1-z0)*.34
                    builder.box('Cross batten', (x0, x1, y0, y1, z, z+.035), 'timber', .004, 0)
        else:
            count = max(1, math.ceil((x1-x0)/.058))
            for index in range(count):
                left, right = x0+(x1-x0)*index/count, x0+(x1-x0)*(index+1)/count
                builder.box('Pressed straw bundle', (left, right, -.049, .049, z0, z1),
                            'chaume', .011, 2, True)
            # Both faces are woven; the tie lines are real geometry, never a flat cutout.
            rows = max(1, round((z1-z0)/.17))
            for row in range(rows):
                z = z0+(row+.5)*(z1-z0)/rows
                for y in [-.056, .056]:
                    builder.rope((x0+.004, y, z), (x1-.004, y, z), .0035)
    # Four restrained square wooden pegs / rope wraps emphasize the frame joints.
    if palette == 'chaume':
        for x in [-.5+frame/2, .5-frame/2]:
            for z in [min(.10, h*.1), h-min(.10, h*.1)]:
                for offset in [-.009, 0, .009]:
                    builder.rope((x-frame*.35, -.072, z+offset), (x+frame*.35, -.072, z+offset), .0023)
                    builder.rope((x-frame*.35, .072, z+offset), (x+frame*.35, .072, z+offset), .0023)
    else:
        for x in [-.5+frame/2, .5-frame/2]:
            for z in [frame*.6, h-frame*.6]:
                for sign in [-1, 1]:
                    builder.box('Wood peg', (x-.007, x+.007, sign*.073-.002, sign*.073+.002,
                                             z-.007, z+.007), 'bois', .002, 2)


def stone_wall(builder, shape):
    h = SHAPES[shape]['height']
    mask = regions(shape)
    for x0, x1, z0, z1 in mask:
        builder.box('Cool mortar backing', (x0, x1, -.065, .065, z0, z1), 'mortar', .003, 0)
    rows = round(h/.2)
    for row in range(rows):
        lower, upper = row*h/rows, (row+1)*h/rows
        offset = -.17 if row % 2 else 0
        cuts = sorted(set([-.5, .5]+[x for x in [offset+i*.335 for i in range(-3, 4)] if -.49 < x < .49]))
        for left, right in zip(cuts, cuts[1:]):
            for x0, x1, z0, z1 in mask:
                a, b, c, d = max(left, x0), min(right, x1), max(lower, z0), min(upper, z1)
                if min(b-a, d-c) < .008:
                    continue
                # Closed boundary stones define the nominal envelope; only inner joints recede.
                aa = a+.003 if a > x0+.0001 else a
                bb = b-.003 if b < x1-.0001 else b
                cc = c+.003 if c > z0+.0001 else c
                dd = d-.003 if d < z1-.0001 else d
                builder.box('Hand shaped ashlar', (aa, bb, -.075, .075, cc, dd), 'pierre', .011, 0, True)


def horizontal(builder, shape, palette):
    h = SHAPES[shape]['height']
    if palette == 'pierre':
        builder.box('Mortar bed', (-.5, .5, -.485, .485, 0, h-.012), 'mortar', .004, 0)
        top = h-.065 if shape == 'fondation' else 0
        for i in range(3):
            for j in range(3):
                x0, x1 = -.5+i/3, -.5+(i+1)/3
                y0, y1 = -.5+j/3, -.5+(j+1)/3
                gap = .003
                builder.box('Upper paving stone', (x0+(gap if i else 0), x1-(gap if i<2 else 0),
                            y0+(gap if j else 0), y1-(gap if j<2 else 0), top, h),
                            'pierre', .008, 0, True)
        if shape == 'fondation':
            for row in range(3):
                z0, z1 = row*top/3, (row+1)*top/3
                cuts = [-.5, -.26, .08, .34, .5] if row % 2 else [-.5, -.18, .18, .5]
                for a, b in zip(cuts, cuts[1:]):
                    lo, hi = a+(.003 if a>-.5 else 0), b-(.003 if b<.5 else 0)
                    for front in [-1, 1]:
                        y0, y1 = (-.5, -.36) if front == -1 else (.36, .5)
                        builder.box('Foundation facing', (lo, hi, y0, y1,
                                    z0+(.003 if row else 0), z1-.003), 'pierre', .011, 0, True)
                    for side in [-1, 1]:
                        x0, x1 = (-.5, -.36) if side == -1 else (.36, .5)
                        builder.box('Foundation return', (x0, x1, max(-.36,lo), min(.36,hi),
                                    z0+(.003 if row else 0), z1-.003), 'pierre', .011, 1, True)
        return
    rail = .075 if shape == 'fondation' else .045
    topthickness = .065 if shape == 'fondation' else .1
    if shape == 'fondation':
        for x in [-.5+rail/2, .5-rail/2]:
            for y in [-.5+rail/2, .5-rail/2]:
                builder.box('Square corner upright', (x-rail/2, x+rail/2, y-rail/2, y+rail/2, 0, h),
                            'timber', .009, 2)
        # Four side infills use the same vertically woven / sawn construction as the walls.
        for along in [0, 1]:
            for sign in [-1, 1]:
                count = 6 if palette == 'bois' else 14
                for index in range(count):
                    a = -.5+rail+(1-2*rail)*index/count
                    b = -.5+rail+(1-2*rail)*(index+1)/count
                    low, high = (-.482, -.40) if sign == -1 else (.40, .482)
                    limits = (a+.001, b-.001, low, high, .045, h-topthickness) if along == 0 else (
                        low, high, a+.001, b-.001, .045, h-topthickness)
                    builder.box('Foundation infill', limits, palette, .009, 2, True)
                if palette == 'chaume':
                    for z in [.12, .28, .39]:
                        y = sign*.488
                        a, b = (-.5+rail, y, z), (.5-rail, y, z)
                        if along: a, b = (y, -.5+rail, z), (y, .5-rail, z)
                        builder.rope(a, b, .0035)
        for z0, z1 in [(0, .045), (h-topthickness-.04, h-topthickness)]:
            for along in [0, 1]:
                for sign in [-1, 1]:
                    a, b = (-.5, -.5+rail) if sign == -1 else (.5-rail, .5)
                    lim = (-.5+rail, .5-rail, a, b, z0, z1) if along == 0 else (
                        a, b, -.5, .5, z0, z1)
                    builder.box('Foundation side rail', lim, 'timber', .006, along)
    for along in [0, 1]:
        for sign in [-1, 1]:
            a, b = (-.5, -.5+rail) if sign == -1 else (.5-rail, .5)
            lim = (-.5+rail, .5-rail, a, b, h-topthickness, h) if along == 0 else (
                a, b, -.5, .5, h-topthickness, h)
            builder.box('Deck perimeter beam', lim, 'timber', .007, along)
    count = 6 if palette == 'bois' else 13
    for index in range(count):
        a, b = -.5+rail+(1-2*rail)*index/count, -.5+rail+(1-2*rail)*(index+1)/count
        builder.box('Deck board' if palette == 'bois' else 'Woven deck bundle',
                    (a+.001, b-.001, -.5+rail, .5-rail, h-topthickness+.006, h-.007),
                    palette, .008, 1, True)
    # Underface support makes the ceiling readable from below and preserves clean join faces.
    if shape == 'plafond':
        for x in [-.27, .27]:
            builder.box('Underface joist', (x-.023, x+.023, -.5+rail, .5-rail, 0, .027),
                        'timber', .005, 1)
    if palette == 'chaume':
        for y in [-.28, 0, .28]:
            builder.rope((-.5+rail, y, h-.0028), (.5-rail, y, h-.0028), .0026)
            if shape == 'plafond':
                builder.rope((-.5+rail, y, .022), (.5-rail, y, .022), .0026)


def snap_points(cfg):
    w, h, d = cfg['width'], cfg['height'], cfg['depth']
    return [
        {'name': 'Snap_Left', 'position_m': [-w/2, h/2, 0], 'normal': [-1, 0, 0]},
        {'name': 'Snap_Right', 'position_m': [w/2, h/2, 0], 'normal': [1, 0, 0]},
        {'name': 'Snap_Bottom', 'position_m': [0, 0, 0], 'normal': [0, -1, 0]},
        {'name': 'Snap_Top', 'position_m': [0, h, 0], 'normal': [0, 1, 0]},
        {'name': 'Snap_Front', 'position_m': [0, h/2, d/2], 'normal': [0, 0, 1]},
        {'name': 'Snap_Back', 'position_m': [0, h/2, -d/2], 'normal': [0, 0, -1]},
    ]


def studio(objects, target, extent, filepath, draft=False, gpu=False, wide=False):
    scene = bpy.context.scene
    scene.render.engine = 'CYCLES'
    scene.cycles.samples = 16 if draft else 32
    scene.cycles.use_denoising = True
    scene.render.threads_mode = 'FIXED'
    scene.render.threads = 6
    if gpu:
        prefs = bpy.context.preferences.addons['cycles'].preferences
        prefs.compute_device_type = 'OPTIX'
        prefs.get_devices()
        for device in prefs.devices:
            device.use = device.type == 'OPTIX'
        scene.cycles.device = 'GPU'
    scene.render.resolution_x = (1800 if wide else 1000) if not draft else (1400 if wide else 700)
    scene.render.resolution_y = 1350 if wide else scene.render.resolution_x
    scene.render.resolution_percentage = 100
    world = bpy.data.worlds.new('Neutral sage studio')
    scene.world = world
    world.use_nodes = True
    world.node_tree.nodes['Background'].inputs['Color'].default_value = (.30, .35, .33, 1)
    world.node_tree.nodes['Background'].inputs['Strength'].default_value = .65
    bpy.ops.mesh.primitive_plane_add(size=200, location=(0, 0, -.002))
    floor = bpy.context.object
    floor.name = 'Studio floor | not exported'
    mat = bpy.data.materials.new('Neutral studio floor')
    mat.diffuse_color = (.23, .265, .235, 1)
    mat.use_nodes = True
    mat.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = mat.diffuse_color
    mat.node_tree.nodes['Principled BSDF'].inputs['Roughness'].default_value = .9
    floor.data.materials.append(mat)
    for name, pos, energy, size in [('Broad key', (-3, -4, 5), 650, 4),
                                    ('Broad fill', (4, -1, 3), 300, 3),
                                    ('Soft rim', (1, 3, 4), 480, 3)]:
        data = bpy.data.lights.new(name, 'AREA')
        data.energy = energy * extent**2
        data.shape = 'DISK'
        data.size = size * extent
        light = bpy.data.objects.new(name, data)
        scene.collection.objects.link(light)
        light.location = Vector(target) + Vector(pos)*extent
        light.rotation_euler = (Vector(target)-light.location).to_track_quat('-Z', 'Y').to_euler()
    data = bpy.data.cameras.new('Module preview')
    camera = bpy.data.objects.new('Module preview', data)
    scene.collection.objects.link(camera)
    scene.camera = camera
    camera.location = Vector(target) + Vector((1.0, -6.0, 7.0) if wide else (1.7, -3.8, 2.1))*extent
    camera.rotation_euler = (Vector(target)-camera.location).to_track_quat('-Z', 'Y').to_euler()
    data.type = 'ORTHO'
    bpy.context.view_layer.update()
    inverse_rotation = camera.rotation_euler.to_matrix().transposed()
    projected = [inverse_rotation @ (obj.matrix_world @ Vector(corner)-Vector(target))
                 for obj in objects for corner in obj.bound_box]
    screen_width = max(p.x for p in projected)-min(p.x for p in projected)
    screen_height = max(p.y for p in projected)-min(p.y for p in projected)
    aspect = scene.render.resolution_x/scene.render.resolution_y
    data.ortho_scale = max(screen_width, screen_height*aspect)*1.18
    scene.view_settings.view_transform = 'AgX'
    scene.render.image_settings.file_format = 'PNG'
    scene.render.filepath = str(filepath)
    bpy.ops.render.render(write_still=True)


def build(shape, palette, args):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.preferences.filepaths.save_version = 0
    cfg = SHAPES[shape]
    stem = shape+'-'+palette
    folder = ROOT/'assets'/stem
    folder.mkdir(parents=True, exist_ok=True)
    root = bpy.data.objects.new('Arkya_Structure_'+stem, None)
    bpy.context.scene.collection.objects.link(root)
    root['arkya_family'] = 'structures'
    root['arkya_shape'] = shape
    root['arkya_material'] = palette
    root['units'] = 'metres'
    # Some glTF consumers reserve extras.pivot for a numerical offset vector.
    root['arkya_pivot'] = 'bottom_center'
    root['nominal_dimensions_m'] = [cfg['width'], cfg['height'], cfg['depth']]
    builder = Builder(palette)
    if shape in ['fondation', 'plafond']:
        horizontal(builder, shape, palette)
    elif palette == 'pierre':
        stone_wall(builder, shape)
    else:
        timber_wall(builder, shape, palette)
    mesh = builder.object('Structure_'+stem, root)
    markers = []
    for point in snap_points(cfg):
        obj = bpy.data.objects.new(point['name'], None)
        bpy.context.scene.collection.objects.link(obj)
        obj.parent = root
        x, y, z = point['position_m']
        obj.location = (x, -z, y)
        obj.empty_display_type = 'ARROWS'
        obj.empty_display_size = .07
        obj['snap_role'] = point['name'][5:].lower()
        obj['position_m'] = point['position_m']
        obj['normal'] = point['normal']
        obj['coordinate_system'] = 'glTF Y-up, metres, root-local'
        markers.append(obj)
    bpy.context.view_layer.update()
    triangles = sum(len(poly.vertices)-2 for poly in mesh.data.polygons)
    assert triangles < 20000, (stem, triangles)
    low = [min(v.co[i] for v in mesh.data.vertices) for i in range(3)]
    high = [max(v.co[i] for v in mesh.data.vertices) for i in range(3)]
    expected = [cfg['width'], cfg['depth'], cfg['height']]
    assert max(abs(high[i]-low[i]-expected[i]) for i in range(3)) < .00001, (stem, low, high)
    bpy.ops.object.select_all(action='DESELECT')
    for obj in [root, mesh]+markers:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = mesh
    bpy.ops.export_scene.gltf(filepath=str(folder/(stem+'.glb')), export_format='GLB',
                             use_selection=True, export_apply=True, export_materials='EXPORT',
                             export_yup=True, export_animations=False, export_extras=True,
                             export_cameras=False, export_lights=False, export_tangents=False)
    scene = bpy.context.scene
    scene.unit_settings.system = 'METRIC'
    scene['arkya_resource'] = 'structures'
    scene['source_script'] = 'scripts/generate_structures.py'
    text = bpy.data.texts.new('Arkya provenance')
    text.write('Original modular structures, painted textures, softly beveled construction geometry. '
               'Five nominal shapes and three material constructions. Source scripts/generate_structures.py. '
               'Artistic status proposed. Mesh and root at bottom-centre, metres, GLB Y-up. '
               'Snap points export as empty nodes with local Y-up position_m and normal extras. '
               'No collisions, gameplay ownership or authoritative build system implemented.')
    if args.render:
        studio([mesh], (0, 0, cfg['height']*.44), max(expected), folder/(stem+'-preview.png'), args.draft, args.gpu)
    bpy.ops.file.pack_all()
    bpy.ops.wm.save_as_mainfile(filepath=str(folder/(stem+'.blend')))
    record = {'id': stem, 'shape': shape, 'material': palette,
              'label': cfg['label']+' · '+PALETTES[palette]['label'],
              'glb': (folder/(stem+'.glb')).relative_to(ROOT).as_posix(),
              'blend': (folder/(stem+'.blend')).relative_to(ROOT).as_posix(),
              'preview': (folder/(stem+'-preview.png')).relative_to(ROOT).as_posix() if args.render else None,
              'height': cfg['height'], 'width': cfg['width'], 'depth': cfg['depth'],
              'nominal_dimensions': {k: cfg[k] for k in ['width', 'height', 'depth']},
              'triangles': triangles, 'materials_count': len(set(builder.indices)),
              'texture_resolution': [int(i) for i in bpy.data.images.get(palette+'-basecolor.png').size],
              'opaque': True, 'pivot': 'centre au sol', 'gltf_up_axis': 'Y',
              'snap_points': snap_points(cfg), 'snap_coordinate_system': 'glTF Y-up, metres, root-local'}
    if palette != 'pierre':
        record['wood_finish'] = {'revision': 2, 'uv_tile_metres': list(WOOD_TILE_METRES),
                                 'roughness': .96, 'source': 'sources/worked-timber-painted-v2.png'}
    if cfg.get('opening'):
        record['opening'] = cfg['opening']
    (folder/(stem+'.json')).write_text(json.dumps(record, ensure_ascii=False, indent=2), encoding='utf-8')
    path = ROOT/'manifest.json'
    old = json.loads(path.read_text(encoding='utf-8')) if path.exists() else {}
    records = [a for a in old.get('assets', []) if a['id'] != stem]+[record]
    manifest = {'family': 'structures', 'artistic_status': 'proposed',
                'shapes': [dict(id=k, nominal_dimensions={n: v[n] for n in ['width', 'height', 'depth']}, **v)
                           for k, v in SHAPES.items()],
                'materials': [dict(id=k, **v) for k, v in PALETTES.items()],
                'assets': sorted(records, key=lambda a: (list(SHAPES).index(a['shape']), list(PALETTES).index(a['material'])))}
    path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding='utf-8')
    print('STRUCTURE_READY', stem, triangles, flush=True)


def comparison(args):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    meshes = []
    # Three rows and five shapes at a shared metric scale, small pieces beside the 2 m door.
    xs = [-3.0, -1.5, 0, 1.5, 3.0]
    ys = [-3.2, 0, 3.2]
    for row, palette in enumerate(PALETTES):
        for col, shape in enumerate(SHAPES):
            stem = shape+'-'+palette
            path = ROOT/'assets'/stem/(stem+'.glb')
            before = set(bpy.data.objects)
            bpy.ops.import_scene.gltf(filepath=str(path))
            added = set(bpy.data.objects)-before
            for obj in added:
                if obj.parent is None:
                    obj.location += Vector((xs[col], ys[row], 0))
                if obj.type == 'MESH':
                    meshes.append(obj)
    studio(meshes, (0, 0, .6), 7.8, ROOT/'comparaison-tailles.png', args.draft, args.gpu, True)
    print('STRUCTURE_COMPARISON_READY', flush=True)


def main():
    p = argparse.ArgumentParser()
    p.add_argument('--shape', choices=['all']+list(SHAPES), default='mur-fenetre')
    p.add_argument('--material', choices=['all']+list(PALETTES), default='bois')
    p.add_argument('--render', action='store_true')
    p.add_argument('--draft', action='store_true')
    p.add_argument('--gpu', action='store_true')
    p.add_argument('--comparison', action='store_true')
    p.add_argument('--comparison-only', action='store_true')
    args = p.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
    if not args.comparison_only:
        for shape in list(SHAPES) if args.shape == 'all' else [args.shape]:
            for palette in list(PALETTES) if args.material == 'all' else [args.material]:
                build(shape, palette, args)
    if args.comparison or args.comparison_only:
        comparison(args)


if __name__ == '__main__':
    main()
