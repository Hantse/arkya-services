"""Sculpt, UV, bake and export twelve painted Arkya stone props.

--geometry-only prepares four reusable meshes without using the GPU.
--shape 02-bloc-oblique --material 01-gris-bleu builds one review asset.
Rendering is opt-in with --render; --gpu enables OptiX only when coordinated.
"""
import argparse
import hashlib
import json
import math
from pathlib import Path
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Vector


ROOT=Path(__file__).resolve().parents[1]
SOURCE=ROOT.parent/'sources'/'stone-painted.png'
PAINT_CONTRAST=.75
SHAPES=[
    {'id':'01-galet','label':'Galet compact','height':.45,'intent':'Volume usé, compact et arrondi, plans doux.'},
    {'id':'02-bloc-oblique','label':'Bloc oblique','height':.90,'intent':'Sommet incliné, épaule décalée et base stable.'},
    {'id':'03-rocher-large','label':'Rocher large','height':1.60,'intent':'Masse horizontale asymétrique et ressaut latéral.'},
    {'id':'04-affleurement','label':'Affleurement','height':2.70,'intent':'Trois épaules inégales autour d’une crête oblique.'},
]
MATERIALS=[
    {'id':'01-gris-bleu','label':'Pierre gris bleu','palette':[(.085,.120,.157),(.205,.275,.315),(.385,.465,.49)]},
    {'id':'02-gres-chaud','label':'Grès chaud','palette':[(.18,.095,.045),(.38,.245,.115),(.59,.445,.255)]},
    {'id':'03-moussu','label':'Pierre moussue','palette':[(.09,.115,.09),(.225,.265,.185),(.40,.435,.29)]},
]


def select(objects):
    bpy.ops.object.select_all(action='DESELECT')
    for obj in objects:obj.hide_set(False);obj.select_set(True)
    bpy.context.view_layer.objects.active=objects[0]


def hull(name,rings,bevel):
    # Rings are authored sections, not a subdivided sphere; every shoulder has a purpose.
    vertices=[]
    pattern=[1.00,.94,1.05,.95,1.02,.91,1.04,.96]
    for z,rx,ry,cx,cy,angle,slope in rings:
        for i in range(8):
            theta=math.tau*i/8+angle
            x=math.cos(theta)*rx*pattern[i];y=math.sin(theta)*ry*pattern[(i+3)%8]
            vertices.append((x+cx,y+cy,z+slope*x))
    bm=bmesh.new()
    for point in vertices:bm.verts.new(point)
    bm.verts.ensure_lookup_table()
    result=bmesh.ops.convex_hull(bm,input=list(bm.verts),use_existing_faces=False)
    if result.get('geom_interior'):bmesh.ops.delete(bm,geom=result['geom_interior'],context='VERTS')
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
    mesh=bpy.data.meshes.new(name);bm.to_mesh(mesh);bm.free()
    obj=bpy.data.objects.new(name,mesh);bpy.context.scene.collection.objects.link(obj);select([obj])
    modifier=obj.modifiers.new('Worn rounded edges','BEVEL')
    modifier.width=bevel;modifier.segments=5;modifier.affect='EDGES';modifier.limit_method='ANGLE';modifier.angle_limit=.18
    bpy.ops.object.modifier_apply(modifier=modifier.name)
    return obj


