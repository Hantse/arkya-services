"""Clean painted material studies; preserves V1, mesh geometry and original UVs.

Blender 4.5 --background --python generate_materials_v2.py -- --variant mystique
"""
import argparse
from array import array
import hashlib
import json
import math
from pathlib import Path
import struct
import sys

import bpy
from mathutils import Vector

sys.path.insert(0, str(Path(__file__).resolve().parent))
sys.dont_write_bytecode = True
from generate_materials import Graph, ROOT, BASE, VARIANTS, configure, render_views, select

OUTPUT = ROOT/'matieres-v2'


def map_range(g, value, low, high, start=0.0, end=1.0):
    node = g.node('ShaderNodeMapRange')
    node.clamp = True
    node.inputs['From Min'].default_value = low
    node.inputs['From Max'].default_value = high
    node.inputs['To Min'].default_value = start
    node.inputs['To Max'].default_value = end
    g.connect(value, node.inputs['Value'])
    return node.outputs['Result']


def sparse_marks(g, theta, z, warm=False):
    mask = 0.0
    # Four authored strokes around the trunk replace repeated all-over procedural cracks.
    for angle, height, length, slope in [(-1.05, 1.3, .78, .10), (-.45, 3.0, .95, -.09),
                                         (1.85, 2.35, .82, .12), (-2.75, 3.85, .72, -.11)]:
        dz = g.math('SUBTRACT', z, height)
        phase = g.math('SUBTRACT', theta, angle)
        side = g.math('SINE', phase)
        front = g.math('GREATER_THAN', g.math('COSINE', phase), .1)
        curved = g.math('ADD', g.math('MULTIPLY', dz, slope),
                        g.math('MULTIPLY', g.math('SINE', g.math('MULTIPLY', dz, 3.3)), .025))
        line = g.math('ABSOLUTE', g.math('SUBTRACT', side, curved))
        taper = map_range(g, g.math('ABSOLUTE', dz), length*.24, length*.54, 1, 0)
        width = g.math('MULTIPLY', taper, .055 if warm else .078)
        stroke = map_range(g, g.math('DIVIDE', line, g.math('ADD', width, .001)), .65, 1, 1, 0)
        stroke = g.math('MULTIPLY', stroke, taper)
        stroke = g.math('MULTIPLY', stroke, front)
        mask = g.math('MAXIMUM', mask, stroke)
    return mask


