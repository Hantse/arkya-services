"""Painted-image bark and volumetric canopy study; original mesh remains immutable."""
import argparse
import ast
from array import array
import hashlib
import importlib
import importlib.util
import json
import math
from pathlib import Path
import struct
import sys

import bpy
import numpy as np
from mathutils import Vector
from mathutils.kdtree import KDTree

sys.dont_write_bytecode = True
sys.path.insert(0,str(Path(__file__).resolve().parent))
from generate_materials import Graph, ROOT, BASE, VARIANTS, configure, render_views, select
from generate_materials_v2 import map_range

OUTPUT=ROOT/'matieres-v3'
SOURCE=OUTPUT/'source'/'painted-bark.png'
SIZE=4096


def orient_bark(wood):
    """Recover only the original centerlines, then attach paint coordinates; no mesh edits."""
    path=BASE.parent/'build_tree_structure_v2.py'
    spec=importlib.util.spec_from_file_location('approved_structure_for_flow',path)
    module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)
    tree=ast.parse(path.read_text(encoding='utf-8'))
    function=next(node for node in tree.body if isinstance(node,ast.FunctionDef) and node.name=='build_structure')
    body=[]
    for statement in function.body:
        if isinstance(statement,ast.Assign):
            names=[t.id for t in statement.targets if isinstance(t,ast.Name)]
            if 'mesh' in names:break
            if 'collection' in names:continue
        if isinstance(statement,ast.Expr) and 'collection.children.link' in ast.unparse(statement):continue
        body.append(statement)
    paths=[]
    def capture(vertices,faces,points,radii,**kwargs):
        samples=module._spline(points,radii,kwargs.get('steps',7))
        paths.append((samples,kwargs.get('root',False)))
        return samples
    function.body=body
    scope={'_tube':capture,'_at':module._at,'Vector':Vector,'math':math}
    exec(compile(ast.fix_missing_locations(ast.Module(body=[function],type_ignores=[])),str(path),'exec'),scope)
    scope['build_structure']()
    samples=[]
    for path_id,(line,is_root) in enumerate(paths):
        arc=float(line[0][0].z);previous=None;side=Vector((1,0,0))
        for index,(center,radius) in enumerate(line):
            tangent=(line[min(index+1,len(line)-1)][0]-line[max(0,index-1)][0]).normalized()
            side=side-tangent*side.dot(tangent)
            if side.length<.01:side=tangent.cross(Vector((0,1,0)))
            side.normalize();across=tangent.cross(side).normalized()
            if previous is not None:arc+=(center-previous).length
            samples.append((center.copy(),radius,path_id,tangent.copy(),side.copy(),across,arc,is_root))
            previous=center
    tree=KDTree(len(samples))
    for index,item in enumerate(samples):tree.insert(item[0],index)
    tree.balance()
    count=len(wood.data.vertices)
    fields={name:np.empty((count,3),dtype=np.float32) for name in ('BarkFlowAU','BarkFlowAV','BarkFlowBU','BarkFlowBV')}
    weights={name:np.empty(count,dtype=np.float32) for name in ('BarkFlowAWeight','BarkFlowBWeight','BarkFlowBranchMix')}
    for vertex in wood.data.vertices:
        point=vertex.co;normal=vertex.normal;candidates={}
        for _,index,distance in tree.find_n(point,32):
            center,radius,path_id,tangent,side,across,arc,is_root=samples[index]
            delta=point-center;along=delta.dot(tangent)
            transverse=delta-tangent*along
            score=transverse.length/max(.03,radius)
            score+=abs(along)*.25
            if path_id not in candidates or score<candidates[path_id][0]:
                candidates[path_id]=(score,index,along)
        best=sorted(candidates.values())[:2]
        if len(best)==1:best.append(best[0])
        for label,(_,index,along) in zip(('A','B'),best):
            center,radius,path_id,tangent,side,across,arc,is_root=samples[index]
            delta=point-center
            fields['BarkFlow'+label+'U'][vertex.index]=(delta.dot(side)*.40,(arc+along)*.31,0)
            fields['BarkFlow'+label+'V'][vertex.index]=(delta.dot(across)*.40,(arc+along)*.31,0)
            a=abs(normal.dot(side))**3;b=abs(normal.dot(across))**3
            weights['BarkFlow'+label+'Weight'][vertex.index]=a/max(.0001,a+b)
        relative=((best[0][0]+.05)/(best[1][0]+.05))**7
        weights['BarkFlowBranchMix'][vertex.index]=relative/(1+relative)
    for name,values in fields.items():
        attribute=wood.data.attributes.new(name=name,type='FLOAT_VECTOR',domain='POINT')
        attribute.data.foreach_set('vector',values.ravel())
    for name,values in weights.items():
        attribute=wood.data.attributes.new(name=name,type='FLOAT',domain='POINT')
        attribute.data.foreach_set('value',values)
    print('BARK_FLOW_READY',len(paths),'centerlines',count,'vertices',flush=True)


