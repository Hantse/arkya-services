"""Bake three material studies from the immutable approved Arkya tree.

Blender 4.5: --background --python generate_materials.py -- --variant all
"""
import argparse
from array import array
import hashlib
import json
import math
from pathlib import Path
import random
import struct
import sys

import bpy
from mathutils import Vector

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
BASE = ROOT.parent / 'arkya-ancient-tree-v2.blend'
VARIANTS = {'mystique': '01-mystique', 'brule': '02-brule', 'moussu': '03-moussu'}


def configure(draft=False):
    scene = bpy.context.scene
    scene.render.engine = 'CYCLES'
    prefs = bpy.context.preferences.addons['cycles'].preferences
    prefs.compute_device_type = 'OPTIX'
    prefs.refresh_devices()
    for device in prefs.devices:
        device.use = device.type == 'OPTIX'
    scene.cycles.device = 'GPU'
    scene.cycles.samples = 12 if draft else 32
    scene.cycles.use_denoising = True
    scene.cycles.max_bounces = 6
    scene.cycles.adaptive_threshold = .035
    scene.render.resolution_x = 700 if draft else 1100
    scene.render.resolution_y = scene.render.resolution_x
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = 'PNG'
    scene.render.image_settings.color_mode = 'RGB'
    scene.view_settings.view_transform = 'AgX'
    scene.view_settings.look = 'AgX - Medium High Contrast'
    scene.view_settings.exposure = .20
    bpy.context.preferences.filepaths.save_version = 0


class Graph:
    def __init__(self, name):
        self.material = bpy.data.materials.new(name)
        self.material.use_nodes = True
        self.nodes = self.material.node_tree.nodes
        self.links = self.material.node_tree.links
        self.p = self.nodes.get('Principled BSDF')
        self.p.inputs['Metallic'].default_value = 0
        self.p.inputs['Specular IOR Level'].default_value = .23
        self.p.inputs['Roughness'].default_value = .85

    def node(self, kind, label=None):
        n = self.nodes.new(kind)
        if label:
            n.label = label
        return n

    def connect(self, source, dest):
        if isinstance(source, (float, int)):
            dest.default_value = source
        elif isinstance(source, (tuple, list)):
            dest.default_value = source
        else:
            self.links.new(source, dest)

    def math(self, operation, a, b=0):
        n = self.node('ShaderNodeMath', operation)
        n.operation = operation
        self.connect(a, n.inputs[0]); self.connect(b, n.inputs[1])
        return n.outputs[0]

    def noise(self, vector, scale, detail=3, roughness=.7):
        n = self.node('ShaderNodeTexNoise')
        n.inputs['Scale'].default_value = scale
        n.inputs['Detail'].default_value = detail
        n.inputs['Roughness'].default_value = roughness
        self.connect(vector, n.inputs['Vector'])
        return n.outputs['Fac']

    def ramp(self, value, stops):
        n = self.node('ShaderNodeValToRGB')
        n.color_ramp.interpolation = 'EASE'
        n.color_ramp.elements[0].position = stops[0][0]
        n.color_ramp.elements[0].color = stops[0][1]
        n.color_ramp.elements[1].position = stops[-1][0]
        n.color_ramp.elements[1].color = stops[-1][1]
        for position, color in stops[1:-1]:
            n.color_ramp.elements.new(position).color = color
        self.connect(value, n.inputs['Fac'])
        return n.outputs['Color']

    def mix(self, factor, a, b, blend='MIX'):
        n = self.node('ShaderNodeMixRGB')
        n.blend_type = blend
        self.connect(factor, n.inputs[0]); self.connect(a, n.inputs[1]); self.connect(b, n.inputs[2])
        return n.outputs[0]

    def scale(self, vector, factors):
        n = self.node('ShaderNodeVectorMath')
        n.operation = 'MULTIPLY'
        self.connect(vector, n.inputs[0]); self.connect(factors, n.inputs[1])
        return n.outputs[0]