def build_shape(shape):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sid=shape['id']
    if sid=='01-galet':
        parts=[hull('Galet | worn broad planes',[(.01,.28,.22,0,0,.13,0),(.12,.37,.29,.015,-.01,.08,.02),
                    (.29,.33,.27,.025,.025,.13,.07),(.43,.19,.18,.015,.035,.18,.06)],.072)]
    elif sid=='02-bloc-oblique':
        parts=[hull('Bloc | leaning crown',[(.01,.40,.32,0,0,.12,0),(.24,.49,.39,-.02,.01,.04,.04),
                    (.61,.46,.35,-.09,.04,.06,.24),(.92,.29,.27,-.19,.08,.13,.26)],.065)]
    elif sid=='03-rocher-large':
        parts=[hull('Rocher | main broad ridge',[(.01,.96,.72,.20,.02,.07,0),(.45,1.12,.83,.19,.05,.03,.02),
                    (1.12,.88,.73,.24,.10,.10,.14),(1.58,.51,.48,.44,.14,.14,.12)],.10),
               hull('Rocher | lower west shoulder',[(.01,.59,.55,-.86,-.07,.15,0),(.35,.71,.63,-.92,-.09,.08,-.05),
                    (.74,.61,.55,-.89,-.04,.06,-.12),(1.02,.36,.42,-.78,.02,.18,-.12)],.095),
               hull('Rocher | rear ledge',[(.04,.52,.43,.55,.58,.12,0),(.55,.61,.51,.59,.61,.16,.05),
                    (1.12,.43,.41,.60,.63,.08,.10)],.075)]
    else:
        parts=[hull('Affleurement | tall tilted spine',[(.01,.79,.63,-.49,.14,.08,0),(.64,.88,.71,-.55,.18,.10,.06),
                    (1.58,.75,.63,-.68,.24,.16,.18),(2.66,.45,.47,-.87,.31,.12,.21)],.13),
               hull('Affleurement | eastern shoulder',[(.01,.82,.71,.63,.04,.16,0),(.54,.93,.74,.68,.03,.08,.04),
                    (1.33,.77,.61,.66,.09,.13,-.10),(1.94,.43,.45,.48,.13,.15,-.12)],.12),
               hull('Affleurement | low front spur',[(.01,.56,.50,.71,-.62,.12,0),(.35,.65,.52,.75,-.68,.07,.08),
                    (.88,.39,.35,.84,-.65,.20,.10)],.09)]
    select(parts);bpy.ops.object.join();obj=bpy.context.object
    obj.name='Arkya | '+shape['label']
    if len(parts)>1:
        remesh=obj.modifiers.new('Continuous weathered stone','REMESH');remesh.mode='VOXEL'
        remesh.voxel_size=shape['height']*.012;remesh.use_smooth_shade=True
        bpy.ops.object.modifier_apply(modifier=remesh.name)
        smooth=obj.modifiers.new('Soften grown junctions','SMOOTH');smooth.factor=.62;smooth.iterations=3
        bpy.ops.object.modifier_apply(modifier=smooth.name)
        triangles=sum(len(p.vertices)-2 for p in obj.data.polygons)
        if triangles>8500:
            decimate=obj.modifiers.new('Preserve sculpted silhouette under 10k','DECIMATE');decimate.ratio=8500/triangles
            bpy.ops.object.modifier_apply(modifier=decimate.name)
    else:
        subdivision=obj.modifiers.new('Quiet rounded surface transitions','SUBSURF');subdivision.levels=1
        subdivision.subdivision_type='CATMULL_CLARK'
        bpy.ops.object.modifier_apply(modifier=subdivision.name)
    for polygon in obj.data.polygons:polygon.use_smooth=True
    points=np.asarray([v.co[:] for v in obj.data.vertices]);low=points.min(axis=0);high=points.max(axis=0)
    factor=shape['height']/(high[2]-low[2]);center=(low+high)*.5;center[2]=low[2]
    for vertex in obj.data.vertices:vertex.co=(vertex.co-Vector(center))*factor
    select([obj]);bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(70),island_margin=.018,correct_aspect=True,scale_to_bounds=True)
    bpy.ops.object.mode_set(mode='OBJECT')
    obj['Arkya_family']='cailloux';obj['Arkya_shape']=sid;obj['design_intent']=shape['intent']
    placeholder=bpy.data.materials.new('Stone | neutral geometry review');placeholder.use_nodes=True
    placeholder.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value=(.25,.32,.35,1)
    placeholder.node_tree.nodes['Principled BSDF'].inputs['Roughness'].default_value=.85
    obj.data.materials.append(placeholder)
    return obj


def configure(gpu=False):
    scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.samples=24;scene.cycles.use_denoising=True
    scene.cycles.max_bounces=5;scene.render.threads_mode='FIXED';scene.render.threads=8
    if gpu:
        prefs=bpy.context.preferences.addons['cycles'].preferences;prefs.compute_device_type='OPTIX';prefs.refresh_devices()
        for device in prefs.devices:device.use=device.type=='OPTIX'
    scene.cycles.device='GPU' if gpu else 'CPU'
    scene.view_settings.view_transform='AgX';scene.view_settings.look='AgX - Medium High Contrast'
    scene.render.image_settings.file_format='PNG';scene.render.image_settings.color_mode='RGB'
    scene.render.resolution_x=900;scene.render.resolution_y=900;scene.render.resolution_percentage=100
    bpy.context.preferences.filepaths.save_version=0
    return scene