def flow_texture(g,image):
    def attribute(name):
        node=g.node('ShaderNodeAttribute');node.attribute_name=name
        return node
    def projection(name):
        split=g.node('ShaderNodeSeparateXYZ');g.connect(attribute(name).outputs['Vector'],split.inputs[0])
        combine=g.node('ShaderNodeCombineXYZ')
        for axis in ('X','Y'):
            g.connect(g.math('PINGPONG',split.outputs[axis],1),combine.inputs[axis])
        texture=g.node('ShaderNodeTexImage','Paint flows along the approved branch')
        texture.image=image;texture.interpolation='Linear';texture.extension='EXTEND'
        g.connect(combine.outputs[0],texture.inputs['Vector'])
        return texture.outputs['Color']
    colors=[]
    for branch in ('A','B'):
        u=projection('BarkFlow'+branch+'U');v=projection('BarkFlow'+branch+'V')
        colors.append(g.mix(attribute('BarkFlow'+branch+'Weight').outputs['Fac'],u,v))
    return g.mix(attribute('BarkFlowBranchMix').outputs['Fac'],colors[0],colors[1])


def island(g,position,center,radius):
    node=g.node('ShaderNodeVectorMath');node.operation='DISTANCE'
    g.connect(position,node.inputs[0]);node.inputs[1].default_value=center
    return map_range(g,node.outputs['Value'],radius*.35,radius,1,0)


def painted_material(variant):
    if not SOURCE.exists():raise FileNotFoundError(str(SOURCE))
    g=Graph('Arkya V3 | '+variant+' | painted image authoring')
    g.material.use_fake_user=True
    image=bpy.data.images.load(str(SOURCE),check_existing=True)
    image.pack()
    coords=g.node('ShaderNodeTexCoord')
    position=coords.outputs['Object']
    painted=flow_texture(g,image)
    gray=g.node('ShaderNodeRGBToBW');g.connect(painted,gray.inputs[0])
    pixels=np.empty(len(image.pixels),dtype=np.float32);image.pixels.foreach_get(pixels)
    samples=pixels.reshape((-1,4))[::17,:3]
    # Image.pixels is stored sRGB here; the shader's image output is scene-linear.
    samples=np.where(samples<=.04045,samples/12.92,((samples+.055)/1.055)**2.4)
    luminance=samples@np.array([.2126,.7152,.0722])
    bottom,top=np.percentile(luminance,[3,97])
    grain=map_range(g,gray.outputs[0],float(bottom),float(top))
    grooves=map_range(g,grain,.10,.29,1,0)
    region=0.0
    for center,radius in [((.35,-.55,1.45),.85), ((-.3,-.65,3.0),.85),
                          ((.35,.5,4.3),1.0), ((-1.7,.25,4.2),.75)]:
        region=g.math('MAXIMUM',region,island(g,position,center,radius))
    accent=g.math('MULTIPLY',grooves,region)
    emission=(0,0,0,1)
    if variant=='mystique':
        color=g.ramp(grain,[(0,(.038,.024,.041,1)),(.20,(.075,.043,.066,1)),
                            (.46,(.155,.089,.112,1)),(.72,(.265,.167,.17,1)),
                            (1,(.39,.285,.24,1))])
        color=g.mix(g.math('MULTIPLY',accent,.88),color,(.055,.32,.26,1))
        emission=g.mix(accent,(0,0,0,1),(.012,.085,.060,1))
        roughness=map_range(g,grain,0,1,.87,.69)
    elif variant=='brule':
        color=g.ramp(grain,[(0,(.017,.020,.029,1)),(.22,(.035,.042,.061,1)),
                            (.48,(.073,.079,.102,1)),(.76,(.14,.14,.16,1)),
                            (1,(.23,.20,.18,1))])
        color=g.mix(accent,color,(.33,.12,.025,1))
        emission=g.mix(accent,(0,0,0,1),(.20,.045,.006,1))
        roughness=map_range(g,grain,0,1,.87,.74)
    else:
        color=g.ramp(grain,[(0,(.065,.041,.023,1)),(.22,(.13,.076,.035,1)),
                            (.48,(.25,.155,.065,1)),(.76,(.41,.285,.12,1)),
                            (1,(.55,.41,.21,1))])
        broad=g.noise(g.scale(position,(.62,.62,.42)),1,0,.5)
        eroded=g.math('ADD',broad,g.math('MULTIPLY',grain,.12))
        moss=g.ramp(eroded,[(.62,(0,0,0,1)),(.68,(.94,.94,.94,1))])
        moss_color=g.ramp(grain,[(.08,(.045,.085,.032,1)),(.45,(.12,.205,.065,1)),
                                 (.87,(.28,.345,.15,1))])
        color=g.mix(moss,color,moss_color)
        roughness=g.mix(moss,(.74,.74,.74,1),(.91,.91,.91,1))
    g.connect(color,g.p.inputs['Base Color']);g.connect(roughness,g.p.inputs['Roughness'])
    g.connect(emission,g.p.inputs['Emission Color']);g.p.inputs['Emission Strength'].default_value=1
    bump=g.node('ShaderNodeBump')
    bump.inputs['Strength'].default_value=.27;bump.inputs['Distance'].default_value=.038
    g.connect(grain,bump.inputs['Height']);g.connect(bump.outputs[0],g.p.inputs['Normal'])
    return g,{'basecolor':color,'normal':bump.outputs[0],'roughness':roughness,'emission':emission}