def painted_material(variant):
    g = Graph('Arkya V2 | '+variant+' | broad painted authoring')
    g.material.use_fake_user = True
    coords = g.node('ShaderNodeTexCoord')
    xyz = g.node('ShaderNodeSeparateXYZ')
    g.connect(coords.outputs['Object'], xyz.inputs[0])
    x, y, z = xyz.outputs['X'], xyz.outputs['Y'], xyz.outputs['Z']
    theta = g.math('ARCTAN2', y, x)
    phase = g.math('ADD', g.math('MULTIPLY', theta, 5),
                   g.math('MULTIPLY', g.math('SINE', g.math('MULTIPLY', z, .62)), .42))
    ribbons = g.math('ADD', .5, g.math('MULTIPLY', g.math('COSINE', phase), .5))
    geometry = g.node('ShaderNodeNewGeometry')
    dot = g.node('ShaderNodeVectorMath'); dot.operation = 'DOT_PRODUCT'
    g.connect(geometry.outputs['Normal'], dot.inputs[0])
    dot.inputs[1].default_value = (.38, -.64, .42)
    painted_light = map_range(g, dot.outputs['Value'], -.65, .7)
    height = map_range(g, z, .1, 7.0)
    value = g.math('ADD', .12, g.math('MULTIPLY', painted_light, .52))
    value = g.math('ADD', value, g.math('MULTIPLY', ribbons, .20))
    value = g.math('ADD', value, g.math('MULTIPLY', height, .12))
    emission = (0,0,0,1)
    if variant == 'mystique':
        color = g.ramp(value, [(.06,(.065,.050,.077,1)), (.44,(.155,.102,.137,1)),
                               (.88,(.265,.185,.212,1))])
        marks = sparse_marks(g, theta, z)
        color = g.mix(marks, color, (.075,.31,.26,1))
        emission = g.mix(marks, (0,0,0,1), (.018,.105,.080,1))
        roughness = .76
    elif variant == 'brule':
        color = g.ramp(value, [(.06,(.025,.035,.059,1)), (.44,(.067,.075,.106,1)),
                               (.88,(.15,.155,.19,1))])
        marks = sparse_marks(g, theta, z, True)
        color = g.mix(marks, color, (.36,.15,.045,1))
        emission = g.mix(marks, (0,0,0,1), (.23,.065,.008,1))
        roughness = .78
    else:
        color = g.ramp(value, [(.06,(.095,.064,.055,1)), (.44,(.25,.17,.085,1)),
                               (.88,(.43,.32,.16,1))])
        broad = g.noise(g.scale(coords.outputs['Object'], (.72,.72,.45)), 1, 0, .5)
        moss = g.ramp(broad, [(.53,(0,0,0,1)), (.64,(.88,.88,.88,1))])
        moss_color = g.ramp(painted_light, [(.10,(.07,.14,.087,1)), (.83,(.245,.32,.15,1))])
        color = g.mix(moss, color, moss_color)
        roughness = .82
    # Mid-scale painted grooves add wood identity without surface noise or displacement.
    flowing = g.math('ADD', g.math('MULTIPLY',theta,10),
                      g.math('MULTIPLY',g.math('SINE',g.math('ADD',g.math('MULTIPLY',z,.82),
                                                        g.math('MULTIPLY',theta,.3))),.62))
    length_variation = g.math('ADD',.35,g.math('MULTIPLY',
                          g.math('ADD',1,g.math('SINE',g.math('ADD',g.math('MULTIPLY',z,1.65),
                                                                    g.math('MULTIPLY',theta,3)))),.325))
    groove = map_range(g,g.math('COSINE',flowing),.52,.96)
    groove = g.math('MULTIPLY',groove,g.math('MULTIPLY',length_variation,.145))
    border = map_range(g,g.math('COSINE',g.math('ADD',flowing,.30)),.90,.99)
    border = g.math('MULTIPLY',border,g.math('MULTIPLY',length_variation,.105))
    groove_color = {'mystique':(.065,.038,.066,1),'brule':(.021,.026,.046,1),'moussu':(.105,.06,.04,1)}[variant]
    border_color = {'mystique':(.34,.235,.245,1),'brule':(.20,.20,.24,1),'moussu':(.49,.355,.18,1)}[variant]
    color = g.mix(groove,color,groove_color)
    color = g.mix(border,color,border_color)
    roughness = g.math('ADD',roughness,0)
    g.connect(color,g.p.inputs['Base Color'])
    g.connect(roughness,g.p.inputs['Roughness'])
    g.connect(emission,g.p.inputs['Emission Color'])
    g.p.inputs['Emission Strength'].default_value = 1
    # Geometry carries the silhouette; the broad normal modulation is deliberately faint.
    bump = g.node('ShaderNodeBump')
    bump.inputs['Strength'].default_value = .18
    bump.inputs['Distance'].default_value = .018
    g.connect(ribbons,bump.inputs['Height'])
    g.connect(bump.outputs[0],g.p.inputs['Normal'])
    return g, {'basecolor':color,'roughness':roughness,'emission':emission}