class Graph:
    def __init__(self,name):
        self.mat=bpy.data.materials.new(name);self.mat.use_nodes=True
        self.nodes=self.mat.node_tree.nodes;self.links=self.mat.node_tree.links
        self.p=self.nodes.get('Principled BSDF');self.p.inputs['Metallic'].default_value=0
        self.p.inputs['Specular IOR Level'].default_value=.22
    def node(self,kind):return self.nodes.new(kind)
    def link(self,value,socket):
        if isinstance(value,(int,float,tuple,list)):socket.default_value=value
        else:self.links.new(value,socket)
    def math(self,op,a,b=0):
        n=self.node('ShaderNodeMath');n.operation=op;self.link(a,n.inputs[0]);self.link(b,n.inputs[1]);return n.outputs[0]
    def ramp(self,value,stops):
        n=self.node('ShaderNodeValToRGB');n.color_ramp.interpolation='EASE'
        n.color_ramp.elements.remove(n.color_ramp.elements[1])
        for i,(position,color) in enumerate(stops):
            e=n.color_ramp.elements[0] if i==0 else n.color_ramp.elements.new(position);e.position=position;e.color=(*color,1)
        self.link(value,n.inputs[0]);return n.outputs[0]
    def mix(self,factor,a,b):
        n=self.node('ShaderNodeMixRGB');self.link(factor,n.inputs[0]);self.link(a,n.inputs[1]);self.link(b,n.inputs[2]);return n.outputs[0]


def procedural(material,height):
    if not SOURCE.exists():raise FileNotFoundError('Painted source not available yet: '+str(SOURCE))
    g=Graph('Arkya | '+material['label']+' | editable painted source');g.mat.use_fake_user=True
    image=bpy.data.images.load(str(SOURCE),check_existing=True);image.pack()
    coords=g.node('ShaderNodeTexCoord');texture=g.node('ShaderNodeTexImage');texture.image=image
    texture.projection='BOX';texture.projection_blend=.22;texture.extension='REPEAT';texture.interpolation='Linear'
    g.link(coords.outputs['Generated'],texture.inputs['Vector'])
    gray=g.node('ShaderNodeRGBToBW');g.link(texture.outputs['Color'],gray.inputs[0])
    pixel=np.empty(len(image.pixels),dtype=np.float32);image.pixels.foreach_get(pixel)
    sample=pixel.reshape((-1,4))[::19,:3]
    sample=np.where(sample<=.04045,sample/12.92,((sample+.055)/1.055)**2.4)
    low,high=np.percentile(sample@np.array([.2126,.7152,.0722]),[3,97])
    remap=g.node('ShaderNodeMapRange');remap.clamp=True
    remap.inputs['From Min'].default_value=float(low);remap.inputs['From Max'].default_value=float(high)
    g.link(gray.outputs[0],remap.inputs['Value']);grain=remap.outputs['Result']
    original_color=g.ramp(grain,list(zip((.0,.48,1.),material['palette'])))
    color=g.mix(PAINT_CONTRAST,(*material['palette'][1],1),original_color)
    selected_cracks=g.ramp(grain,[(.02,(1,1,1)),(.08,(.75,.75,.75)),(.16,(0,0,0))])
    color=g.mix(g.math('MULTIPLY',selected_cracks,.60),color,original_color)
    rough=g.math('SUBTRACT',.88,g.math('MULTIPLY',grain,.12))
    if material['id']=='03-moussu':
        noise=g.node('ShaderNodeTexNoise');noise.inputs['Scale'].default_value=3.0;noise.inputs['Detail'].default_value=0
        g.link(coords.outputs['Generated'],noise.inputs['Vector'])
        geometry=g.node('ShaderNodeNewGeometry');separate=g.node('ShaderNodeSeparateXYZ');g.link(geometry.outputs['Normal'],separate.inputs[0])
        mask=g.math('ADD',noise.outputs['Fac'],g.math('MULTIPLY',g.math('MAXIMUM',separate.outputs['Z'],0),.16))
        mask=g.ramp(mask,[(.57,(0,0,0)),(.72,(.92,.92,.92))])
        moss=g.ramp(grain,[(.05,(.045,.085,.035)),(.5,(.14,.23,.075)),(.95,(.30,.37,.15))])
        color=g.mix(mask,color,moss);rough=g.mix(mask,rough,(.92,.92,.92,1))
    ao=g.node('ShaderNodeAmbientOcclusion');ao.inputs['Distance'].default_value=height*.28;ao.samples=8;ao.only_local=True
    shade=g.math('ADD',.73,g.math('MULTIPLY',ao.outputs['AO'],.27))
    multiply=g.node('ShaderNodeMixRGB');multiply.blend_type='MULTIPLY';multiply.inputs[0].default_value=1
    g.link(color,multiply.inputs[1]);g.link(shade,multiply.inputs[2]);color=multiply.outputs[0]
    bump=g.node('ShaderNodeBump');bump.inputs['Strength'].default_value=.20;bump.inputs['Distance'].default_value=height*.018
    g.link(grain,bump.inputs['Height']);g.link(color,g.p.inputs['Base Color']);g.link(rough,g.p.inputs['Roughness'])
    g.link(bump.outputs[0],g.p.inputs['Normal'])
    return g,{'basecolor':color,'normal':bump.outputs[0],'roughness':rough}