def baked_material(wood,images,stem):
    baked=Graph('Arkya V3 | '+stem+' | baked PBR 4K')
    for channel,socket in [('basecolor','Base Color'),('roughness','Roughness'),('emission','Emission Color')]:
        texture=baked.node('ShaderNodeTexImage',channel);texture.image=images[channel]
        baked.links.new(texture.outputs['Color'],baked.p.inputs[socket])
    texture=baked.node('ShaderNodeTexImage','normal');texture.image=images['normal']
    normal=baked.node('ShaderNodeNormalMap');normal.inputs['Strength'].default_value=.8
    baked.links.new(texture.outputs['Color'],normal.inputs['Color'])
    baked.links.new(normal.outputs[0],baked.p.inputs['Normal'])
    baked.p.inputs['Emission Strength'].default_value=1
    wood.data.materials.clear();wood.data.materials.append(baked.material)


def bake(wood,graph,channels,folder,stem,reuse=False):
    if reuse:
        images={key:bpy.data.images.load(str(folder/(stem+'-'+key+'.png')),check_existing=False)
                for key in ('basecolor','normal','roughness','emission')}
        for key,image in images.items():
            image.colorspace_settings.name='Non-Color' if key in ('normal','roughness') else 'sRGB'
            image.pack()
        baked_material(wood,images,stem)
        return
    select([wood]);wood.data.materials.clear();wood.data.materials.append(graph.material)
    for polygon in wood.data.polygons:polygon.material_index=0
    scene=bpy.context.scene;scene.cycles.samples=1
    scene.render.bake.margin=32;scene.render.bake.use_selected_to_active=False
    scene.render.bake.normal_space='TANGENT';scene.render.bake.use_clear=False
    output=graph.nodes.get('Material Output');emitter=graph.node('ShaderNodeEmission')
    target=graph.node('ShaderNodeTexImage','4K atlas, original wood UV')
    graph.nodes.active=target;images={}
    neutral={'01-mystique':(.155,.089,.112,1),'02-brule':(.073,.079,.102,1),'03-moussu':(.25,.155,.065,1)}[stem]
    for channel in ('basecolor','normal','roughness','emission'):
        image=bpy.data.images.new(stem+'-V3-'+channel,SIZE,SIZE,alpha=False)
        image.colorspace_settings.name='Non-Color' if channel in ('normal','roughness') else 'sRGB'
        image.generated_color={'basecolor':neutral,'normal':(.5,.5,1,1),'roughness':(.78,.78,.78,1),'emission':(0,0,0,1)}[channel]
        if channel=='normal':graph.links.new(graph.p.outputs[0],output.inputs['Surface'])
        else:
            for link in list(emitter.inputs['Color'].links):graph.links.remove(link)
            graph.connect(channels[channel],emitter.inputs['Color'])
            graph.links.new(emitter.outputs[0],output.inputs['Surface'])
        target.image=image;print('V3_BAKE',stem,channel,flush=True)
        bpy.ops.object.bake(type='NORMAL' if channel=='normal' else 'EMIT')
        if channel=='normal':
            pixels=np.empty(SIZE*SIZE*4,dtype=np.float32);image.pixels.foreach_get(pixels)
            pixels=pixels.reshape((-1,4))
            invalid=(pixels[:,2]<.72)|(np.abs(pixels[:,0]-.5)>.39)|(np.abs(pixels[:,1]-.5)>.39)
            pixels[invalid,:3]=(.5,.5,1);image.pixels.foreach_set(pixels.ravel())
            print('INVALID_TANGENT_PIXELS',int(invalid.sum()),flush=True)
        image.filepath_raw=str(folder/(stem+'-'+channel+'.png'))
        image.file_format='PNG';image.save();image.pack();images[channel]=image
    graph.links.new(graph.p.outputs[0],output.inputs['Surface'])
    graph.nodes.remove(emitter);graph.nodes.remove(target)
    baked_material(wood,images,stem)