def procedural(variant, base_color, base_normal):
    g = Graph('Arkya | ' + variant + ' | editable procedural authoring')
    g.material.use_fake_user = True
    coords = g.node('ShaderNodeTexCoord')
    pos = coords.outputs['Object']
    stretched = g.scale(pos, (2.8, 2.8, .40))
    grain = g.noise(stretched, 2.0, 4)
    fine = g.noise(pos, 38, 3)
    patches = g.noise(pos, 1.25, 3)
    split = g.node('ShaderNodeSeparateXYZ'); g.connect(pos, split.inputs[0])
    height = split.outputs['Z']
    low = g.ramp(g.math('DIVIDE', height, 2.4), [(.08, (1, 1, 1, 1)), (1, (0, 0, 0, 1))])
    # Preserve the source bake's sheltered crevices without recomputing expensive AO.
    original = g.node('ShaderNodeTexImage'); original.image = base_color
    luminance = g.node('ShaderNodeRGBToBW'); g.connect(original.outputs['Color'], luminance.inputs[0])
    shade = g.node('ShaderNodeMapRange')
    shade.clamp = True
    shade.inputs['From Min'].default_value = .025
    shade.inputs['From Max'].default_value = .26
    shade.inputs['To Min'].default_value = .48
    shade.inputs['To Max'].default_value = 1.0
    g.connect(luminance.outputs[0], shade.inputs['Value'])
    source_normal = g.node('ShaderNodeTexImage'); source_normal.image = base_normal
    normal = g.node('ShaderNodeNormalMap')
    normal.inputs['Strength'].default_value = .75
    g.connect(source_normal.outputs['Color'], normal.inputs['Color'])
    edge = g.node('ShaderNodeTexVoronoi')
    edge.feature = 'DISTANCE_TO_EDGE'
    edge.inputs['Scale'].default_value = 3.5
    g.connect(stretched, edge.inputs['Vector'])
    crack = g.ramp(edge.outputs['Distance'], [(0, (1, 1, 1, 1)), (.022, (1, 1, 1, 1)), (.058, (0, 0, 0, 1))])
    emission = (0, 0, 0, 1)
    if variant == 'mystique':
        bark = g.ramp(grain, [(.2, (.038, .022, .052, 1)), (.42, (.095, .058, .11, 1)),
                             (.62, (.18, .102, .14, 1)), (.8, (.25, .17, .19, 1))])
        indigo = g.ramp(patches, [(.3, (0, 0, 0, 1)), (.7, (.56, .56, .56, 1))])
        bark = g.mix(indigo, bark, (.07, .075, .15, 1))
        rare = g.ramp(g.noise(pos, 1.85, 2), [(.52, (0, 0, 0, 1)), (.66, (1, 1, 1, 1))])
        veins = g.math('MULTIPLY', crack, rare)
        veins = g.math('MULTIPLY', veins, .72)
        bark = g.mix(veins, bark, (.045, .26, .22, 1))
        emission = g.mix(veins, (0, 0, 0, 1), (.035, .32, .23, 1))
        roughness = g.mix(grain, (.69, .69, .69, 1), (.87, .87, .87, 1))
        bump_height = g.math('ADD', g.math('MULTIPLY', grain, .68), g.math('MULTIPLY', fine, .16))
        bump_height = g.math('SUBTRACT', bump_height, g.math('MULTIPLY', crack, .22))
        bump_distance = .065
    elif variant == 'brule':
        bark = g.ramp(grain, [(.18, (.012, .011, .014, 1)), (.44, (.026, .024, .028, 1)),
                             (.65, (.065, .061, .060, 1)), (.82, (.12, .11, .10, 1))])
        ash = g.ramp(g.noise(pos, 8, 3), [(.48, (0, 0, 0, 1)), (.72, (.48, .48, .48, 1))])
        bark = g.mix(ash, bark, (.19, .18, .175, 1))
        rare = g.ramp(g.noise(pos, 2.9, 2), [(.59, (0, 0, 0, 1)), (.72, (1, 1, 1, 1))])
        ember = g.math('MULTIPLY', crack, rare)
        bark = g.mix(ember, bark, (.23, .045, .008, 1))
        emission = g.mix(ember, (0, 0, 0, 1), (.9, .095, .009, 1))
        roughness = g.mix(ash, (.81, .81, .81, 1), (.99, .99, .99, 1))
        bump_height = g.math('SUBTRACT', g.math('MULTIPLY', fine, .28), g.math('MULTIPLY', crack, .78))
        bump_distance = .095
    else:
        bark = g.ramp(grain, [(.2, (.038, .047, .029, 1)), (.44, (.085, .11, .052, 1)),
                             (.65, (.18, .19, .095, 1)), (.8, (.25, .25, .15, 1))])
        patch = g.math('ADD', patches, g.math('MULTIPLY', low, .10))
        moss = g.ramp(patch, [(.42, (0, 0, 0, 1)), (.55, (.65, .65, .65, 1)), (.64, (1, 1, 1, 1))])
        moss_color = g.ramp(g.noise(pos, 12, 4), [(.25, (.055, .11, .028, 1)), (.55, (.15, .24, .055, 1)),
                                               (.78, (.26, .34, .12, 1))])
        bark = g.mix(moss, bark, moss_color)
        verdigris = g.math('MULTIPLY', g.ramp(patches, [(.28, (.26, .26, .26, 1)), (.52, (0, 0, 0, 1))]), .7)
        bark = g.mix(verdigris, bark, (.075, .18, .13, 1))
        wet = g.math('MULTIPLY', low, .55)
        bark = g.mix(wet, bark, (.025, .045, .028, 1))
        roughness = g.mix(wet, (.97, .97, .97, 1), (.53, .53, .53, 1))
        moss_relief = g.math('MULTIPLY', moss, g.math('ADD', .55, g.math('MULTIPLY', fine, .45)))
        bump_height = g.math('ADD', g.math('MULTIPLY', grain, .25), moss_relief)
        bump_distance = .085
    color = g.mix(1, bark, shade.outputs['Result'], 'MULTIPLY')
    bump = g.node('ShaderNodeBump')
    bump.inputs['Strength'].default_value = .72
    bump.inputs['Distance'].default_value = bump_distance
    g.connect(bump_height, bump.inputs['Height']); g.connect(normal.outputs[0], bump.inputs['Normal'])
    g.connect(color, g.p.inputs['Base Color']); g.connect(roughness, g.p.inputs['Roughness'])
    g.connect(bump.outputs[0], g.p.inputs['Normal'])
    g.connect(emission, g.p.inputs['Emission Color'])
    g.p.inputs['Emission Strength'].default_value = 1
    return g, {'basecolor': color, 'roughness': roughness, 'emission': emission}