def bake(obj,graph,channels,folder,stem,material):
    folder.mkdir(parents=True,exist_ok=True);select([obj]);obj.data.materials.clear();obj.data.materials.append(graph.mat)
    for p in obj.data.polygons:p.material_index=0
    scene=bpy.context.scene;scene.cycles.samples=1;scene.render.bake.margin=24;scene.render.bake.use_clear=False
    scene.render.bake.use_selected_to_active=False;scene.render.bake.normal_space='TANGENT'
    output=graph.nodes.get('Material Output');emitter=graph.node('ShaderNodeEmission');target=graph.node('ShaderNodeTexImage');graph.nodes.active=target
    images={}
    for channel in ('basecolor','normal','roughness'):
        image=bpy.data.images.new(stem+'-'+channel,2048,2048,alpha=False)
        image.colorspace_settings.name='sRGB' if channel=='basecolor' else 'Non-Color'
        image.generated_color={'basecolor':(*material['palette'][1],1),'normal':(.5,.5,1,1),'roughness':(.82,.82,.82,1)}[channel]
        target.image=image
        if channel=='normal':graph.links.new(graph.p.outputs[0],output.inputs['Surface'])
        else:
            for link in list(emitter.inputs['Color'].links):graph.links.remove(link)
            graph.link(channels[channel],emitter.inputs['Color']);graph.links.new(emitter.outputs[0],output.inputs['Surface'])
        print('STONE_BAKE',stem,channel,flush=True)
        bpy.ops.object.bake(type='NORMAL' if channel=='normal' else 'EMIT')
        if channel=='normal':
            pixels=np.empty(len(image.pixels),dtype=np.float32);image.pixels.foreach_get(pixels);pixels=pixels.reshape((-1,4))
            invalid=(pixels[:,2]<.32)|(~np.isfinite(pixels[:,:3]).all(axis=1));pixels[invalid,:3]=(.5,.5,1)
            image.pixels.foreach_set(pixels.ravel())
        image.filepath_raw=str(folder/(stem+'-'+channel+'.png'));image.file_format='PNG';image.save();image.pack();images[channel]=image
    graph.links.new(graph.p.outputs[0],output.inputs['Surface']);graph.nodes.remove(emitter);graph.nodes.remove(target)
    baked=Graph('Arkya | '+stem+' | baked painted PBR 2K')
    for channel,socket in [('basecolor','Base Color'),('roughness','Roughness')]:
        tex=baked.node('ShaderNodeTexImage');tex.image=images[channel];baked.links.new(tex.outputs['Color'],baked.p.inputs[socket])
    tex=baked.node('ShaderNodeTexImage');tex.image=images['normal'];normal=baked.node('ShaderNodeNormalMap');normal.inputs['Strength'].default_value=.70
    baked.links.new(tex.outputs['Color'],normal.inputs['Color']);baked.links.new(normal.outputs[0],baked.p.inputs['Normal'])
    obj.data.materials.clear();obj.data.materials.append(baked.mat)