def generate(variant,draft=False,reuse=False):
    stem=VARIANTS[variant];folder=OUTPUT/stem;textures=folder/'textures'
    textures.mkdir(parents=True,exist_ok=True)
    bpy.ops.wm.open_mainfile(filepath=str(BASE));configure(draft)
    wood=bpy.data.objects['Oak | continuous sculpted trunk roots and limbs']
    leaves=bpy.data.objects['Oak | layered connected foliage'];twigs=bpy.data.objects['Oak | living branchlet network']
    orient_bark(wood)
    graph,channels=painted_material(variant)
    bake(wood,graph,channels,textures,stem,reuse)
    foliage=importlib.import_module('foliage_v3')
    foliage_stats=foliage.apply_foliage(leaves,twigs,variant)
    for image in bpy.data.images:
        if image.name.startswith('Arkya') and image.source=='GENERATED':
            image.filepath_raw=str(textures/(image.name.replace(' ','-').replace('|','')+'.png'))
            image.file_format='PNG';image.save();image.pack()
    objects=[wood,twigs,leaves];select(objects)
    bpy.ops.export_scene.gltf(filepath=str(folder/(stem+'.glb')),export_format='GLB',use_selection=True,
                             export_apply=True,export_materials='EXPORT',export_yup=True,
                             export_animations=False,export_cameras=False,export_lights=False,export_tangents=True,
                             export_vertex_color='NAME',export_vertex_color_name='COLOR_0',export_all_vertex_colors=False)
    render_views(folder,stem,draft)
    bpy.ops.file.pack_all();bpy.ops.wm.save_as_mainfile(filepath=str(folder/(stem+'.blend')))
    metadata={'id':stem,'material_version':3,'variant':variant,'source':BASE.name,
              'source_sha256':hashlib.sha256(BASE.read_bytes()).hexdigest(),
              'painted_source':'../source/painted-bark.png','painted_source_sha256':hashlib.sha256(SOURCE.read_bytes()).hexdigest(),
              'geometry':'Approved positions and topology unchanged; original wood UV preserved',
              'vertices':sum(len(o.data.vertices) for o in objects),
              'triangles':sum(len(p.vertices)-2 for o in objects for p in o.data.polygons),'mesh_count':3,
              'textures':{key:'textures/'+stem+'-'+key+'.png' for key in ('basecolor','normal','roughness','emission')},
              'texture_resolution':[SIZE,SIZE],'foliage':foliage_stats,
              'render':{'engine':'Cycles OptiX','samples':12 if draft else 32,'size':700 if draft else 1100},
              'glb_bytes':(folder/(stem+'.glb')).stat().st_size,
              'notes':'Painted bitmap carries full bark structure; softened image normal; accents follow its crevices; centerline-oriented projection blended at forks'}
    (folder/(stem+'.json')).write_text(json.dumps(metadata,ensure_ascii=False,indent=2),encoding='utf-8')
    print('V3_READY',stem,flush=True)