def bake(wood,graph,channels,folder,stem):
    select([wood]); wood.data.materials.clear(); wood.data.materials.append(graph.material)
    for polygon in wood.data.polygons: polygon.material_index=0
    scene=bpy.context.scene; scene.cycles.samples=1
    scene.render.bake.margin=32
    scene.render.bake.use_selected_to_active=False
    scene.render.bake.normal_space='TANGENT'
    scene.render.bake.use_clear=False
    output=graph.nodes.get('Material Output')
    emitter=graph.node('ShaderNodeEmission')
    target=graph.node('ShaderNodeTexImage','4K supersampled bake, original UV')
    graph.nodes.active=target
    images={}
    neutral={'01-mystique':(.15,.10,.14,1),'02-brule':(.067,.075,.106,1),'03-moussu':(.25,.17,.085,1)}[stem]
    for channel in ('basecolor','normal','roughness','emission'):
        image=bpy.data.images.new(stem+'-'+channel,4096,4096,alpha=False)
        image.colorspace_settings.name='Non-Color' if channel in ('normal','roughness') else 'sRGB'
        image.generated_color={'basecolor':neutral,'normal':(.5,.5,1,1),'roughness':(.78,.78,.78,1),'emission':(0,0,0,1)}[channel]
        if channel=='normal':
            graph.links.new(graph.p.outputs[0],output.inputs['Surface'])
        else:
            for link in list(emitter.inputs['Color'].links):graph.links.remove(link)
            graph.connect(channels[channel],emitter.inputs['Color'])
            graph.links.new(emitter.outputs[0],output.inputs['Surface'])
        target.image=image
        print('PAINT_BAKE',stem,channel,flush=True)
        bpy.ops.object.bake(type='NORMAL' if channel=='normal' else 'EMIT')
        image.scale(2048,2048)
        if channel=='normal':
            import numpy as np
            pixels=np.empty(2048*2048*4,dtype=np.float32)
            image.pixels.foreach_get(pixels)
            pixels=pixels.reshape((-1,4))
            invalid=(pixels[:,2]<.92)|(np.abs(pixels[:,0]-.5)>.13)|(np.abs(pixels[:,1]-.5)>.13)
            pixels[invalid,:3]=(.5,.5,1)
            image.pixels.foreach_set(pixels.ravel())
            print('NEUTRALIZED_INVALID_TANGENT_PIXELS',int(invalid.sum()),flush=True)
        image.filepath_raw=str(folder/(stem+'-'+channel+'.png'))
        image.file_format='PNG';image.save();image.pack();images[channel]=image
    graph.links.new(graph.p.outputs[0],output.inputs['Surface'])
    graph.nodes.remove(emitter);graph.nodes.remove(target)
    baked=Graph('Arkya V2 | '+stem+' | baked PBR 2K')
    for channel,socket in [('basecolor','Base Color'),('roughness','Roughness'),('emission','Emission Color')]:
        texture=baked.node('ShaderNodeTexImage',channel);texture.image=images[channel]
        baked.links.new(texture.outputs['Color'],baked.p.inputs[socket])
    texture=baked.node('ShaderNodeTexImage','normal');texture.image=images['normal']
    normal=baked.node('ShaderNodeNormalMap');normal.inputs['Strength'].default_value=.55
    baked.links.new(texture.outputs['Color'],normal.inputs['Color']);baked.links.new(normal.outputs[0],baked.p.inputs['Normal'])
    baked.p.inputs['Emission Strength'].default_value=1
    wood.data.materials.clear();wood.data.materials.append(baked.material)
    return images


SOURCE_PALETTE = ((.024,.071,.043),(.042,.122,.057),(.076,.194,.077),
                  (.145,.275,.105),(.18,.32,.10),(.24,.38,.12))
LEAF_PALETTES = {
    'mystique': ((.038,.102,.095),(.085,.215,.185),(.245,.365,.285)),
    'brule': ((.075,.062,.075),(.19,.12,.085),(.36,.235,.125)),
    'moussu': ((.050,.10,.082),(.13,.225,.12),(.30,.385,.185))}


def interpolate(palette, t):
    index = min(len(palette)-2,int(t*(len(palette)-1)))
    alpha = t*(len(palette)-1)-index
    return tuple(palette[index][i]*(1-alpha)+palette[index+1][i]*alpha for i in range(3))