def stage(obj,height):
    scene=bpy.context.scene
    world=bpy.data.worlds.new('Stone review | neutral daylight');world.use_nodes=True
    world.node_tree.nodes['Background'].inputs['Color'].default_value=(.34,.39,.42,1);world.node_tree.nodes['Background'].inputs['Strength'].default_value=.55;scene.world=world
    target=Vector((0,0,height*.46));scale=max(height,1.0)
    for name,pos,energy,size,color in [('Key',(-3,-4,6),500,4,(1,.92,.80)),('Fill',(4,-1,3),240,3,(.76,.87,1)),('Rim',(0,4,5),450,3,(1,.97,.87))]:
        data=bpy.data.lights.new(name,'AREA');data.energy=energy*scale*scale;data.size=size*scale;data.color=color
        light=bpy.data.objects.new(name,data);scene.collection.objects.link(light);light.location=Vector(pos)*scale
        light.rotation_euler=(target-light.location).to_track_quat('-Z','Y').to_euler()
    bpy.ops.mesh.primitive_plane_add(size=100*scale,location=(0,0,-.015))
    floor=bpy.context.object;floor.name='Review only | floor';mat=bpy.data.materials.new('Review only | light gray');mat.use_nodes=True
    mat.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value=(.38,.42,.41,1);mat.node_tree.nodes['Principled BSDF'].inputs['Roughness'].default_value=.95;floor.data.materials.append(mat)
    data=bpy.data.cameras.new('Stone review camera');data.type='ORTHO'
    camera=bpy.data.objects.new(data.name,data);scene.collection.objects.link(camera)
    camera.location=target+Vector((2.5,-3.8,2.4))*scale;camera.rotation_euler=(target-camera.location).to_track_quat('-Z','Y').to_euler()
    points=np.asarray([v.co[:] for v in obj.data.vertices]);projected=points@np.asarray(camera.rotation_euler.to_matrix())
    data.ortho_scale=max(float(np.ptp(projected[:,0])),float(np.ptp(projected[:,1])))*1.28
    scene.camera=camera
    return scene


def stats(obj):
    points=np.asarray([v.co[:] for v in obj.data.vertices]);size=points.max(axis=0)-points.min(axis=0)
    return {'height':round(float(size[2]),4),'width':round(float(size[0]),4),'depth':round(float(size[1]),4),
            'triangles':sum(len(p.vertices)-2 for p in obj.data.polygons),'vertices':len(obj.data.vertices)}


def manifest():
    assets=[]
    for shape in SHAPES:
        for material in MATERIALS:
            stem=shape['id']+'-'+material['id'];folder=ROOT/shape['id']/material['id'];path=folder/(stem+'.json')
            if path.exists():assets.append(json.loads(path.read_text(encoding='utf-8')))
    value={'family':'cailloux','shapes':SHAPES,'materials':MATERIALS,'assets':assets}
    (ROOT/'manifest.json').write_text(json.dumps(value,indent=2,ensure_ascii=False),encoding='utf-8')


def comparison(gpu=False):
    bpy.ops.wm.read_factory_settings(use_empty=True);configure(gpu)
    groups=[]
    for shape in SHAPES:
        stem=shape['id']+'-01-gris-bleu';path=ROOT/shape['id']/'01-gris-bleu'/(stem+'.blend')
        name='Arkya | '+shape['label']
        with bpy.data.libraries.load(str(path),link=False) as (source,target):target.objects=[name]
        obj=target.objects[0];bpy.context.scene.collection.objects.link(obj)
        groups.append((shape,obj,stats(obj)['width']))
    total=sum(width for _,_,width in groups)+.65*(len(groups)-1);cursor=-total*.5
    for shape,obj,width in groups:obj.location.x=cursor+width*.5;cursor+=width+.65
    scene=stage(groups[-1][1],2.7);camera=scene.camera;target=Vector((0,0,1.40))
    camera.location=(0,-14,6);camera.rotation_euler=(target-camera.location).to_track_quat('-Z','Y').to_euler()
    camera.data.ortho_scale=total*1.22
    scene.render.resolution_x=2400;scene.render.resolution_y=1050;scene.cycles.samples=32
    for obj in bpy.data.objects:
        if obj.type=='LIGHT':obj.data.size=13;obj.data.energy*=1.8
    ink=bpy.data.materials.new('Comparison | lettering');ink.use_nodes=True;ink.node_tree.nodes.clear()
    output=ink.node_tree.nodes.new('ShaderNodeOutputMaterial');emission=ink.node_tree.nodes.new('ShaderNodeEmission')
    emission.inputs['Color'].default_value=(.018,.026,.025,1);ink.node_tree.links.new(emission.outputs[0],output.inputs[0])
    def label(body,location,size):
        curve=bpy.data.curves.new(body,'FONT');curve.body=body;curve.align_x='CENTER';curve.align_y='CENTER';curve.size=size
        obj=bpy.data.objects.new(body,curve);scene.collection.objects.link(obj);obj.location=location;obj.rotation_euler=camera.rotation_euler;curve.materials.append(ink)
    for shape,obj,width in groups:label(shape['label']+' · '+str(shape['height']).replace('.',',')+' m',(obj.location.x,-2,.14),.14)
    label('ARKYA  /  QUATRE FORMES DE PIERRE',(0,.4,3.55),.22)
    label('Même échelle · pierre gris bleu',(0,.4,3.24),.14)
    scene.render.filepath=str(ROOT/'comparaison-tailles.png')
    bpy.ops.file.pack_all();bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'comparaison-tailles.blend'),compress=True)
    bpy.ops.render.render(write_still=True)