def verify(variants):
    def fingerprint(obj,uv=False):
        digest=hashlib.sha256()
        if uv:
            for layer in obj.data.uv_layers:
                values=array('f',[0])*(len(layer.data)*2);layer.data.foreach_get('uv',values);digest.update(values.tobytes())
        else:
            for prop,count,data,kind in [('co',len(obj.data.vertices)*3,obj.data.vertices,'f'),
                                         ('vertex_index',len(obj.data.loops),obj.data.loops,'I')]:
                values=array(kind,[0])*count;data.foreach_get(prop,values);digest.update(values.tobytes())
        return digest.hexdigest()
    bpy.ops.wm.open_mainfile(filepath=str(BASE))
    source={o.name:fingerprint(o) for o in bpy.context.scene.objects if o.type=='MESH' and o.name.startswith('Oak |')}
    wood_uv=fingerprint(bpy.data.objects['Oak | continuous sculpted trunk roots and limbs'],True)
    for variant in variants:
        stem=VARIANTS[variant];folder=OUTPUT/stem
        bpy.ops.wm.open_mainfile(filepath=str(folder/(stem+'.blend')))
        same=all(fingerprint(bpy.data.objects[name])==digest for name,digest in source.items())
        same_uv=wood_uv==fingerprint(bpy.data.objects['Oak | continuous sculpted trunk roots and limbs'],True)
        blob=(folder/(stem+'.glb')).read_bytes();length,_=struct.unpack_from('<II',blob,12)
        gltf=json.loads(blob[20:20+length]);mat=next(m for m in gltf['materials'] if 'baked PBR' in m.get('name',''))
        pbr=mat['pbrMetallicRoughness']
        maps='baseColorTexture' in pbr and 'metallicRoughnessTexture' in pbr and 'normalTexture' in mat and 'emissiveTexture' in mat
        external=any('uri' in item for item in gltf.get('buffers',[])+gltf.get('images',[]))
        color=any('COLOR_0' in p['attributes'] for m in gltf['meshes'] for p in m['primitives'])
        color_means=[]
        leaf_texture=False
        for mesh in gltf['meshes']:
            for primitive in mesh['primitives']:
                if 'COLOR_0' not in primitive['attributes']:continue
                accessor=gltf['accessors'][primitive['attributes']['COLOR_0']]
                view=gltf['bufferViews'][accessor['bufferView']]
                dtype=np.dtype({5126:'<f4',5123:'<u2',5121:'u1'}[accessor['componentType']])
                components={'VEC3':3,'VEC4':4}[accessor['type']]
                offset=28+length+view.get('byteOffset',0)+accessor.get('byteOffset',0)
                stride=view.get('byteStride',dtype.itemsize*components)
                values=np.ndarray((accessor['count'],components),dtype=dtype,buffer=blob,offset=offset,
                                  strides=(stride,dtype.itemsize))[:,:3].astype(np.float64)
                if accessor['componentType']!=5126:values/=np.iinfo(dtype).max
                mean=values.mean(axis=0)
                assert not np.all(mean>.98) and values.std()>.01,'White or flat leaf COLOR_0'
                color_means.append([round(float(v),6) for v in mean])
                leaf_material=gltf['materials'][primitive['material']]
                leaf_texture=('baseColorTexture' in leaf_material.get('pbrMetallicRoughness',{})
                              and 'TEXCOORD_0' in primitive['attributes'])
        packed=all(i.packed_file for i in bpy.data.images if i.source=='FILE')
        assert same and same_uv and maps and not external and color and packed and leaf_texture
        path=folder/(stem+'.json');metadata=json.loads(path.read_text(encoding='utf-8'))
        assert metadata['source_sha256']==hashlib.sha256(BASE.read_bytes()).hexdigest()
        metadata['glb_bytes']=len(blob)
        metadata['validation']={'geometry_identical_to_base':same,'wood_uv_identical_to_base':same_uv,
                                'blend_images_packed':bool(packed),'glb_embedded_images':len(gltf['images']),
                                'glb_all_pbr_maps_present':maps,'glb_color_0_present':color,
                                'glb_leaf_vertex_rgb_mean':color_means,'glb_leaf_texture_and_uv_present':leaf_texture,
                                'glb_external_dependencies':external,'source_sha256_unchanged':True}
        path.write_text(json.dumps(metadata,ensure_ascii=False,indent=2),encoding='utf-8')
        print('VERIFIED_V3',stem,metadata['validation'],flush=True)


if __name__=='__main__':
    parser=argparse.ArgumentParser()
    parser.add_argument('--variant',choices=['all']+list(VARIANTS),default='mystique')
    parser.add_argument('--draft',action='store_true');parser.add_argument('--verify',action='store_true')
    parser.add_argument('--reuse-bakes',action='store_true')
    args=parser.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
    variants=list(VARIANTS) if args.variant=='all' else [args.variant]
    if args.verify:verify(variants)
    else:
        for variant in variants:generate(variant,args.draft,args.reuse_bakes)
        verify(variants)