def select(objects):
    bpy.ops.object.select_all(action='DESELECT')
    for obj in objects:
        obj.hide_set(False); obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]


def bake(wood, graph, channels, folder, stem):
    select([wood])
    wood.data.materials.clear(); wood.data.materials.append(graph.material)
    for polygon in wood.data.polygons:
        polygon.material_index = 0
    scene = bpy.context.scene
    scene.cycles.samples = 1
    scene.render.bake.margin = 12
    scene.render.bake.use_selected_to_active = False
    scene.render.bake.normal_space = 'TANGENT'
    output = graph.nodes.get('Material Output')
    emitter = graph.node('ShaderNodeEmission')
    target = graph.node('ShaderNodeTexImage', 'Bake target; original UV preserved')
    graph.nodes.active = target
    images = {}
    for channel in ('basecolor', 'normal', 'roughness', 'emission'):
        image = bpy.data.images.new(stem+'-'+channel, 2048, 2048, alpha=False)
        image.colorspace_settings.name = 'Non-Color' if channel in ('normal', 'roughness') else 'sRGB'
        if channel == 'normal':
            image.generated_color = (.5, .5, 1, 1)
            graph.links.new(graph.p.outputs[0], output.inputs['Surface'])
        else:
            for link in list(emitter.inputs['Color'].links): graph.links.remove(link)
            graph.connect(channels[channel], emitter.inputs['Color'])
            graph.links.new(emitter.outputs[0], output.inputs['Surface'])
        target.image = image
        print('BAKE_START', stem, channel, flush=True)
        bpy.ops.object.bake(type='NORMAL' if channel == 'normal' else 'EMIT')
        image.filepath_raw = str(folder/(stem+'-'+channel+'.png'))
        image.file_format = 'PNG'; image.save(); image.pack()
        images[channel] = image
        print('BAKE_DONE', stem, channel, flush=True)
    graph.links.new(graph.p.outputs[0], output.inputs['Surface'])
    graph.nodes.remove(emitter); graph.nodes.remove(target)
    baked = Graph('Arkya | '+stem+' | baked PBR 2K')
    for channel, socket in [('basecolor','Base Color'), ('roughness','Roughness'), ('emission','Emission Color')]:
        texture = baked.node('ShaderNodeTexImage', channel); texture.image = images[channel]
        baked.links.new(texture.outputs['Color'], baked.p.inputs[socket])
    texture = baked.node('ShaderNodeTexImage', 'normal'); texture.image = images['normal']
    normal = baked.node('ShaderNodeNormalMap'); normal.inputs['Strength'].default_value = .9
    baked.links.new(texture.outputs['Color'], normal.inputs['Color'])
    baked.links.new(normal.outputs[0], baked.p.inputs['Normal'])
    baked.p.inputs['Emission Strength'].default_value = 1
    wood.data.materials.clear(); wood.data.materials.append(baked.material)
    return images