def painted_foliage(leaves,twigs,variant):
    attribute = leaves.data.color_attributes['COLOR_0']
    values = array('f',[0])*(len(attribute.data)*4)
    attribute.data.foreach_get('color',values)
    mesh = leaves.data
    low = min(v.co.z for v in mesh.vertices)
    high = max(v.co.z for v in mesh.vertices)
    palette = LEAF_PALETTES[variant]
    for first in range(0,len(mesh.vertices),11):
        point = mesh.vertices[first+10].co
        t = max(0,min(1,(point.z-low)/(high-low)))
        broad = .07*math.sin(point.x*.95+point.y*.7)+.045*math.sin(point.y*1.2-point.z*.8)
        source_t = max(.05,min(.96,.14+.72*t+broad))
        expected = interpolate(SOURCE_PALETTE,source_t)
        original = values[(first+10)*4:(first+10)*4+3]
        # Retain the source's occlusion ordering, compressing it into a soft painted range.
        ao = max(.20,min(1,sum(original)/max(.001,sum(expected)*1.07)))
        softened = .43+.57*ao
        group = max(0,min(1,.10+.86*t+.025*math.sin(point.x*.5+point.y*.4)))
        base = interpolate(palette,group)
        for local in range(11):
            fold = 1.025 if local == 10 else 1.0
            for c in range(3):
                values[(first+local)*4+c] = base[c]*softened*fold
    attribute.data.foreach_set('color',values)
    leaves['canopy_occlusion_preserved'] = 'Original sky occlusion order retained, range compressed to 0.54–1.0; broad crown gradients'
    material = twigs.data.materials[0].copy()
    material.name = 'Arkya V2 | '+variant+' | painted branchlets'
    color = {'mystique':(.095,.085,.12,1),'brule':(.045,.05,.072,1),'moussu':(.18,.12,.07,1)}[variant]
    material.diffuse_color = color
    material.node_tree.nodes.get('Principled BSDF').inputs['Base Color'].default_value = color
    material.node_tree.nodes.get('Principled BSDF').inputs['Roughness'].default_value = .8
    twigs.data.materials.clear(); twigs.data.materials.append(material)


def generate(variant,draft=False):
    stem = VARIANTS[variant]
    folder = OUTPUT/stem
    textures = folder/'textures'
    textures.mkdir(parents=True,exist_ok=True)
    bpy.ops.wm.open_mainfile(filepath=str(BASE))
    configure(draft)
    wood = bpy.data.objects['Oak | continuous sculpted trunk roots and limbs']
    leaves = bpy.data.objects['Oak | layered connected foliage']
    twigs = bpy.data.objects['Oak | living branchlet network']
    objects = [wood,twigs,leaves]
    graph,channels = painted_material(variant)
    bake(wood,graph,channels,textures,stem)
    painted_foliage(leaves,twigs,variant)
    select(objects)
    bpy.ops.export_scene.gltf(filepath=str(folder/(stem+'.glb')),export_format='GLB',use_selection=True,
                             export_apply=True,export_materials='EXPORT',export_yup=True,
                             export_animations=False,export_cameras=False,export_lights=False,export_tangents=True)
    render_views(folder,stem,draft)
    bpy.ops.file.pack_all()
    bpy.ops.wm.save_as_mainfile(filepath=str(folder/(stem+'.blend')))
    metadata = {'id':stem,'material_version':2,'variant':variant,'source':BASE.name,
                'source_sha256':hashlib.sha256(BASE.read_bytes()).hexdigest(),
                'geometry':'Identical approved mesh positions, topology and UVs; materials only',
                'vertices':sum(len(o.data.vertices) for o in objects),
                'triangles':sum(len(p.vertices)-2 for o in objects for p in o.data.polygons),
                'mesh_count':3,'textures':{key:'textures/'+stem+'-'+key+'.png' for key in ('basecolor','normal','roughness','emission')},
                'texture_resolution':[2048,2048], 'foliage':'Original occlusion softened, coherent height palette, minimal per-leaf variation',
                'render':{'engine':'Cycles OptiX','samples':12 if draft else 32,'size':700 if draft else 1100},
                'glb_bytes':(folder/(stem+'.glb')).stat().st_size,
                'notes':'Clean painted colour fields, five broad ribbons and ten subtle painted grooves, very light normals; no fine noise, marbling or Voronoi',
                'bake':'4096 px supersampled and reduced to 2048 px; neutral prefill, no clearing, 32 px source margin; invalid tangent pixels neutralized'}
    (folder/(stem+'.json')).write_text(json.dumps(metadata,ensure_ascii=False,indent=2),encoding='utf-8')
    print('PAINTED_MATERIAL_READY',stem,flush=True)