def main():
    parser=argparse.ArgumentParser();parser.add_argument('--geometry-only',action='store_true');parser.add_argument('--render',action='store_true');parser.add_argument('--gpu',action='store_true')
    parser.add_argument('--comparison-only',action='store_true')
    parser.add_argument('--shape',choices=[s['id'] for s in SHAPES]);parser.add_argument('--material',choices=[m['id'] for m in MATERIALS])
    args=parser.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
    ROOT.mkdir(parents=True,exist_ok=True)
    if args.comparison_only:comparison(args.gpu);return
    for shape in SHAPES:
        if args.shape and args.shape!=shape['id']:continue
        if args.geometry_only:
            obj=build_shape(shape);configure(False)
            folder=ROOT/'geometry';folder.mkdir(exist_ok=True)
            bpy.ops.wm.save_as_mainfile(filepath=str(folder/(shape['id']+'.blend')),compress=True)
            print('STONE_GEOMETRY_READY',shape['id'],json.dumps(stats(obj)),flush=True)
            continue
        for material in MATERIALS:
            if args.material and args.material!=material['id']:continue
            obj=build_shape(shape);configure(args.gpu)
            stem=shape['id']+'-'+material['id'];folder=ROOT/shape['id']/material['id'];folder.mkdir(parents=True,exist_ok=True)
            graph,channels=procedural(material,shape['height']);bake(obj,graph,channels,folder/'textures',stem,material)
            select([obj]);bpy.ops.export_scene.gltf(filepath=str(folder/(stem+'.glb')),export_format='GLB',use_selection=True,
                export_apply=True,export_materials='EXPORT',export_yup=True,export_animations=False,export_cameras=False,export_lights=False,
                export_vertex_color='NAME',export_vertex_color_name='COLOR_0',export_all_vertex_colors=False)
            data={'id':stem,'shape':shape['id'],'material':material['id'],'label':shape['label']+' · '+material['label'],
                  'glb':str((folder/(stem+'.glb')).relative_to(ROOT)).replace('\\','/'),
                  'blend':str((folder/(stem+'.blend')).relative_to(ROOT)).replace('\\','/'),
                  'preview':str((folder/(stem+'-preview.png')).relative_to(ROOT)).replace('\\','/'),**stats(obj),
                  'textures_size':[2048,2048],'paint_contrast':PAINT_CONTRAST,'source':'../sources/stone-painted.png','source_sha256':hashlib.sha256(SOURCE.read_bytes()).hexdigest(),
                  'mesh_count':1,'pivot':'Centered X/Y, lowest point Z=0','gltf_up_axis':'Y'}
            scene=stage(obj,shape['height']);scene.cycles.samples=32
            bpy.ops.file.pack_all();bpy.ops.wm.save_as_mainfile(filepath=str(folder/(stem+'.blend')),compress=True)
            (folder/(stem+'.json')).write_text(json.dumps(data,indent=2,ensure_ascii=False),encoding='utf-8');manifest()
            print('STONE_ASSET_READY',stem,json.dumps(stats(obj)),flush=True)
            if args.render:
                scene.render.filepath=str(folder/(stem+'-preview.png'));bpy.ops.render.render(write_still=True)
    manifest()


if __name__=='__main__':main()