def recolor_foliage(leaves, twigs, variant):
    attribute = leaves.data.color_attributes['COLOR_0']
    values = array('f', [0])*(len(attribute.data)*4)
    attribute.data.foreach_get('color', values)
    # Homogeneous linear transforms keep the multiplicative AO in every original vertex.
    for index in range(len(attribute.data)):
        r, g, b = values[index*4:index*4+3]
        if variant == 'mystique':
            rgb = (r*.48+g*.025, g*.80+b*.06, b*.95+g*.38)
        elif variant == 'brule':
            factor = .43 if random.Random(index//11+142).random() < .28 else 1
            rgb = ((r*.55+g*.20)*factor, (g*.20+r*.04)*factor, (b*.16+r*.035)*factor)
        else:
            rgb = (r*.70+g*.13, g*.94+b*.04, b*.89+g*.12)
        for component in range(3):
            values[index*4+component] = min(1, rgb[component])
    attribute.data.foreach_set('color', values)
    leaves['material_variant'] = variant
    leaves['canopy_occlusion_preserved'] = 'Linear colour transform of original baked AO; no AO recomputation'
    material = twigs.data.materials[0].copy()
    material.name = 'Arkya | '+variant+' | branchlets'
    color = {'mystique':(.055,.032,.07,1), 'brule':(.025,.022,.023,1), 'moussu':(.057,.073,.034,1)}[variant]
    material.diffuse_color = color
    material.node_tree.nodes.get('Principled BSDF').inputs['Base Color'].default_value = color
    material.node_tree.nodes.get('Principled BSDF').inputs['Roughness'].default_value = .88
    twigs.data.materials.clear(); twigs.data.materials.append(material)


def render_views(folder, stem, draft):
    configure(draft)
    scene = bpy.context.scene
    camera = scene.camera
    camera.location = (15, -21, 13)
    camera.rotation_euler = (Vector((0,0,5))-camera.location).to_track_quat('-Z','Y').to_euler()
    camera.data.type = 'ORTHO'; camera.data.ortho_scale = 12.0
    scene.render.filepath = str(folder/(stem+'-preview.png'))
    bpy.ops.render.render(write_still=True)
    camera.location = (6,-9,4.6)
    camera.rotation_euler = (Vector((0,0,2.4))-camera.location).to_track_quat('-Z','Y').to_euler()
    camera.data.ortho_scale = 5.8
    scene.render.filepath = str(folder/(stem+'-tronc.png'))
    bpy.ops.render.render(write_still=True)
    camera.location = (15,-21,13)
    camera.rotation_euler = (Vector((0,0,5))-camera.location).to_track_quat('-Z','Y').to_euler()
    camera.data.ortho_scale = 12.0


def generate(variant, draft=False):
    stem = VARIANTS[variant]
    folder = ROOT/'matieres'/stem
    textures = folder/'textures'
    textures.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.open_mainfile(filepath=str(BASE))
    configure(draft)
    wood = bpy.data.objects['Oak | continuous sculpted trunk roots and limbs']
    leaves = bpy.data.objects['Oak | layered connected foliage']
    twigs = bpy.data.objects['Oak | living branchlet network']
    objects = [wood, twigs, leaves]
    graph, channels = procedural(variant, bpy.data.images['Oak_V2_Bark_color'], bpy.data.images['Oak_V2_Bark_normal'])
    bake(wood, graph, channels, textures, stem)
    recolor_foliage(leaves, twigs, variant)
    select(objects)
    bpy.ops.export_scene.gltf(filepath=str(folder/(stem+'.glb')), export_format='GLB', use_selection=True,
                             export_apply=True, export_materials='EXPORT', export_yup=True,
                             export_animations=False, export_cameras=False, export_lights=False, export_tangents=True)
    render_views(folder, stem, draft)
    # All texture images remain embedded; procedural authoring nodes are retained as a fake user material.
    bpy.ops.file.pack_all()
    bpy.ops.wm.save_as_mainfile(filepath=str(folder/(stem+'.blend')))
    metadata = {'id':stem, 'variant':variant, 'source':str(BASE.relative_to(ROOT.parent)),
                'source_sha256':hashlib.sha256(BASE.read_bytes()).hexdigest(),
                'geometry':'Unchanged approved tree: identical meshes, topology and UVs',
                'vertices':sum(len(o.data.vertices) for o in objects),
                'triangles':sum(len(p.vertices)-2 for o in objects for p in o.data.polygons),
                'mesh_count':3, 'textures':{key: 'textures/'+stem+'-'+key+'.png' for key in ('basecolor','normal','roughness','emission')},
                'texture_resolution':[2048,2048], 'foliage':'Original vertex COLOR_0 AO preserved through linear remapping',
                'render':{'engine':'Cycles OptiX','samples':12 if draft else 32,'size':700 if draft else 1100},
                'glb_bytes':(folder/(stem+'.glb')).stat().st_size,
                'notes':{'mystique':'Brown violet and indigo grain; sparse turquoise organic fissures with restrained emission',
                         'brule':'Charcoal, grey ash and sparse ember fissures; muted russet and charcoal foliage',
                         'moussu':'Thick irregular moss patches, verdigris, damp dark roots and deep sage foliage'}[variant]}
    (folder/(stem+'.json')).write_text(json.dumps(metadata,ensure_ascii=False,indent=2),encoding='utf-8')
    print('MATERIAL_VARIANT_READY', stem, flush=True)


def verify_saved_variants():
    def mesh_fingerprint(obj):
        digest = hashlib.sha256()
        for prop, count, data, kind in [('co', len(obj.data.vertices)*3, obj.data.vertices, 'f'),
                                        ('vertex_index', len(obj.data.loops), obj.data.loops, 'I')]:
            values = array(kind, [0])*count
            data.foreach_get(prop, values)
            digest.update(values.tobytes())
        for layer in obj.data.uv_layers:
            values = array('f', [0])*(len(layer.data)*2)
            layer.data.foreach_get('uv', values)
            digest.update(values.tobytes())
        return digest.hexdigest()

    bpy.ops.wm.open_mainfile(filepath=str(BASE))
    source_hash = hashlib.sha256(BASE.read_bytes()).hexdigest()
    source = {obj.name:mesh_fingerprint(obj) for obj in bpy.context.scene.objects
              if obj.type == 'MESH' and obj.name.startswith('Oak |')}
    for stem in VARIANTS.values():
        folder = ROOT/'matieres'/stem
        bpy.ops.wm.open_mainfile(filepath=str(folder/(stem+'.blend')))
        unchanged = all(mesh_fingerprint(bpy.data.objects[name]) == digest for name,digest in source.items())
        blob = (folder/(stem+'.glb')).read_bytes()
        length, chunk_type = struct.unpack_from('<II', blob, 12)
        gltf = json.loads(blob[20:20+length])
        material = next(m for m in gltf['materials'] if 'baked PBR' in m.get('name',''))
        pbr = material['pbrMetallicRoughness']
        maps = ('baseColorTexture' in pbr and 'metallicRoughnessTexture' in pbr
                and 'normalTexture' in material and 'emissiveTexture' in material)
        external = any('uri' in item for item in gltf.get('buffers',[])+gltf.get('images',[]))
        color = any('COLOR_0' in primitive['attributes'] for mesh in gltf['meshes'] for primitive in mesh['primitives'])
        packed = all(image.packed_file for image in bpy.data.images if image.source == 'FILE')
        assert unchanged and maps and not external and color and packed, stem+' validation failed'
        metadata_path = folder/(stem+'.json')
        metadata = json.loads(metadata_path.read_text(encoding='utf-8'))
        assert metadata['source_sha256'] == source_hash
        metadata['validation'] = {'geometry_and_uv_identical_to_base':unchanged,
                                  'blend_images_packed':bool(packed), 'glb_embedded_images':len(gltf['images']),
                                  'glb_all_pbr_maps_present':maps, 'glb_color_0_present':color,
                                  'glb_external_dependencies':external, 'source_sha256_unchanged':True}
        metadata_path.write_text(json.dumps(metadata,ensure_ascii=False,indent=2),encoding='utf-8')
        print('VERIFIED',stem,metadata['validation'],flush=True)


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--variant', choices=['all']+list(VARIANTS), default='all')
    parser.add_argument('--draft', action='store_true')
    parser.add_argument('--verify', action='store_true')
    args = parser.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
    if args.verify:
        verify_saved_variants()
    else:
        for key in VARIANTS if args.variant == 'all' else [args.variant]:
            generate(key,args.draft)