def verify(variants):
    def fingerprint(obj):
        digest = hashlib.sha256()
        for prop,count,data,kind in [('co',len(obj.data.vertices)*3,obj.data.vertices,'f'),
                                     ('vertex_index',len(obj.data.loops),obj.data.loops,'I')]:
            values = array(kind,[0])*count; data.foreach_get(prop,values); digest.update(values.tobytes())
        for layer in obj.data.uv_layers:
            values = array('f',[0])*(len(layer.data)*2); layer.data.foreach_get('uv',values); digest.update(values.tobytes())
        return digest.hexdigest()
    bpy.ops.wm.open_mainfile(filepath=str(BASE))
    source = {o.name:fingerprint(o) for o in bpy.context.scene.objects if o.type=='MESH' and o.name.startswith('Oak |')}
    for variant in variants:
        stem = VARIANTS[variant]; folder = OUTPUT/stem
        bpy.ops.wm.open_mainfile(filepath=str(folder/(stem+'.blend')))
        same = all(fingerprint(bpy.data.objects[name])==digest for name,digest in source.items())
        blob = (folder/(stem+'.glb')).read_bytes(); length,_ = struct.unpack_from('<II',blob,12)
        gltf = json.loads(blob[20:20+length])
        mat = next(m for m in gltf['materials'] if 'baked PBR' in m.get('name',''))
        pbr = mat['pbrMetallicRoughness']
        maps = 'baseColorTexture' in pbr and 'metallicRoughnessTexture' in pbr and 'normalTexture' in mat and 'emissiveTexture' in mat
        external = any('uri' in item for item in gltf.get('buffers',[])+gltf.get('images',[]))
        color = any('COLOR_0' in p['attributes'] for m in gltf['meshes'] for p in m['primitives'])
        packed = all(i.packed_file for i in bpy.data.images if i.source=='FILE')
        assert same and maps and not external and color and packed
        path = folder/(stem+'.json'); metadata = json.loads(path.read_text(encoding='utf-8'))
        metadata['glb_bytes'] = len(blob)
        metadata['notes'] = 'Clean painted colour fields, five broad ribbons and ten subtle painted grooves, very light normals; no fine noise, marbling or Voronoi'
        metadata['bake'] = '4096 px supersampled and reduced to 2048 px; neutral prefill, no clearing, 32 px source margin; invalid tangent pixels neutralized'
        assert metadata['source_sha256']==hashlib.sha256(BASE.read_bytes()).hexdigest()
        metadata['validation'] = {'geometry_and_uv_identical_to_base':same,'blend_images_packed':bool(packed),
                                  'glb_embedded_images':len(gltf['images']),'glb_all_pbr_maps_present':maps,
                                  'glb_color_0_present':color,'glb_external_dependencies':external,'source_sha256_unchanged':True}
        path.write_text(json.dumps(metadata,ensure_ascii=False,indent=2),encoding='utf-8')
        print('VERIFIED',stem,metadata['validation'],flush=True)


if __name__=='__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--variant',choices=['all']+list(VARIANTS),default='mystique')
    parser.add_argument('--draft',action='store_true')
    parser.add_argument('--verify',action='store_true')
    args = parser.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
    variants = list(VARIANTS) if args.variant=='all' else [args.variant]
    if args.verify:
        verify(variants)
    else:
        for variant in variants:
            generate(variant,args.draft)
        verify(variants)
